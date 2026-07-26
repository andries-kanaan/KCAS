using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientOperationalVerificationService(ApplicationDbContext db)
{
    public async Task<List<ClientOperationalPortfolioItem>> LoadPortfolioAsync(string? lifecycleStatus = null)
    {
        var query = db.Clients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(lifecycleStatus))
        {
            query = query.Where(client => client.LifecycleStatus == lifecycleStatus);
        }

        return await query
            .OrderBy(client => client.DisplayName)
            .Select(client => new ClientOperationalPortfolioItem(
                client.Id,
                client.KanaanId,
                client.DisplayName,
                client.LifecycleStatus,
                client.VerificationItems.Count(item => item.Status == ClientVerificationStatuses.Pending),
                client.VerificationItems.Count(item =>
                    item.Status == ClientVerificationStatuses.Pending && item.IsBlocking)))
            .ToListAsync();
    }

    public async Task<ClientOperationalReviewModel> LoadClientAsync(int clientId)
    {
        var client = await db.Clients.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new KeyNotFoundException("Client not found.");
        var items = await db.ClientVerificationItems.AsNoTracking()
            .Where(item => item.ClientId == clientId)
            .OrderBy(item => item.Status == ClientVerificationStatuses.Pending ? 0 : 1)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ToListAsync();
        return new ClientOperationalReviewModel { Client = client, VerificationItems = items };
    }

    public async Task ClassifyLifecycleAsync(
        int clientId,
        ClientLifecycleReviewRequest request,
        string? userName)
    {
        var user = RequireUser(userName);
        RequireReason(request.Reason);
        if (!ClientLifecycleStatuses.All.Contains(request.Status) ||
            request.Status == ClientLifecycleStatuses.Unreviewed)
        {
            throw new ValidationException("Select a final lifecycle classification.");
        }

        var client = await db.Clients.SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new KeyNotFoundException("Client not found.");
        if (request.Status == ClientLifecycleStatuses.Duplicate)
        {
            if (!request.DuplicateOfClientId.HasValue || request.DuplicateOfClientId == clientId ||
                !await db.Clients.AnyAsync(item => item.Id == request.DuplicateOfClientId.Value))
            {
                throw new ValidationException("A duplicate must identify a different existing client.");
            }
        }

        var oldValue = JsonSerializer.Serialize(new
        {
            client.LifecycleStatus,
            client.DuplicateOfClientId,
            client.IsActive
        });
        client.LifecycleStatus = request.Status;
        client.DuplicateOfClientId = request.Status == ClientLifecycleStatuses.Duplicate
            ? request.DuplicateOfClientId
            : null;
        client.LifecycleReason = request.Reason.Trim();
        client.LifecycleReviewedAtUtc = DateTime.UtcNow;
        client.LifecycleReviewedBy = user;
        client.IsActive = request.Status == ClientLifecycleStatuses.Current;
        client.UpdatedAtUtc = DateTime.UtcNow;
        db.ComplianceAuditEvents.Add(CreateAudit(
            client.Id,
            "LifecycleClassified",
            user,
            request.Reason,
            oldValue,
            JsonSerializer.Serialize(new
            {
                client.LifecycleStatus,
                client.DuplicateOfClientId,
                client.IsActive
            })));
        await db.SaveChangesAsync();
    }

    public async Task<int> AddVerificationItemAsync(
        int clientId,
        ClientVerificationCreateRequest request,
        string? userName)
    {
        var user = RequireUser(userName);
        if (!ClientVerificationFields.Labels.TryGetValue(request.FieldCode, out var fieldLabel))
        {
            throw new ValidationException("Select a supported client field.");
        }
        if (request.ChangeType is not (ClientVerificationChangeTypes.ConfirmExisting or ClientVerificationChangeTypes.Replace))
        {
            throw new ValidationException("Select a supported verification action.");
        }
        if (string.IsNullOrWhiteSpace(request.SourceReference))
        {
            throw new ValidationException("The source document or reference is required.");
        }
        RequireReason(request.Recommendation);

        var client = await db.Clients.SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new KeyNotFoundException("Client not found.");
        var existingValue = ReadField(client, request.FieldCode);
        var proposedValue = request.ChangeType == ClientVerificationChangeTypes.ConfirmExisting
            ? existingValue
            : Normalize(request.ProposedValue);
        if (request.ChangeType == ClientVerificationChangeTypes.Replace &&
            string.Equals(existingValue, proposedValue, StringComparison.Ordinal))
        {
            throw new ValidationException("The proposed replacement is the same as the current value.");
        }

        var item = new ClientVerificationItem
        {
            ClientId = clientId,
            FieldCode = request.FieldCode,
            FieldLabel = fieldLabel,
            ChangeType = request.ChangeType,
            ExistingValue = existingValue,
            ProposedValue = proposedValue,
            SourceReference = request.SourceReference.Trim(),
            Recommendation = request.Recommendation.Trim(),
            IsBlocking = request.IsBlocking,
            CreatedBy = user
        };
        db.ClientVerificationItems.Add(item);
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(CreateAudit(
            item.Id,
            "VerificationProposed",
            user,
            request.Recommendation,
            null,
            JsonSerializer.Serialize(new
            {
                item.ClientId,
                item.FieldCode,
                item.ChangeType,
                item.ExistingValue,
                item.ProposedValue,
                item.SourceReference,
                item.IsBlocking
            }),
            nameof(ClientVerificationItem)));
        await db.SaveChangesAsync();
        return item.Id;
    }

    public Task VerifyAsync(int itemId, string? userName, string reason) =>
        DecideAsync(itemId, true, userName, reason);

    public Task RejectAsync(int itemId, string? userName, string reason) =>
        DecideAsync(itemId, false, userName, reason);

    private async Task DecideAsync(int itemId, bool verify, string? userName, string reason)
    {
        var user = RequireUser(userName);
        RequireReason(reason);
        var item = await db.ClientVerificationItems
            .Include(entry => entry.Client)
            .SingleOrDefaultAsync(entry => entry.Id == itemId)
            ?? throw new KeyNotFoundException("Verification item not found.");
        if (item.Status != ClientVerificationStatuses.Pending)
        {
            throw new InvalidOperationException("This verification item has already been decided.");
        }

        var currentValue = ReadField(item.Client, item.FieldCode);
        if (!string.Equals(currentValue, item.ExistingValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The client value changed after this recommendation was created. Create a fresh verification item.");
        }

        var oldValue = JsonSerializer.Serialize(new { item.Status, CurrentValue = currentValue });
        if (verify && item.ChangeType == ClientVerificationChangeTypes.Replace)
        {
            if (item.FieldCode == ClientVerificationFields.KanaanId &&
                !string.IsNullOrWhiteSpace(item.ProposedValue) &&
                await db.Clients.AnyAsync(client =>
                    client.Id != item.ClientId && client.KanaanId == item.ProposedValue))
            {
                throw new ValidationException("The proposed Kanaan ID is already assigned to another client.");
            }
            ApplyField(item.Client, item.FieldCode, item.ProposedValue);
            item.AppliedAtUtc = DateTime.UtcNow;
            item.AppliedBy = user;
            item.Client.UpdatedAtUtc = DateTime.UtcNow;
        }

        item.Status = verify ? ClientVerificationStatuses.Verified : ClientVerificationStatuses.Rejected;
        item.DecidedAtUtc = DateTime.UtcNow;
        item.DecidedBy = user;
        item.DecisionReason = reason.Trim();
        db.ComplianceAuditEvents.Add(CreateAudit(
            item.Id,
            verify ? "VerificationAccepted" : "VerificationRejected",
            user,
            reason,
            oldValue,
            JsonSerializer.Serialize(new
            {
                item.Status,
                CurrentValue = ReadField(item.Client, item.FieldCode),
                item.AppliedAtUtc
            }),
            nameof(ClientVerificationItem)));
        await db.SaveChangesAsync();
    }

    public async Task<int> CountBlockingPendingAsync(int clientId) =>
        await db.ClientVerificationItems.CountAsync(item =>
            item.ClientId == clientId &&
            item.Status == ClientVerificationStatuses.Pending &&
            item.IsBlocking);

    private static string? ReadField(Client client, string fieldCode) => fieldCode switch
    {
        ClientVerificationFields.KanaanId => client.KanaanId,
        ClientVerificationFields.Title => client.Title,
        ClientVerificationFields.Initials => client.Initials,
        ClientVerificationFields.FullName => client.FullName,
        ClientVerificationFields.SurnameOrEntityName => client.SurnameOrEntityName,
        ClientVerificationFields.DisplayName => client.DisplayName,
        ClientVerificationFields.Language => client.Language,
        ClientVerificationFields.ClientFolder => client.ClientFolder,
        ClientVerificationFields.ClientCategory => client.ClientCategory,
        _ => throw new ValidationException("Unsupported client field.")
    };

    private static void ApplyField(Client client, string fieldCode, string? value)
    {
        ValidateFieldValue(fieldCode, value);
        switch (fieldCode)
        {
            case ClientVerificationFields.KanaanId:
                client.KanaanId = value;
                break;
            case ClientVerificationFields.Title:
                client.Title = value;
                break;
            case ClientVerificationFields.Initials:
                client.Initials = value;
                break;
            case ClientVerificationFields.FullName:
                client.FullName = value;
                break;
            case ClientVerificationFields.SurnameOrEntityName:
                client.SurnameOrEntityName = value ?? "";
                break;
            case ClientVerificationFields.DisplayName:
                client.DisplayName = value ?? "";
                break;
            case ClientVerificationFields.Language:
                client.Language = value;
                break;
            case ClientVerificationFields.ClientFolder:
                client.ClientFolder = value;
                break;
            case ClientVerificationFields.ClientCategory:
                if (value is not (ClientCategories.NaturalPerson or ClientCategories.LegalPerson or ClientCategories.Trust or ClientCategories.Other))
                {
                    throw new ValidationException("The proposed client category is invalid.");
                }
                client.ClientCategory = value;
                client.ClientCategorySource = ClientCategorySources.Manual;
                break;
            default:
                throw new ValidationException("Unsupported client field.");
        }
    }

    private static void ValidateFieldValue(string fieldCode, string? value)
    {
        var maxLength = fieldCode switch
        {
            ClientVerificationFields.KanaanId or ClientVerificationFields.Title => 30,
            ClientVerificationFields.Initials or ClientVerificationFields.Language => 50,
            ClientVerificationFields.FullName or ClientVerificationFields.SurnameOrEntityName => 200,
            ClientVerificationFields.DisplayName => 220,
            ClientVerificationFields.ClientFolder => 512,
            ClientVerificationFields.ClientCategory => 96,
            _ => throw new ValidationException("Unsupported client field.")
        };
        if (value?.Length > maxLength)
        {
            throw new ValidationException(
                $"The proposed {ClientVerificationFields.Labels[fieldCode]} exceeds {maxLength} characters.");
        }
        if (fieldCode is ClientVerificationFields.DisplayName or ClientVerificationFields.SurnameOrEntityName &&
            string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{ClientVerificationFields.Labels[fieldCode]} cannot be blank.");
        }
    }

    private static string RequireUser(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("An authenticated user is required.")
            : value.Trim();

    private static void RequireReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException("A reason is required.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ComplianceAuditEvent CreateAudit(
        int entityId,
        string action,
        string user,
        string reason,
        string? oldValue,
        string? newValue,
        string entityType = nameof(Client)) =>
        new()
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserName = user,
            Reason = reason.Trim(),
            OldValueJson = oldValue,
            NewValueJson = newValue
        };
}
