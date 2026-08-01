using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed partial class ClientEvidenceReadinessService(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };
    private static readonly string[] SupportedExtensions = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".txt", ".msg", ".eml"];

    public async Task<ClientEvidenceDashboardModel> LoadDashboardAsync()
    {
        await EnsureDefaultRequirementsAsync();

        var clients = await db.Clients
            .AsNoTracking()
            .OrderBy(client => client.DisplayName)
            .Select(client => new ClientEvidenceClientSummaryModel
            {
                ClientId = client.Id,
                DisplayName = client.DisplayName,
                SurnameOrEntityName = client.SurnameOrEntityName,
                KanaanId = client.KanaanId,
                ClientCategory = client.ClientCategory,
                ClientFolder = client.ClientFolder
            })
            .ToListAsync();

        var requirements = await LoadActiveRequirementsAsync();
        var items = await db.ClientEvidenceItems.AsNoTracking().ToListAsync();
        var exceptions = await db.ClientEvidenceExceptions.AsNoTracking().Where(item => item.IsActive).ToListAsync();
        var entityProfiles = await db.ClientEntityProfiles.AsNoTracking().ToListAsync();
        var relatedParties = await db.ClientRelatedParties
            .AsNoTracking()
            .AsSplitQuery()
            .Include(party => party.Roles)
            .Include(party => party.EvidenceLinks).ThenInclude(link => link.EvidenceItem)
            .ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var client in clients)
        {
            var readiness = CalculateReadiness(client.ClientId, client.ClientCategory, requirements, items, exceptions, today);
            var ownershipBlockers = EntityOwnershipRules.CalculateBlockers(
                client.ClientCategory,
                entityProfiles.FirstOrDefault(profile => profile.ClientId == client.ClientId),
                relatedParties.Where(party => party.ClientId == client.ClientId),
                items.Where(item => item.ClientId == client.ClientId),
                today);
            client.RequiredCount = readiness.RequiredCount;
            client.CompleteCount = readiness.CompleteCount;
            client.OwnershipBlockedCount = ownershipBlockers.Count;
            client.BlockedCount = readiness.BlockedCount + ownershipBlockers.Count;
            client.ExceptionCount = readiness.ExceptionCount;
            client.LinkedEvidenceCount = items.Count(item => item.ClientId == client.ClientId && ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus));
            client.VerifiedEvidenceCount = items.Count(item => item.ClientId == client.ClientId && ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus) && item.VerifiedDate is not null);
            client.IsReadyForRiskAssessment = readiness.IsReadyForRiskAssessment && ownershipBlockers.Count == 0;
        }

        var latestRun = await db.ClientEvidenceScanRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAtUtc)
            .Select(run => new ClientEvidenceScanRunModel
            {
                Id = run.Id,
                RootPath = run.RootPath,
                StartedAtUtc = run.StartedAtUtc,
                FinishedAtUtc = run.FinishedAtUtc,
                Status = run.Status,
                TotalFiles = run.TotalFiles,
                LinkedFiles = run.LinkedFiles,
                UnmatchedFiles = run.UnmatchedFiles,
                AmbiguousFiles = run.AmbiguousFiles,
                SkippedFiles = run.SkippedFiles,
                ErrorMessage = run.ErrorMessage
            })
            .FirstOrDefaultAsync();

        var activeRoot = await db.ClientEvidenceScanRoots.AsNoTracking().Where(root => root.IsActive).OrderByDescending(root => root.Id).FirstOrDefaultAsync();
        var unmatchedFiles = await db.ClientEvidenceScanFiles
            .AsNoTracking()
            .Include(file => file.Client)
            .Where(file => file.MatchStatus == ClientEvidenceScanFileStatuses.Unmatched ||
                file.MatchStatus == ClientEvidenceScanFileStatuses.Ambiguous)
            .OrderByDescending(file => file.Id)
            .Take(100)
            .Select(file => ClientEvidenceScanFileModel.FromFile(file))
            .ToListAsync();
        var aliases = await db.ClientEvidenceOwnershipAliases
            .AsNoTracking()
            .Where(alias => alias.IsActive)
            .OrderBy(alias => alias.Alias)
            .ToListAsync();
        var sharedFolders = clients
            .Where(client => !string.IsNullOrWhiteSpace(client.ClientFolder))
            .GroupBy(client => NormalizeFolderKey(client.ClientFolder!), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new ClientEvidenceSharedFolderModel
            {
                FolderPath = group.First().ClientFolder!,
                Clients = group.Select(client => new ClientEvidenceFolderClientModel
                {
                    ClientId = client.ClientId,
                    DisplayName = client.DisplayName,
                    IsJoint = IsJointClientSummary(client),
                    Aliases = aliases
                        .Where(alias => alias.ClientId == client.ClientId && SameFolder(alias.FolderPath, client.ClientFolder))
                        .Select(alias => new ClientEvidenceOwnershipAliasModel
                        {
                            Id = alias.Id,
                            Alias = alias.Alias
                        })
                        .ToList()
                }).ToList()
            })
            .ToList();
        var reviewItems = await db.ClientEvidenceItems
            .AsNoTracking()
            .Include(item => item.Client)
            .Where(item => item.OwnershipStatus == ClientEvidenceOwnershipStatuses.NeedsReview)
            .OrderByDescending(item => item.Id)
            .Take(200)
            .ToListAsync();
        var ownershipReviews = reviewItems
            .GroupBy(item => $"{item.SourcePath}\n{item.FileSha256}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var item = group.First();
                var folder = sharedFolders.FirstOrDefault(shared => SameFolder(shared.FolderPath, item.Client.ClientFolder));
                return new ClientEvidenceOwnershipReviewModel
                {
                    SourceItemId = item.Id,
                    RelativePath = item.RelativePath,
                    EvidenceType = item.EvidenceType,
                    OwnershipReason = item.OwnershipReason,
                    CandidateClients = folder?.Clients ?? []
                };
            })
            .ToList();

        return new ClientEvidenceDashboardModel
        {
            ScanRootPath = activeRoot?.RootPath,
            LatestScanRun = latestRun,
            Clients = clients,
            UnmatchedFiles = unmatchedFiles,
            SharedFolders = sharedFolders,
            OwnershipReviews = ownershipReviews,
            RequirementCount = requirements.Count,
            ReadyClientCount = clients.Count(client => client.IsReadyForRiskAssessment),
            BlockedClientCount = clients.Count(client => client.BlockedCount > 0)
        };
    }

    public async Task<ClientEvidenceReadinessModel> LoadClientReadinessAsync(int clientId)
    {
        await EnsureDefaultRequirementsAsync();

        var client = await db.Clients
            .AsNoTracking()
            .AsSplitQuery()
            .Include(client => client.Relationships)
            .Include(client => client.EntityProfile)
            .Include(client => client.RelatedParties).ThenInclude(party => party.Roles)
            .Include(client => client.RelatedParties).ThenInclude(party => party.EvidenceLinks).ThenInclude(link => link.EvidenceItem)
            .SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");

        var requirements = ActiveForCategory(await LoadActiveRequirementsAsync(), client.ClientCategory);
        var items = await db.ClientEvidenceItems
            .AsNoTracking()
            .Include(item => item.Requirement)
            .Where(item => item.ClientId == clientId)
            .OrderBy(item => item.EvidenceType)
            .ThenBy(item => item.Title)
            .ToListAsync();
        var exceptions = await db.ClientEvidenceExceptions
            .AsNoTracking()
            .Include(exception => exception.Requirement)
            .Where(exception => exception.ClientId == clientId && exception.IsActive)
            .ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var ownershipBlockers = EntityOwnershipRules.CalculateBlockers(
            client.ClientCategory,
            client.EntityProfile,
            client.RelatedParties,
            items,
            today);

        var requirementRows = requirements
            .Select(requirement =>
            {
                var matchedItems = items
                    .Where(item => ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus) &&
                        (item.ClientEvidenceRequirementId == requirement.Id || item.EvidenceType == requirement.EvidenceType))
                    .OrderByDescending(item => item.SelectionStatus == ClientEvidenceSelectionStatuses.Current)
                    .ThenByDescending(item => item.SelectionConfidence ?? 0)
                    .ThenByDescending(item => item.FileLastWriteTimeUtc ?? item.CreatedAtUtc)
                    .ThenBy(item => item.Title)
                    .ToList();
                var activeException = exceptions.FirstOrDefault(exception => exception.ClientEvidenceRequirementId == requirement.Id && !IsExpired(exception.ReviewDate, today));
                var isComplete = matchedItems.Any(item => IsEvidenceComplete(requirement, item, today));
                return new ClientEvidenceRequirementStatusModel
                {
                    RequirementId = requirement.Id,
                    RequirementGroup = requirement.RequirementGroup,
                    EvidenceType = requirement.EvidenceType,
                    Title = requirement.Title,
                    IsBlocking = requirement.IsBlocking,
                    RequiresVerification = requirement.RequiresVerification,
                    RequiresExpiryDate = requirement.RequiresExpiryDate,
                IsComplete = isComplete,
                IsExceptioned = activeException is not null,
                IsBlocked = requirement.IsBlocking && !isComplete && activeException is null,
                ExceptionReason = activeException?.Reason,
                LinkedItemCount = matchedItems.Count,
                VerifiedItemCount = matchedItems.Count(item => item.VerifiedDate is not null),
                CanRecordReview = IsReviewOnlyEvidenceType(requirement.EvidenceType),
                Items = matchedItems.Select(ClientEvidenceItemModel.FromItem).ToList()
            };
            })
            .OrderBy(row => row.RequirementGroup)
            .ThenBy(row => row.Title)
            .ToList();

        return new ClientEvidenceReadinessModel
        {
            ClientId = client.Id,
            DisplayName = client.DisplayName,
            KanaanId = client.KanaanId,
            ClientCategory = client.ClientCategory,
            ClientFolder = client.ClientFolder,
            ScreeningSubjects = BuildScreeningSubjects(client),
            OwnershipBlockers = ownershipBlockers,
            Requirements = requirementRows,
            EvidenceItems = items.Select(ClientEvidenceItemModel.FromItem).ToList(),
            RequiredCount = requirementRows.Count,
            CompleteCount = requirementRows.Count(row => row.IsComplete),
            ExceptionCount = requirementRows.Count(row => row.IsExceptioned),
            BlockedCount = requirementRows.Count(row => row.IsBlocked) + ownershipBlockers.Count,
            LinkedEvidenceCount = items.Count(item => ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus)),
            VerifiedEvidenceCount = items.Count(item => ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus) && item.VerifiedDate is not null),
            IsReadyForRiskAssessment = requirementRows.All(row => !row.IsBlocked) && ownershipBlockers.Count == 0
        };
    }

    public async Task<IReadOnlyDictionary<int, ClientEvidencePortfolioReadiness>> LoadPortfolioReadinessAsync(
        IReadOnlyCollection<int> clientIds,
        CancellationToken cancellationToken = default)
    {
        if (clientIds.Count == 0)
        {
            return new Dictionary<int, ClientEvidencePortfolioReadiness>();
        }

        await EnsureDefaultRequirementsAsync();
        var selectedIds = clientIds.Distinct().ToList();
        var clients = await db.Clients.AsNoTracking()
            .Where(client => selectedIds.Contains(client.Id))
            .Select(client => new { client.Id, client.ClientCategory })
            .ToListAsync(cancellationToken);
        var requirements = await LoadActiveRequirementsAsync(cancellationToken);
        var items = await db.ClientEvidenceItems.AsNoTracking()
            .Where(item => selectedIds.Contains(item.ClientId))
            .ToListAsync(cancellationToken);
        var exceptions = await db.ClientEvidenceExceptions.AsNoTracking()
            .Where(item => selectedIds.Contains(item.ClientId) && item.IsActive)
            .ToListAsync(cancellationToken);
        var profiles = await db.ClientEntityProfiles.AsNoTracking()
            .Where(item => selectedIds.Contains(item.ClientId))
            .ToListAsync(cancellationToken);
        var relatedParties = await db.ClientRelatedParties.AsNoTracking()
            .AsSplitQuery()
            .Where(item => selectedIds.Contains(item.ClientId))
            .Include(item => item.Roles)
            .Include(item => item.EvidenceLinks).ThenInclude(link => link.EvidenceItem)
            .ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);

        return clients.ToDictionary(
            client => client.Id,
            client =>
            {
                var readiness = CalculateReadiness(
                    client.Id,
                    client.ClientCategory,
                    requirements,
                    items,
                    exceptions,
                    today);
                var ownershipBlockers = EntityOwnershipRules.CalculateBlockers(
                    client.ClientCategory,
                    profiles.FirstOrDefault(profile => profile.ClientId == client.Id),
                    relatedParties.Where(party => party.ClientId == client.Id),
                    items.Where(item => item.ClientId == client.Id),
                    today);
                var blockedCount = readiness.BlockedCount + ownershipBlockers.Count;
                return new ClientEvidencePortfolioReadiness(
                    readiness.RequiredCount,
                    readiness.CompleteCount,
                    readiness.ExceptionCount,
                    blockedCount,
                    blockedCount == 0);
            });
    }

    public async Task SaveScanRootAsync(string rootPath, string? userName, string reason)
    {
        RequireReason(reason);
        var normalized = Normalize(rootPath) ?? throw new ValidationException("Scan root path is required.");
        if (!Directory.Exists(normalized))
        {
            throw new ValidationException("Scan root path does not exist on the server.");
        }

        foreach (var existing in await db.ClientEvidenceScanRoots.Where(root => root.IsActive).ToListAsync())
        {
            existing.IsActive = false;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            existing.UpdatedBy = userName;
        }

        var root = new ClientEvidenceScanRoot
        {
            RootPath = normalized,
            IsActive = true,
            UpdatedBy = userName
        };
        db.ClientEvidenceScanRoots.Add(root);
        await AddAuditAsync("ClientEvidenceScanRoot", root.Id, "SetRoot", root, userName, reason);
        await db.SaveChangesAsync();
    }

    public async Task<ClientEvidenceFolderBrowserModel> BrowseServerFoldersAsync(string? requestedPath)
    {
        var roots = DriveInfo.GetDrives()
            .Select(drive => drive.RootDirectory.FullName)
            .OrderBy(path => path)
            .ToList();

        var requested = Normalize(requestedPath);
        var fallbackPath = roots.FirstOrDefault()
            ?? Path.GetPathRoot(Environment.CurrentDirectory)
            ?? Environment.CurrentDirectory;
        var currentPath = requested ?? fallbackPath;
        var result = await EnumerateServerFoldersAsync(currentPath);
        string? errorMessage = result.ErrorMessage;

        if (result.ErrorMessage is not null &&
            !string.Equals(currentPath, fallbackPath, StringComparison.OrdinalIgnoreCase))
        {
            var unavailablePath = currentPath;
            currentPath = fallbackPath;
            result = await EnumerateServerFoldersAsync(currentPath);
            errorMessage = result.ErrorMessage is null
                ? $"The requested folder '{unavailablePath}' is unavailable. Showing '{currentPath}' instead."
                : $"The requested folder '{unavailablePath}' is unavailable. {result.ErrorMessage}";
        }

        var model = new ClientEvidenceFolderBrowserModel
        {
            CurrentPath = currentPath,
            ParentPath = Directory.GetParent(currentPath)?.FullName,
            Roots = roots,
            Folders = result.Folders,
            ErrorMessage = errorMessage
        };

        return model;
    }

    private static async Task<ServerFolderEnumerationResult> EnumerateServerFoldersAsync(string path)
    {
        var enumeration = Task.Run(() =>
        {
            try
            {
                var folders = Directory.EnumerateDirectories(path)
                    .Select(folderPath => new DirectoryInfo(folderPath))
                    .OrderBy(directory => directory.Name)
                    .Select(directory => new ClientEvidenceFolderModel
                    {
                        Name = directory.Name,
                        FullPath = directory.FullName
                    })
                    .ToList();
                return new ServerFolderEnumerationResult(folders, null);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                return new ServerFolderEnumerationResult([], exception.Message);
            }
        });

        var completed = await Task.WhenAny(enumeration, Task.Delay(TimeSpan.FromSeconds(2)));
        return completed == enumeration
            ? await enumeration
            : new ServerFolderEnumerationResult([], $"The folder '{path}' did not respond within 2 seconds.");
    }

    private sealed record ServerFolderEnumerationResult(
        List<ClientEvidenceFolderModel> Folders,
        string? ErrorMessage);

    public async Task SaveClientEvidenceFolderAsync(int clientId, string? selectedClientFolder, string? userName, string reason)
    {
        RequireReason(reason);
        var rootPath = Normalize(selectedClientFolder) ?? throw new ValidationException("Client folder path is required.");
        if (!Directory.Exists(rootPath))
        {
            throw new ValidationException("Client folder path does not exist on the server.");
        }

        var client = await db.Clients.SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        client.ClientFolder = rootPath;
        client.UpdatedAtUtc = DateTime.UtcNow;
        await AddAuditAsync("Client", client.Id, "SetEvidenceClientFolder", new
        {
            client.Id,
            client.DisplayName,
            client.ClientFolder
        }, userName, reason);
        await db.SaveChangesAsync();
    }

    public async Task AddOwnershipAliasAsync(int clientId, string aliasValue, string? userName, string reason)
    {
        RequireReason(reason);
        var alias = Normalize(aliasValue) ?? throw new ValidationException("Ownership alias is required.");
        var client = await db.Clients.SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        var folderPath = Normalize(client.ClientFolder) ?? throw new ValidationException("Select a client folder before adding aliases.");
        if (await db.ClientEvidenceOwnershipAliases.AnyAsync(item =>
            item.ClientId == clientId &&
            item.FolderPath == folderPath &&
            item.Alias == alias &&
            item.IsActive))
        {
            return;
        }

        var ownershipAlias = new ClientEvidenceOwnershipAlias
        {
            ClientId = clientId,
            FolderPath = folderPath,
            Alias = alias,
            IsJoint = IsJointClient(client),
            CreatedBy = userName
        };
        db.ClientEvidenceOwnershipAliases.Add(ownershipAlias);
        await db.SaveChangesAsync();
        await AddAuditAsync("ClientEvidenceOwnershipAlias", ownershipAlias.Id, "Add", ownershipAlias, userName, reason);
        await db.SaveChangesAsync();
    }

    public async Task DisableOwnershipAliasAsync(int aliasId, string? userName, string reason)
    {
        RequireReason(reason);
        var alias = await db.ClientEvidenceOwnershipAliases.SingleOrDefaultAsync(item => item.Id == aliasId)
            ?? throw new InvalidOperationException("Ownership alias not found.");
        alias.IsActive = false;
        await AddAuditAsync("ClientEvidenceOwnershipAlias", alias.Id, "Disable", alias, userName, reason);
        await db.SaveChangesAsync();
    }

    public async Task ReconcileSharedFolderAsync(string folderPath, string? userName, string reason)
    {
        RequireReason(reason);
        var normalizedFolder = Normalize(folderPath) ?? throw new ValidationException("Shared folder path is required.");
        await EnsureDefaultOwnershipAliasesAsync(normalizedFolder, userName);
        var clients = await db.Clients
            .Where(client => client.ClientFolder != null)
            .ToListAsync();
        var sharedClients = clients.Where(client => SameFolder(client.ClientFolder, normalizedFolder)).ToList();
        if (sharedClients.Count < 2)
        {
            throw new ValidationException("The selected folder is not shared by multiple client records.");
        }

        var clientIds = sharedClients.Select(client => client.Id).ToHashSet();
        var aliases = await db.ClientEvidenceOwnershipAliases
            .Where(alias => alias.IsActive && clientIds.Contains(alias.ClientId))
            .ToListAsync();
        var items = await db.ClientEvidenceItems
            .Where(item => clientIds.Contains(item.ClientId) && item.SourcePath != null)
            .ToListAsync();

        foreach (var group in items.GroupBy(item => $"{item.SourcePath}\n{item.FileSha256}", StringComparer.OrdinalIgnoreCase))
        {
            var representative = group.First();
            var match = MatchSharedFolderOwner(representative.RelativePath ?? representative.SourcePath ?? "", sharedClients, aliases);
            foreach (var item in group)
            {
                var selected = match.Client is not null && item.ClientId == match.Client.Id;
                item.OwnershipStatus = selected
                    ? ClientEvidenceOwnershipStatuses.AutoAssigned
                    : match.Client is null
                        ? ClientEvidenceOwnershipStatuses.NeedsReview
                        : ClientEvidenceOwnershipStatuses.Excluded;
                item.OwnershipConfidence = selected ? 100 : null;
                item.OwnershipReason = selected ? match.Reason : match.Client is null ? match.Reason : $"Assigned to client #{match.Client.Id}.";
                item.OwnershipReviewedAtUtc = DateTime.UtcNow;
                item.OwnershipReviewedBy = userName;
                item.UpdatedAtUtc = DateTime.UtcNow;
                item.UpdatedBy = userName;
            }
        }

        await AddAuditAsync("ClientEvidenceFolder", 0, "ReconcileOwnership", new
        {
            FolderPath = normalizedFolder,
            ClientIds = clientIds.OrderBy(id => id),
            ItemCount = items.Count
        }, userName, reason);
        await db.SaveChangesAsync();
        foreach (var clientId in clientIds)
        {
            await RefreshEvidenceSelectionsAsync(clientId, userName, reason, CancellationToken.None);
        }
    }

    public async Task AssignEvidenceOwnershipAsync(int sourceItemId, IReadOnlyCollection<int> selectedClientIds, string? userName, string reason)
    {
        RequireReason(reason);
        if (selectedClientIds.Count == 0)
        {
            throw new ValidationException("Select at least one client for this evidence.");
        }

        var source = await db.ClientEvidenceItems.SingleOrDefaultAsync(item => item.Id == sourceItemId)
            ?? throw new InvalidOperationException("Evidence item not found.");
        var sourceClient = await db.Clients.AsNoTracking().SingleAsync(client => client.Id == source.ClientId);
        var folderPath = Normalize(sourceClient.ClientFolder) ?? throw new ValidationException("Evidence client folder is not configured.");
        var folderClients = (await db.Clients.Where(client => client.ClientFolder != null).ToListAsync())
            .Where(client => SameFolder(client.ClientFolder, folderPath))
            .ToList();
        var allowedIds = folderClients.Select(client => client.Id).ToHashSet();
        if (selectedClientIds.Any(id => !allowedIds.Contains(id)))
        {
            throw new ValidationException("Evidence can only be assigned to clients sharing this folder.");
        }

        var selected = selectedClientIds.Distinct().ToHashSet();
        var groupItems = await db.ClientEvidenceItems
            .Where(item => allowedIds.Contains(item.ClientId) &&
                item.SourcePath == source.SourcePath &&
                item.FileSha256 == source.FileSha256)
            .ToListAsync();
        foreach (var clientId in selected)
        {
            if (groupItems.Any(item => item.ClientId == clientId))
            {
                continue;
            }

            var copy = CopyEvidenceForClient(source, clientId, userName);
            db.ClientEvidenceItems.Add(copy);
            groupItems.Add(copy);
        }

        foreach (var item in groupItems)
        {
            item.OwnershipStatus = selected.Contains(item.ClientId)
                ? ClientEvidenceOwnershipStatuses.Confirmed
                : ClientEvidenceOwnershipStatuses.Excluded;
            item.OwnershipConfidence = selected.Contains(item.ClientId) ? 100 : null;
            item.OwnershipReason = selected.Contains(item.ClientId)
                ? $"Confirmed for client #{item.ClientId} by reviewer."
                : "Excluded by shared-folder ownership review.";
            item.OwnershipReviewedAtUtc = DateTime.UtcNow;
            item.OwnershipReviewedBy = userName;
            item.UpdatedAtUtc = DateTime.UtcNow;
            item.UpdatedBy = userName;
        }

        await AddAuditAsync("ClientEvidenceItem", sourceItemId, "AssignOwnership", new
        {
            source.SourcePath,
            source.FileSha256,
            SelectedClientIds = selected.OrderBy(id => id)
        }, userName, reason);
        await db.SaveChangesAsync();
        foreach (var clientId in allowedIds)
        {
            await RefreshEvidenceSelectionsAsync(clientId, userName, reason, CancellationToken.None);
        }
    }

    public async Task<int> RunScanAsync(string? requestedRootPath, string? userName, string reason)
    {
        var runId = await StartScanRunAsync(requestedRootPath, userName, reason);
        await ExecuteScanRunAsync(runId, userName, reason, CancellationToken.None);
        return runId;
    }

    public async Task<int> RunClientFolderScanAsync(int clientId, string? selectedClientFolder, string? userName, string reason)
    {
        var runId = await StartClientScanRunAsync(clientId, selectedClientFolder, userName, reason);
        await ExecuteClientScanRunAsync(runId, clientId, userName, reason, CancellationToken.None);
        return runId;
    }

    public async Task<int> StartScanRunAsync(string? requestedRootPath, string? userName, string reason)
    {
        RequireReason(reason);
        if (await db.ClientEvidenceScanRuns.AnyAsync(run =>
            run.Status == ClientEvidenceScanStatuses.Running ||
            run.Status == ClientEvidenceScanStatuses.Cancelling))
        {
            throw new InvalidOperationException("An evidence scan is already running.");
        }

        var rootPath = Normalize(requestedRootPath);
        if (rootPath is null)
        {
            rootPath = await db.ClientEvidenceScanRoots
                .AsNoTracking()
                .Where(root => root.IsActive)
                .OrderByDescending(root => root.Id)
                .Select(root => root.RootPath)
                .FirstOrDefaultAsync();
        }

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            throw new ValidationException("A valid server scan root path is required.");
        }

        var run = new ClientEvidenceScanRun
        {
            RootPath = rootPath,
            StartedBy = userName,
            Status = ClientEvidenceScanStatuses.Running
        };
        db.ClientEvidenceScanRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    public async Task<int> StartClientScanRunAsync(int clientId, string? selectedClientFolder, string? userName, string reason)
    {
        RequireReason(reason);
        if (await db.ClientEvidenceScanRuns.AnyAsync(run =>
            run.Status == ClientEvidenceScanStatuses.Running ||
            run.Status == ClientEvidenceScanStatuses.Cancelling))
        {
            throw new InvalidOperationException("An evidence scan is already running.");
        }

        await SaveClientEvidenceFolderAsync(clientId, selectedClientFolder, userName, reason);
        var rootPath = Normalize(selectedClientFolder)!;
        await EnsureDefaultOwnershipAliasesAsync(rootPath, userName);

        var run = new ClientEvidenceScanRun
        {
            RootPath = rootPath,
            StartedBy = userName,
            Status = ClientEvidenceScanStatuses.Running
        };
        db.ClientEvidenceScanRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    public async Task ExecuteScanRunAsync(int runId, string? userName, string reason, CancellationToken cancellationToken)
    {
        await ExecuteScanRunAsync(runId, forcedClientId: null, userName, reason, cancellationToken);
    }

    public async Task ExecuteClientScanRunAsync(int runId, int clientId, string? userName, string reason, CancellationToken cancellationToken)
    {
        await ExecuteScanRunAsync(runId, clientId, userName, reason, cancellationToken);
    }

    private async Task ExecuteScanRunAsync(int runId, int? forcedClientId, string? userName, string reason, CancellationToken cancellationToken)
    {
        var run = await db.ClientEvidenceScanRuns.SingleOrDefaultAsync(run => run.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException("Evidence scan run not found.");
        try
        {
            var clients = await db.Clients.ToListAsync(cancellationToken);
            var requirements = await LoadActiveRequirementsAsync(cancellationToken);
            var ownershipAliases = await db.ClientEvidenceOwnershipAliases
                .AsNoTracking()
                .Where(alias => alias.IsActive)
                .ToListAsync(cancellationToken);
            var affectedClientIds = new HashSet<int>();
            var forcedClient = forcedClientId.HasValue
                ? clients.SingleOrDefault(client => client.Id == forcedClientId.Value) ?? throw new InvalidOperationException("Client not found.")
                : null;
            var sharedFolderClients = forcedClient is null
                ? []
                : clients.Where(client => SameFolder(client.ClientFolder, run.RootPath)).ToList();
            var isSharedFolderScan = sharedFolderClients.Count > 1;
            foreach (var path in Directory.EnumerateFiles(run.RootPath, "*.*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileInfo = new FileInfo(path);
                var relativePath = Path.GetRelativePath(run.RootPath, path);
                var extension = fileInfo.Extension.ToLowerInvariant();
                run.TotalFiles++;

                var scanFile = new ClientEvidenceScanFile
                {
                    ScanRun = run,
                    FullPath = path,
                    RelativePath = relativePath,
                    FileName = fileInfo.Name,
                    FileSizeBytes = fileInfo.Length,
                    FileLastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                    FileSha256 = await ComputeSha256Async(path, cancellationToken),
                    SuggestedEvidenceType = SuggestEvidenceType(relativePath)
                };

                if (!SupportedExtensions.Contains(extension))
                {
                    scanFile.MatchStatus = ClientEvidenceScanFileStatuses.Skipped;
                    scanFile.MatchReason = "Unsupported file extension.";
                    run.SkippedFiles++;
                    db.ClientEvidenceScanFiles.Add(scanFile);
                    await SaveScanProgressIfNeededAsync(run, cancellationToken);
                    continue;
                }

                var match = isSharedFolderScan
                    ? MatchSharedFolderOwner(relativePath, sharedFolderClients, ownershipAliases)
                    : forcedClient is not null
                    ? new ClientEvidenceMatchResult(forcedClient, 1, "Single-client folder scan.")
                    : MatchClient(relativePath, clients);
                scanFile.CandidateCount = match.CandidateCount;
                scanFile.MatchReason = match.Reason;
                if (match.Client is null)
                {
                    scanFile.MatchStatus = isSharedFolderScan
                        ? ClientEvidenceScanFileStatuses.OwnershipReview
                        : match.CandidateCount > 1 ? ClientEvidenceScanFileStatuses.Ambiguous : ClientEvidenceScanFileStatuses.Unmatched;
                    if (scanFile.MatchStatus == ClientEvidenceScanFileStatuses.Ambiguous)
                    {
                        run.AmbiguousFiles++;
                    }
                    else if (scanFile.MatchStatus == ClientEvidenceScanFileStatuses.OwnershipReview)
                    {
                        run.AmbiguousFiles++;
                    }
                    else
                    {
                        run.UnmatchedFiles++;
                    }

                    if (scanFile.MatchStatus == ClientEvidenceScanFileStatuses.OwnershipReview && forcedClient is not null)
                    {
                        scanFile.ClientId = forcedClient.Id;
                        var existingReviewItem = await db.ClientEvidenceItems.FirstOrDefaultAsync(item =>
                            item.ClientId == forcedClient.Id &&
                            item.FileSha256 == scanFile.FileSha256 &&
                            item.RelativePath == scanFile.RelativePath,
                            cancellationToken);
                        if (existingReviewItem is null)
                        {
                            var reviewEvidenceType = scanFile.SuggestedEvidenceType ?? "General";
                            var reviewRequirement = requirements.FirstOrDefault(requirement => requirement.EvidenceType == reviewEvidenceType);
                            db.ClientEvidenceItems.Add(new ClientEvidenceItem
                            {
                                ClientId = forcedClient.Id,
                                ClientEvidenceRequirementId = reviewRequirement?.Id,
                                EvidenceType = reviewEvidenceType,
                                Title = Path.GetFileNameWithoutExtension(fileInfo.Name),
                                SourcePath = path,
                                RelativePath = relativePath,
                                FileName = fileInfo.Name,
                                FileSha256 = scanFile.FileSha256,
                                FileSizeBytes = fileInfo.Length,
                                FileLastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                                Status = ClientEvidenceStatuses.Linked,
                                OwnershipStatus = ClientEvidenceOwnershipStatuses.NeedsReview,
                                OwnershipReason = match.Reason,
                                ScanFile = scanFile,
                                UpdatedBy = userName
                            });
                        }
                    }

                    db.ClientEvidenceScanFiles.Add(scanFile);
                    await SaveScanProgressIfNeededAsync(run, cancellationToken);
                    continue;
                }

                scanFile.ClientId = match.Client.Id;
                scanFile.MatchStatus = ClientEvidenceScanFileStatuses.Linked;
                affectedClientIds.Add(match.Client.Id);
                db.ClientEvidenceScanFiles.Add(scanFile);
                await ResolvePriorScanFilesForPathAsync(scanFile, match.Client.Id, cancellationToken);

                var evidenceType = scanFile.SuggestedEvidenceType ?? "General";
                await ApplyEvidenceCategoryInferenceAsync(match.Client, relativePath, evidenceType, userName, reason);
                var requirement = requirements.FirstOrDefault(requirement => requirement.EvidenceType == evidenceType);
                var existingItem = await db.ClientEvidenceItems.FirstOrDefaultAsync(item =>
                    item.ClientId == match.Client.Id &&
                    item.FileSha256 == scanFile.FileSha256 &&
                    item.RelativePath == scanFile.RelativePath,
                    cancellationToken);

                if (existingItem is null)
                {
                    db.ClientEvidenceItems.Add(new ClientEvidenceItem
                    {
                        ClientId = match.Client.Id,
                        ClientEvidenceRequirementId = requirement?.Id,
                        EvidenceType = evidenceType,
                        Title = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        SourcePath = path,
                        RelativePath = relativePath,
                        FileName = fileInfo.Name,
                        FileSha256 = scanFile.FileSha256,
                        FileSizeBytes = fileInfo.Length,
                        FileLastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                        Status = ClientEvidenceStatuses.Linked,
                        OwnershipStatus = isSharedFolderScan
                            ? ClientEvidenceOwnershipStatuses.AutoAssigned
                            : ClientEvidenceOwnershipStatuses.Confirmed,
                        OwnershipConfidence = isSharedFolderScan ? 100 : null,
                        OwnershipReason = match.Reason,
                        ScanFile = scanFile,
                        UpdatedBy = userName
                    });
                }
                else if (isSharedFolderScan &&
                    existingItem.OwnershipStatus is ClientEvidenceOwnershipStatuses.NeedsReview or ClientEvidenceOwnershipStatuses.Excluded)
                {
                    existingItem.OwnershipStatus = ClientEvidenceOwnershipStatuses.AutoAssigned;
                    existingItem.OwnershipConfidence = 100;
                    existingItem.OwnershipReason = match.Reason;
                    existingItem.OwnershipReviewedAtUtc = DateTime.UtcNow;
                    existingItem.OwnershipReviewedBy = userName;
                }

                run.LinkedFiles++;
                await SaveScanProgressIfNeededAsync(run, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            foreach (var clientId in affectedClientIds)
            {
                await RefreshEvidenceSelectionsAsync(clientId, userName, reason, cancellationToken);
            }

            run.Status = ClientEvidenceScanStatuses.Completed;
            run.FinishedAtUtc = DateTime.UtcNow;
            await AddAuditAsync("ClientEvidenceScanRun", run.Id, "RunScan", new
            {
                run.RootPath,
                ForcedClientId = forcedClientId,
                run.TotalFiles,
                run.LinkedFiles,
                run.UnmatchedFiles,
                run.AmbiguousFiles,
                run.SkippedFiles
            }, userName, reason);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Status = ClientEvidenceScanStatuses.Cancelled;
            run.FinishedAtUtc = DateTime.UtcNow;
            run.ErrorMessage = "Scan cancelled by user request.";
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            run.Status = ClientEvidenceScanStatuses.Failed;
            run.FinishedAtUtc = DateTime.UtcNow;
            run.ErrorMessage = ex.Message;
            await db.SaveChangesAsync();
            throw;
        }
    }

    public async Task RequestScanCancellationAsync(int runId, string? userName, string reason)
    {
        RequireReason(reason);
        var run = await db.ClientEvidenceScanRuns.SingleOrDefaultAsync(run => run.Id == runId)
            ?? throw new InvalidOperationException("Evidence scan run not found.");
        if (run.Status is not (ClientEvidenceScanStatuses.Running or ClientEvidenceScanStatuses.Cancelling))
        {
            return;
        }

        run.Status = ClientEvidenceScanStatuses.Cancelling;
        run.ErrorMessage = "Cancellation requested.";
        await AddAuditAsync("ClientEvidenceScanRun", run.Id, "CancelScan", new
        {
            run.RootPath,
            run.TotalFiles,
            run.LinkedFiles,
            run.UnmatchedFiles,
            run.AmbiguousFiles,
            run.SkippedFiles
        }, userName, reason);
        await db.SaveChangesAsync();
    }

    public async Task CancelUntrackedScanAsync(int runId, string? userName, string reason)
    {
        RequireReason(reason);
        var run = await db.ClientEvidenceScanRuns.SingleOrDefaultAsync(run => run.Id == runId)
            ?? throw new InvalidOperationException("Evidence scan run not found.");
        if (run.Status is not (ClientEvidenceScanStatuses.Running or ClientEvidenceScanStatuses.Cancelling))
        {
            return;
        }

        run.Status = ClientEvidenceScanStatuses.Cancelled;
        run.FinishedAtUtc = DateTime.UtcNow;
        run.ErrorMessage = "Scan cancelled. No active worker was tracking this run.";
        await AddAuditAsync("ClientEvidenceScanRun", run.Id, "CancelScan", new
        {
            run.RootPath,
            run.TotalFiles,
            run.LinkedFiles,
            run.UnmatchedFiles,
            run.AmbiguousFiles,
            run.SkippedFiles
        }, userName, reason);
        await db.SaveChangesAsync();
    }

    public async Task VerifyEvidenceAsync(int evidenceItemId, DateOnly? receivedDate, DateOnly? expiryDate, string? userName, string reason)
    {
        RequireReason(reason);
        var item = await db.ClientEvidenceItems.SingleOrDefaultAsync(item => item.Id == evidenceItemId)
            ?? throw new InvalidOperationException("Evidence item not found.");
        if (!ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus))
        {
            throw new ValidationException("Confirm evidence ownership before verification.");
        }
        await VerifyEvidenceItemAsync(item, receivedDate, expiryDate, userName, reason);
        await db.SaveChangesAsync();
    }

    public async Task<int> VerifyEvidenceBatchAsync(int clientId, IReadOnlyCollection<int> evidenceItemIds, DateOnly? receivedDate, DateOnly? expiryDate, string? userName, string reason)
    {
        RequireReason(reason);
        if (evidenceItemIds.Count == 0)
        {
            throw new ValidationException("Select at least one evidence item to verify.");
        }

        var selectedIds = evidenceItemIds.Distinct().ToList();
        var items = await db.ClientEvidenceItems
            .Where(item => item.ClientId == clientId && selectedIds.Contains(item.Id))
            .ToListAsync();
        if (items.Count != selectedIds.Count)
        {
            throw new InvalidOperationException("One or more selected evidence items could not be found for this client.");
        }
        if (items.Any(item => !ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus)))
        {
            throw new ValidationException("Confirm evidence ownership before verification.");
        }

        foreach (var item in items)
        {
            await VerifyEvidenceItemAsync(item, receivedDate, expiryDate, userName, reason);
        }

        await db.SaveChangesAsync();
        return items.Count;
    }

    public async Task ResolveScanFileAsync(int scanFileId, int clientId, string? evidenceType, string? userName, string reason)
    {
        RequireReason(reason);
        var scanFile = await db.ClientEvidenceScanFiles.SingleOrDefaultAsync(file => file.Id == scanFileId)
            ?? throw new InvalidOperationException("Scan file not found.");
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");

        var normalizedType = Normalize(evidenceType) ?? scanFile.SuggestedEvidenceType ?? "General";
        var requirement = await db.ClientEvidenceRequirements
            .AsNoTracking()
            .FirstOrDefaultAsync(requirement => requirement.EvidenceType == normalizedType && requirement.Status == ClientEvidenceRequirementStatuses.Active);
        var existingItem = await db.ClientEvidenceItems.FirstOrDefaultAsync(item =>
            item.ClientId == client.Id &&
            item.FileSha256 == scanFile.FileSha256 &&
            item.RelativePath == scanFile.RelativePath);

        scanFile.ClientId = client.Id;
        scanFile.MatchStatus = ClientEvidenceScanFileStatuses.Linked;
        scanFile.MatchReason = "Manually linked by reviewer.";
        scanFile.SuggestedEvidenceType = normalizedType;
        scanFile.CandidateCount = 1;

        if (existingItem is null)
        {
            db.ClientEvidenceItems.Add(new ClientEvidenceItem
            {
                ClientId = client.Id,
                ClientEvidenceRequirementId = requirement?.Id,
                EvidenceType = normalizedType,
                Title = Path.GetFileNameWithoutExtension(scanFile.FileName),
                SourcePath = scanFile.FullPath,
                RelativePath = scanFile.RelativePath,
                FileName = scanFile.FileName,
                FileSha256 = scanFile.FileSha256,
                FileSizeBytes = scanFile.FileSizeBytes,
                FileLastWriteTimeUtc = scanFile.FileLastWriteTimeUtc,
                Status = ClientEvidenceStatuses.Linked,
                OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
                OwnershipConfidence = 100,
                OwnershipReason = "Manually linked by reviewer.",
                OwnershipReviewedAtUtc = DateTime.UtcNow,
                OwnershipReviewedBy = userName,
                ScanFile = scanFile,
                UpdatedBy = userName
            });
        }
        else
        {
            existingItem.OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed;
            existingItem.OwnershipConfidence = 100;
            existingItem.OwnershipReason = "Manually linked by reviewer.";
            existingItem.OwnershipReviewedAtUtc = DateTime.UtcNow;
            existingItem.OwnershipReviewedBy = userName;
        }

        await AddAuditAsync("ClientEvidenceScanFile", scanFile.Id, "ResolveScanFile", new
        {
            scanFile.RelativePath,
            ClientId = client.Id,
            EvidenceType = normalizedType
        }, userName, reason);
        await db.SaveChangesAsync();
        await RefreshEvidenceSelectionsAsync(client.Id, userName, reason, CancellationToken.None);
    }

    public Task<int> RecordRequirementReviewAsync(int clientId, int requirementId, string? userName, string reason) =>
        RecordRequirementReviewAsync(clientId, requirementId, new ClientEvidenceScreeningReviewRequest
        {
            SubjectType = ClientEvidenceScreeningSubjectTypes.Client,
            SubjectName = null,
            Outcome = ClientEvidenceScreeningOutcomes.NoMatch,
            RiskSignal = ClientEvidenceRiskSignals.Low,
            ReviewDate = DateOnly.FromDateTime(DateTime.Today),
            Notes = reason
        }, userName, reason);

    public async Task<int> RecordRequirementReviewAsync(int clientId, int requirementId, ClientEvidenceScreeningReviewRequest request, string? userName, string? reason)
    {
        var auditReason = Normalize(reason) ?? "Record screening review.";
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        var requirement = await db.ClientEvidenceRequirements.AsNoTracking().SingleOrDefaultAsync(requirement => requirement.Id == requirementId)
            ?? throw new InvalidOperationException("Evidence requirement not found.");
        if (!IsReviewOnlyEvidenceType(requirement.EvidenceType))
        {
            throw new ValidationException("This requirement does not support internal review recording.");
        }

        var reviewDate = request.ReviewDate ?? DateOnly.FromDateTime(DateTime.Today);
        var subjectType = Normalize(request.SubjectType) ?? ClientEvidenceScreeningSubjectTypes.Client;
        var subjectName = Normalize(request.SubjectName) ?? client.DisplayName;
        var outcome = Normalize(request.Outcome) ?? throw new ValidationException("Screening outcome is required.");
        var riskSignal = Normalize(request.RiskSignal) ?? throw new ValidationException("Risk signal is required.");
        var notes = Normalize(request.Notes);
        ClientRelatedParty? relatedParty = null;
        if (request.ClientRelatedPartyId is not null)
        {
            relatedParty = await db.ClientRelatedParties
                .AsNoTracking()
                .Include(party => party.Roles)
                .SingleOrDefaultAsync(party => party.Id == request.ClientRelatedPartyId && party.ClientId == clientId && party.IsActive)
                ?? throw new ValidationException("The selected related party is not active for this client.");
            subjectName = relatedParty.DisplayName;
            subjectType = MapRelatedPartySubjectType(relatedParty.Roles.Select(role => role.RoleCode));
        }
        ValidateScreeningReview(requirement.EvidenceType, client.ClientCategory, subjectType, outcome, riskSignal, notes);
        var escalationRequired = IsSanctionsEscalation(requirement.EvidenceType, outcome);
        var item = new ClientEvidenceItem
        {
            ClientId = client.Id,
            ClientEvidenceRequirementId = requirement.Id,
            EvidenceType = requirement.EvidenceType,
            Title = $"{requirement.Title}: {subjectName}",
            ReceivedDate = reviewDate,
            VerifiedDate = reviewDate,
            Reviewer = userName,
            Status = ClientEvidenceStatuses.Verified,
            ScreeningReviewDate = reviewDate,
            ScreeningSubjectType = subjectType,
            ScreeningSubjectName = subjectName,
            ClientRelatedPartyId = relatedParty?.Id,
            ScreeningOutcome = outcome,
            ScreeningRiskSignal = riskSignal,
            EscalationRequired = escalationRequired,
            Notes = notes,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = userName
        };
        db.ClientEvidenceItems.Add(item);
        await db.SaveChangesAsync();
        await AddAuditAsync("ClientEvidenceItem", item.Id, "RecordReview", new
        {
            item.Id,
            item.ClientId,
            item.ClientEvidenceRequirementId,
            item.EvidenceType,
            item.Title,
            item.VerifiedDate,
            item.Reviewer,
            item.ScreeningSubjectType,
            item.ScreeningSubjectName,
            item.ScreeningOutcome,
            item.ScreeningRiskSignal,
            item.EscalationRequired,
            item.Notes
        }, userName, auditReason);
        await db.SaveChangesAsync();
        return item.Id;
    }

    public async Task CreateExceptionAsync(int clientId, int requirementId, string exceptionReason, DateOnly? reviewDate, string? userName, string reason)
    {
        RequireReason(reason);
        var normalizedException = Normalize(exceptionReason) ?? throw new ValidationException("Exception reason is required.");
        if (!await db.Clients.AnyAsync(client => client.Id == clientId))
        {
            throw new InvalidOperationException("Client not found.");
        }

        if (!await db.ClientEvidenceRequirements.AnyAsync(requirement => requirement.Id == requirementId))
        {
            throw new InvalidOperationException("Evidence requirement not found.");
        }

        foreach (var existing in await db.ClientEvidenceExceptions
            .Where(exception => exception.ClientId == clientId && exception.ClientEvidenceRequirementId == requirementId && exception.IsActive)
            .ToListAsync())
        {
            existing.IsActive = false;
        }

        var evidenceException = new ClientEvidenceException
        {
            ClientId = clientId,
            ClientEvidenceRequirementId = requirementId,
            Reason = normalizedException,
            ApprovedBy = userName ?? "Unknown",
            ReviewDate = reviewDate
        };
        db.ClientEvidenceExceptions.Add(evidenceException);
        await db.SaveChangesAsync();
        await AddAuditAsync("ClientEvidenceException", evidenceException.Id, "ApproveException", evidenceException, userName, reason);
        await db.SaveChangesAsync();
    }

    public async Task CreateTaskForRequirementAsync(int clientId, int requirementId, string? owner, DateOnly? dueDate, string? userName, string reason)
    {
        RequireReason(reason);
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        var requirement = await db.ClientEvidenceRequirements.AsNoTracking().SingleOrDefaultAsync(requirement => requirement.Id == requirementId)
            ?? throw new InvalidOperationException("Evidence requirement not found.");

        var task = new ComplianceTask
        {
            Title = $"Resolve evidence gap: {client.DisplayName} - {requirement.Title}",
            Description = requirement.Description,
            Owner = Normalize(owner),
            DueDate = dueDate,
            Priority = requirement.IsBlocking ? "High" : "Medium",
            Status = ComplianceStatuses.Draft,
            LinkedEntityType = "ClientEvidenceRequirement",
            LinkedEntityId = requirement.Id,
            UpdatedBy = userName
        };
        db.ComplianceTasks.Add(task);
        await AddAuditAsync("ComplianceTask", task.Id, "CreateEvidenceTask", task, userName, reason);
        await db.SaveChangesAsync();
    }

    private async Task EnsureDefaultRequirementsAsync()
    {
        if (await db.ClientEvidenceRequirements.AnyAsync())
        {
            return;
        }

        db.ClientEvidenceRequirements.AddRange(
            Requirement("Identity", "Identity", "Identity and verification document", "Current identity, registration or trust instrument evidence.", 10, true, false),
            Requirement("Address", "Address", "Residential or operating address evidence", "Current address evidence or acceptable verification note.", 20, true, false),
            Requirement("TaxResidency", "Profile", "Tax and residency profile", "Tax number, residency and relevant cross-border indicators.", 30, true, false),
            Requirement("SourceOfFunds", "Funds and wealth", "Source of funds evidence", "Corroborated source of funds for investment activity.", 40, true, false),
            Requirement("SourceOfWealth", "Funds and wealth", "Source of wealth evidence", "Corroborated source of wealth where required by risk profile.", 50, true, false),
            Requirement("BeneficialOwnership", "Ownership", "Ownership and control evidence", "Beneficial ownership, trustees, directors, authorised persons or controlling persons.", 60, true, false),
            Requirement("PepPip", "Screening", "PEP/PIP screening evidence", "Recorded PEP/PIP screening result and review outcome.", 70, true, false),
            Requirement("SanctionsTfs", "Screening", "Sanctions/TFS screening evidence", "Recorded sanctions, TFS and PF screening result.", 80, true, false),
            Requirement("AdverseInformation", "Screening", "Adverse information review", "Adverse media or other adverse information search result where applicable.", 90, false, false),
            Requirement("ProductService", "Relationship", "Product and service exposure", "Products, services, wrappers and administrator/platform exposure.", 100, true, false),
            Requirement("DeliveryChannel", "Relationship", "Delivery channel evidence", "Face-to-face, remote, intermediary or electronic delivery-channel evidence.", 110, true, false),
            Requirement("Geography", "Relationship", "Geographic exposure evidence", "Residence, nationality, source/destination geography and offshore exposure.", 120, true, false),
            Requirement("LegalPersonRegistration", "Ownership", "Legal-person registration evidence", "Company, close corporation or other entity registration and authority evidence.", 130, true, false, ClientCategories.LegalPerson),
            Requirement("LegalPersonControllers", "Ownership", "Directors, members and controlling persons", "Current directors, members, authorised persons and natural persons exercising control.", 140, true, false, ClientCategories.LegalPerson),
            Requirement("TrustDeed", "Ownership", "Trust deed and authority evidence", "Trust deed, letters of authority and current trustee authority evidence.", 150, true, false, ClientCategories.Trust),
            Requirement("TrustParties", "Ownership", "Trust parties and beneficial ownership", "Founder, trustees, beneficiaries and natural persons exercising effective control.", 160, true, false, ClientCategories.Trust));

        await db.SaveChangesAsync();
    }

    private static ClientEvidenceRequirement Requirement(string type, string group, string title, string description, int sortOrder, bool blocking, bool expiry, string category = "All") => new()
    {
        ClientCategory = category,
        RequirementGroup = group,
        EvidenceType = type,
        Title = title,
        Description = description,
        SortOrder = sortOrder,
        IsBlocking = blocking,
        RequiresVerification = true,
        RequiresExpiryDate = expiry
    };

    private async Task<List<ClientEvidenceRequirement>> LoadActiveRequirementsAsync(CancellationToken cancellationToken = default) =>
        await db.ClientEvidenceRequirements
            .AsNoTracking()
            .Where(requirement => requirement.Status == ClientEvidenceRequirementStatuses.Active)
            .OrderBy(requirement => requirement.SortOrder)
            .ToListAsync(cancellationToken);

    private static ClientEvidenceReadinessCounts CalculateReadiness(
        int clientId,
        string clientCategory,
        IReadOnlyList<ClientEvidenceRequirement> requirements,
        IReadOnlyList<ClientEvidenceItem> items,
        IReadOnlyList<ClientEvidenceException> exceptions,
        DateOnly today)
    {
        var applicableRequirements = ActiveForCategory(requirements, clientCategory);
        var complete = 0;
        var exceptionCount = 0;
        var blocked = 0;
        foreach (var requirement in applicableRequirements)
        {
            var matchedItems = items.Where(item =>
                item.ClientId == clientId &&
                ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus) &&
                (item.ClientEvidenceRequirementId == requirement.Id || item.EvidenceType == requirement.EvidenceType));
            var isComplete = matchedItems.Any(item => IsEvidenceComplete(requirement, item, today));
            var isExceptioned = exceptions.Any(exception =>
                exception.ClientId == clientId &&
                exception.ClientEvidenceRequirementId == requirement.Id &&
                !IsExpired(exception.ReviewDate, today));

            if (isComplete)
            {
                complete++;
            }
            else if (isExceptioned)
            {
                exceptionCount++;
            }
            else if (requirement.IsBlocking)
            {
                blocked++;
            }
        }

        return new ClientEvidenceReadinessCounts(applicableRequirements.Count, complete, exceptionCount, blocked);
    }

    private static List<ClientEvidenceRequirement> ActiveForCategory(IReadOnlyList<ClientEvidenceRequirement> requirements, string? clientCategory)
    {
        var category = string.IsNullOrWhiteSpace(clientCategory) ? ClientCategories.NaturalPerson : clientCategory;
        return requirements
            .Where(requirement => requirement.ClientCategory == "All" || requirement.ClientCategory == category)
            .OrderBy(requirement => requirement.SortOrder)
            .ToList();
    }

    private static bool IsEvidenceComplete(ClientEvidenceRequirement requirement, ClientEvidenceItem item, DateOnly today)
    {
        if (item.Status is ClientEvidenceStatuses.Rejected or ClientEvidenceStatuses.Replaced)
        {
            return false;
        }

        if (requirement.RequiresVerification && item.VerifiedDate is null)
        {
            return false;
        }

        if (item.ExpiryDate.HasValue && item.ExpiryDate.Value < today)
        {
            return false;
        }

        if (requirement.RequiresExpiryDate && item.ExpiryDate is null)
        {
            return false;
        }

        return true;
    }

    private static bool IsExpired(DateOnly? date, DateOnly today) => date.HasValue && date.Value < today;

    private static ClientEvidenceMatchResult MatchClient(string relativePath, IReadOnlyList<Client> clients)
    {
        var firstSegment = FirstPathSegment(relativePath);
        var normalizedFirstSegment = NormalizeClientAlias(firstSegment);
        if (!string.IsNullOrWhiteSpace(normalizedFirstSegment))
        {
            var exactFolderMatches = clients
                .SelectMany(client => ClientAliases(client).Select(alias => new { Client = client, Alias = alias }))
                .Where(match => match.Alias == normalizedFirstSegment)
                .Select(match => match.Client)
                .DistinctBy(client => client.Id)
                .ToList();
            if (exactFolderMatches.Count == 1)
            {
                return new(exactFolderMatches[0], 1, "Client folder segment match.");
            }

            if (exactFolderMatches.Count > 1)
            {
                return new(null, exactFolderMatches.Count, "Multiple clients matched the folder segment.");
            }
        }

        var normalizedPath = NormalizeToken(relativePath);
        var matches = new List<(Client Client, int Score, string Reason)>();
        foreach (var client in clients)
        {
            var score = 0;
            var reason = "";
            if (!string.IsNullOrWhiteSpace(client.KanaanId) && normalizedPath.Contains(NormalizeToken(client.KanaanId)))
            {
                score += 40;
                reason = "Kanaan ID match.";
            }

            foreach (var alias in ClientAliases(client))
            {
                if (alias.Length >= 4 && normalizedPath.Contains(alias))
                {
                    score += 25;
                    reason = "Client alias match.";
                    break;
                }
            }

            if (score > 0)
            {
                matches.Add((client, score, reason));
            }
        }

        if (matches.Count == 0)
        {
            return new(null, 0, "No client match.");
        }

        var bestScore = matches.Max(match => match.Score);
        var bestMatches = matches.Where(match => match.Score == bestScore).ToList();
        return bestMatches.Count == 1
            ? new(bestMatches[0].Client, matches.Count, bestMatches[0].Reason)
            : new(null, bestMatches.Count, "Multiple clients matched equally.");
    }

    private static string? SuggestEvidenceType(string relativePath)
    {
        var text = NormalizeToken(relativePath);
        var folderText = NormalizeToken(Path.GetDirectoryName(relativePath));
        if (ContainsAny(text, "trustdeed", "trustakte", "lettersofauthority", "letterauthority")) return "TrustDeed";
        if (ContainsAny(text, "trustee", "trustees", "founder")) return "TrustParties";
        if (ContainsAny(text, "cipc", "companyregistration", "cor14", "cor39", "ck1", "ck2")) return "LegalPersonRegistration";
        if (ContainsAny(text, "director", "directors", "member", "members", "authorisedsignatory", "authorizedsignatory", "resolution")) return "LegalPersonControllers";
        if (ContainsAny(text, "beneficiary", "beneficiaries", "beneficial", "ownership", "director", "trustee")) return "BeneficialOwnership";
        if (ContainsAny(text, "proofaddress", "proofofaddress", "address", "adres", "proofresidence", "utility", "municipal", "residence")) return "Address";
        if (ContainsAny(folderText, "fica", "kyc") || ContainsAny(text, "identity", "identitydocument", "passport", "registration")) return "Identity";
        if (ContainsAny(folderText, "tax") || ContainsAny(text, "tax", "sars", "residency")) return "TaxResidency";
        if (ContainsAny(text, "bop", "forex", "foreignexchange", "dealing", "dealconfirmation", "dealsettlement", "bankconfirmation", "bankstatement", "proofpayment", "proofofpayment", "sourcefund", "funds", "deposit")) return "SourceOfFunds";
        if (ContainsAny(text, "sourcewealth", "wealth", "inheritance", "salary", "income")) return "SourceOfWealth";
        if (ContainsAny(text, "pep", "pip", "prominent")) return "PepPip";
        if (ContainsAny(text, "sanction", "tfs", "goaml", "screening")) return "SanctionsTfs";
        if (ContainsAny(text, "adverse", "media")) return "AdverseInformation";
        if (ContainsAny(text, "remote", "emailinstruction", "callback", "clientconsent", "consent")) return "DeliveryChannel";
        if (ContainsAny(text, "offshore", "country", "geography", "jurisdiction")) return "Geography";
        if (ContainsAny(text, "policy", "policies", "product", "investment", "mandate", "applicationform", "applicationforms", "portfolio", "unittrust", "hedgefund", "annuity")) return "ProductService";
        return null;
    }

    private async Task ApplyEvidenceCategoryInferenceAsync(Client client, string relativePath, string evidenceType, string? userName, string reason)
    {
        if (!ClientCategoryInference.CanApplyInferredCategory(client))
        {
            return;
        }

        var inferred = ClientCategoryInference.InferFromEvidence(relativePath, evidenceType);
        if (inferred is null || string.Equals(client.ClientCategory, inferred.Category, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previous = new
        {
            client.ClientCategory,
            client.ClientCategorySource,
            client.ClientCategoryReason
        };
        client.ClientCategory = inferred.Category;
        client.ClientCategorySource = inferred.Source;
        client.ClientCategoryReason = inferred.Reason;
        client.ClientCategoryUpdatedAtUtc = DateTime.UtcNow;
        client.ClientCategoryUpdatedBy = userName;
        await AddAuditAsync("Client", client.Id, "InferClientCategoryFromEvidence", new
        {
            Previous = previous,
            Current = new
            {
                client.ClientCategory,
                client.ClientCategorySource,
                client.ClientCategoryReason,
                client.ClientCategoryUpdatedAtUtc,
                client.ClientCategoryUpdatedBy
            },
            EvidenceType = evidenceType,
            RelativePath = relativePath
        }, userName, reason);
    }

    private async Task VerifyEvidenceItemAsync(ClientEvidenceItem item, DateOnly? receivedDate, DateOnly? expiryDate, string? userName, string reason)
    {
        item.ReceivedDate = receivedDate;
        item.VerifiedDate = DateOnly.FromDateTime(DateTime.Today);
        item.ExpiryDate = expiryDate;
        item.Reviewer = userName;
        item.Status = ClientEvidenceStatuses.Verified;
        item.VerificationPolicy = "VerifiedByReviewer";
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedBy = userName;
        await AddAuditAsync("ClientEvidenceItem", item.Id, "Verify", item, userName, reason);
    }

    private async Task ResolvePriorScanFilesForPathAsync(ClientEvidenceScanFile scanFile, int clientId, CancellationToken cancellationToken)
    {
        var priorFiles = await db.ClientEvidenceScanFiles
            .Where(file =>
                file.Id != scanFile.Id &&
                file.FullPath == scanFile.FullPath &&
                file.FileSha256 == scanFile.FileSha256 &&
                (file.MatchStatus == ClientEvidenceScanFileStatuses.Unmatched ||
                    file.MatchStatus == ClientEvidenceScanFileStatuses.Ambiguous ||
                    file.MatchStatus == ClientEvidenceScanFileStatuses.OwnershipReview))
            .ToListAsync(cancellationToken);

        foreach (var priorFile in priorFiles)
        {
            priorFile.ClientId = clientId;
            priorFile.MatchStatus = ClientEvidenceScanFileStatuses.Linked;
            priorFile.MatchReason = "Resolved by client-specific folder scan.";
            priorFile.CandidateCount = 1;
            if (string.IsNullOrWhiteSpace(priorFile.SuggestedEvidenceType))
            {
                priorFile.SuggestedEvidenceType = scanFile.SuggestedEvidenceType;
            }
        }
    }

    private async Task EnsureDefaultOwnershipAliasesAsync(string folderPath, string? userName)
    {
        var clients = (await db.Clients
            .Where(client => client.ClientFolder != null)
            .ToListAsync())
            .Where(client => SameFolder(client.ClientFolder, folderPath))
            .ToList();
        if (clients.Count < 2)
        {
            return;
        }

        var existing = await db.ClientEvidenceOwnershipAliases
            .Where(alias => alias.FolderPath == folderPath)
            .ToListAsync();
        foreach (var client in clients)
        {
            var aliases = new[]
            {
                client.DisplayName,
                client.FullName,
                string.IsNullOrWhiteSpace(client.Initials) ? null : $"{client.Initials} {client.SurnameOrEntityName}"
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var aliasValue in aliases)
            {
                if (existing.Any(alias =>
                    alias.ClientId == client.Id &&
                    string.Equals(alias.Alias, aliasValue, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var alias = new ClientEvidenceOwnershipAlias
                {
                    ClientId = client.Id,
                    FolderPath = folderPath,
                    Alias = aliasValue,
                    IsJoint = IsJointClient(client),
                    CreatedBy = userName
                };
                db.ClientEvidenceOwnershipAliases.Add(alias);
                existing.Add(alias);
            }
        }

        await db.SaveChangesAsync();
    }

    private static ClientEvidenceMatchResult MatchSharedFolderOwner(
        string relativePath,
        IReadOnlyList<Client> clients,
        IReadOnlyList<ClientEvidenceOwnershipAlias> aliases)
    {
        var clientIds = clients.Select(client => client.Id).ToHashSet();
        var matchingAliases = aliases
            .Where(alias => clientIds.Contains(alias.ClientId) && AliasMatches(relativePath, alias.Alias))
            .ToList();
        var jointMatches = matchingAliases.Where(alias => alias.IsJoint).Select(alias => alias.ClientId).Distinct().ToList();
        var matchingClientIds = jointMatches.Count > 0
            ? jointMatches
            : matchingAliases.Select(alias => alias.ClientId).Distinct().ToList();
        if (matchingClientIds.Count == 1)
        {
            var client = clients.Single(item => item.Id == matchingClientIds[0]);
            var matchedNames = matchingAliases
                .Where(alias => alias.ClientId == client.Id)
                .Select(alias => alias.Alias)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return new(client, 1, $"Explicit shared-folder alias matched: {string.Join(", ", matchedNames)}.");
        }

        return matchingClientIds.Count > 1
            ? new(null, matchingClientIds.Count, "Conflicting shared-folder aliases matched; ownership review required.")
            : new(null, clients.Count, "No explicit client alias matched; ownership review required.");
    }

    private static bool AliasMatches(string relativePath, string alias)
    {
        var text = NormalizeOwnershipText(relativePath);
        var normalizedAlias = NormalizeOwnershipText(alias);
        if (normalizedAlias.Length < 3)
        {
            return false;
        }

        return Regex.IsMatch(text, $@"(?:^|\s){Regex.Escape(normalizedAlias)}(?:$|\s)", RegexOptions.IgnoreCase);
    }

    private static string NormalizeOwnershipText(string value) =>
        Regex.Replace(
            value.ToLowerInvariant()
                .Replace("&", " and ", StringComparison.Ordinal)
                .Replace("_", " ", StringComparison.Ordinal)
                .Replace("-", " ", StringComparison.Ordinal),
            @"[^\p{L}\p{N}]+",
            " ").Trim();

    private static bool SameFolder(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        static string NormalizePath(string value) =>
            Path.GetFullPath(value.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFolderKey(string value) =>
        Path.GetFullPath(value.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsJointClient(Client client)
    {
        var text = $"{client.Title} {client.Initials} {client.DisplayName}";
        return text.Contains('&') ||
            Regex.IsMatch(text, @"\b(and|en)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsJointClientSummary(ClientEvidenceClientSummaryModel client) =>
        Regex.IsMatch(client.DisplayName, @"(&|\b(and|en)\b)", RegexOptions.IgnoreCase);

    private static ClientEvidenceItem CopyEvidenceForClient(ClientEvidenceItem source, int clientId, string? userName) => new()
    {
        ClientId = clientId,
        ClientEvidenceRequirementId = source.ClientEvidenceRequirementId,
        EvidenceType = source.EvidenceType,
        Title = source.Title,
        SourcePath = source.SourcePath,
        RelativePath = source.RelativePath,
        FileName = source.FileName,
        FileSha256 = source.FileSha256,
        FileSizeBytes = source.FileSizeBytes,
        FileLastWriteTimeUtc = source.FileLastWriteTimeUtc,
        Status = ClientEvidenceStatuses.Linked,
        SelectionStatus = ClientEvidenceSelectionStatuses.Candidate,
        VerificationPolicy = "ManualRequired",
        OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
        OwnershipConfidence = 100,
        OwnershipReason = "Created by shared-folder ownership review.",
        OwnershipReviewedAtUtc = DateTime.UtcNow,
        OwnershipReviewedBy = userName,
        UpdatedBy = userName
    };

    private async Task RefreshEvidenceSelectionsAsync(int clientId, string? userName, string reason, CancellationToken cancellationToken)
    {
        var items = await db.ClientEvidenceItems
            .Where(item => item.ClientId == clientId &&
                (item.OwnershipStatus == ClientEvidenceOwnershipStatuses.Confirmed ||
                    item.OwnershipStatus == ClientEvidenceOwnershipStatuses.AutoAssigned) &&
                item.Status != ClientEvidenceStatuses.Rejected &&
                item.Status != ClientEvidenceStatuses.Replaced)
            .ToListAsync(cancellationToken);

        foreach (var group in items.GroupBy(item => item.EvidenceType))
        {
            var ranked = group
                .Select(item => new
                {
                    Item = item,
                    Score = ScoreEvidenceSelection(item)
                })
                .OrderByDescending(entry => entry.Score.Score)
                .ThenByDescending(entry => entry.Item.FileLastWriteTimeUtc ?? entry.Item.CreatedAtUtc)
                .ThenByDescending(entry => entry.Item.FileSizeBytes ?? 0)
                .ThenByDescending(entry => entry.Item.Id)
                .ToList();

            var current = ranked.FirstOrDefault();
            if (current is null)
            {
                continue;
            }

            foreach (var entry in ranked)
            {
                var item = entry.Item;
                var newStatus = item.Id == current.Item.Id
                    ? ClientEvidenceSelectionStatuses.Current
                    : ClientEvidenceSelectionStatuses.Historical;
                int? supersededById = item.Id == current.Item.Id ? null : current.Item.Id;
                var reasonText = item.Id == current.Item.Id
                    ? $"Selected as current {item.EvidenceType} evidence: {entry.Score.Reason}"
                    : $"Historical {item.EvidenceType} evidence; current item is #{current.Item.Id}.";

                if (item.SelectionStatus == newStatus &&
                    item.SelectionConfidence == entry.Score.Score &&
                    item.SelectionReason == reasonText &&
                    item.SupersededByClientEvidenceItemId == supersededById)
                {
                    continue;
                }

                var previous = new
                {
                    item.SelectionStatus,
                    item.SelectionConfidence,
                    item.SelectionReason,
                    item.SupersededByClientEvidenceItemId
                };

                item.SelectionStatus = newStatus;
                item.SelectionConfidence = entry.Score.Score;
                item.SelectionReason = reasonText;
                item.SelectedAtUtc = DateTime.UtcNow;
                item.SelectedBy = userName;
                item.VerificationPolicy = item.VerifiedDate.HasValue ? "VerifiedByReviewer" : "ManualRequired";
                item.SupersededByClientEvidenceItemId = supersededById;
                item.UpdatedAtUtc = DateTime.UtcNow;
                item.UpdatedBy = userName;

                await AddAuditAsync("ClientEvidenceItem", item.Id, "SelectCurrentEvidence", new
                {
                    Previous = previous,
                    Current = new
                    {
                        item.SelectionStatus,
                        item.SelectionConfidence,
                        item.SelectionReason,
                        item.SupersededByClientEvidenceItemId,
                        item.VerificationPolicy
                    },
                    item.ClientId,
                    item.EvidenceType,
                    item.FileName,
                    item.RelativePath
                }, userName, reason);
            }
        }
    }

    private static ClientEvidenceSelectionScore ScoreEvidenceSelection(ClientEvidenceItem item)
    {
        var score = item.VerifiedDate.HasValue ? 10_000 : 0;
        var reasons = new List<string>();
        if (item.VerifiedDate.HasValue)
        {
            reasons.Add("reviewer verified");
        }

        var extension = Path.GetExtension(item.FileName ?? item.SourcePath ?? "").ToLowerInvariant();
        if (extension is ".pdf" or ".jpg" or ".jpeg" or ".png")
        {
            score += 40;
            reasons.Add("directly reviewable file");
        }
        else if (extension is ".doc" or ".docx" or ".xls" or ".xlsx")
        {
            score += 15;
            reasons.Add("office document");
        }
        else if (extension is ".msg" or ".eml")
        {
            score -= 20;
            reasons.Add("email wrapper");
        }

        var text = NormalizeToken($"{item.RelativePath} {item.FileName} {item.Title}");
        if (ContainsAny(text, "temporary", "temp", "~$"))
        {
            score -= 25;
            reasons.Add("temporary-path penalty");
        }

        if (ContainsAny(text, "certified", "cert", "signed", "updated", "current", "2025", "2026"))
        {
            score += 20;
            reasons.Add("current/certified/signed naming");
        }

        if (ContainsAny(text, "old", "previous", "draft", "unsigned"))
        {
            score -= 15;
            reasons.Add("old/draft naming penalty");
        }

        if (item.FileLastWriteTimeUtc.HasValue)
        {
            var yearBonus = Math.Clamp(item.FileLastWriteTimeUtc.Value.Year - 2010, 0, 25);
            score += yearBonus;
            reasons.Add($"latest file date {item.FileLastWriteTimeUtc.Value:yyyy-MM-dd}");
        }

        if ((item.FileSizeBytes ?? 0) > 50_000)
        {
            score += 5;
            reasons.Add("substantive file size");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("default candidate ranking");
        }

        return new(score, string.Join("; ", reasons));
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task AddAuditAsync(string entityType, int entityId, string action, object entity, string? userName, string reason)
    {
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserName = userName,
            TimestampUtc = DateTime.UtcNow,
            Reason = reason,
            NewValueJson = JsonSerializer.Serialize(entity, AuditJsonOptions)
        });
        await Task.CompletedTask;
    }

    private async Task SaveScanProgressIfNeededAsync(ClientEvidenceScanRun run, CancellationToken cancellationToken)
    {
        if (run.TotalFiles % 25 == 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("A reason is required.");
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : NonAlphaNumericRegex().Replace(value.ToUpperInvariant(), "");

    private static string? LastPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.LastOrDefault();
    }

    private static string? FirstPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static IEnumerable<string> ClientAliases(Client client)
    {
        var aliases = new HashSet<string>();
        AddAlias(aliases, LastPathSegment(client.ClientFolder));
        AddAlias(aliases, client.DisplayName);
        AddAlias(aliases, client.FullName);
        if (!string.IsNullOrWhiteSpace(client.SurnameOrEntityName) && !string.IsNullOrWhiteSpace(client.Initials))
        {
            AddAlias(aliases, $"{client.SurnameOrEntityName} {client.Initials}");
            AddAlias(aliases, $"{client.Initials} {client.SurnameOrEntityName}");
        }

        var fullNameInitials = InitialsFromName(client.FullName);
        if (!string.IsNullOrWhiteSpace(client.SurnameOrEntityName) && !string.IsNullOrWhiteSpace(fullNameInitials))
        {
            AddAlias(aliases, $"{client.SurnameOrEntityName} {fullNameInitials}");
            AddAlias(aliases, $"{fullNameInitials} {client.SurnameOrEntityName}");
        }

        return aliases;
    }

    private static void AddAlias(HashSet<string> aliases, string? value)
    {
        var alias = NormalizeClientAlias(value);
        if (!string.IsNullOrWhiteSpace(alias) && alias.Length >= 4)
        {
            aliases.Add(alias);
        }
    }

    private static string NormalizeClientAlias(string? value)
    {
        var token = NormalizeToken(value);
        return token is "KANAAN" or "CLIENT" or "CLIENTS" ? "" : token;
    }

    private static string InitialsFromName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var words = NonAlphaNumericSpaceRegex()
            .Replace(value.ToUpperInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 1 && word is not ("MR" or "MRS" or "MS" or "DR" or "PROF" or "TRUST"))
            .ToList();
        return string.Concat(words.Select(word => word[0]));
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(value => text.Contains(NormalizeToken(value)));

    private static bool IsReviewOnlyEvidenceType(string evidenceType) =>
        evidenceType is "PepPip" or "SanctionsTfs" or "AdverseInformation";

    private static List<ClientEvidenceScreeningSubjectModel> BuildScreeningSubjects(Client client)
    {
        var subjects = new List<ClientEvidenceScreeningSubjectModel>
        {
            new()
            {
                SubjectType = ClientEvidenceScreeningSubjectTypes.Client,
                SubjectName = client.DisplayName
            }
        };

        subjects.AddRange(client.Relationships
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Name))
            .OrderBy(relationship => relationship.RelationshipType)
            .ThenBy(relationship => relationship.Name)
            .Select(relationship => new ClientEvidenceScreeningSubjectModel
            {
                SubjectType = MapRelationshipSubjectType(relationship.RelationshipType),
                SubjectName = relationship.Name!.Trim()
            }));

        subjects.AddRange(client.RelatedParties
            .Where(party => party.IsActive && !string.IsNullOrWhiteSpace(party.DisplayName))
            .OrderBy(party => party.DisplayName)
            .Select(party => new ClientEvidenceScreeningSubjectModel
            {
                ClientRelatedPartyId = party.Id,
                SubjectType = MapRelatedPartySubjectType(party.Roles.Select(role => role.RoleCode)),
                SubjectName = party.DisplayName
            }));

        return subjects
            .DistinctBy(subject => $"{subject.SubjectType}|{NormalizeToken(subject.SubjectName)}")
            .ToList();
    }

    private static string MapRelationshipSubjectType(string? relationshipType)
    {
        var normalized = NormalizeToken(relationshipType);
        if (normalized.Contains("TRUSTEE")) return ClientEvidenceScreeningSubjectTypes.Trustee;
        if (normalized.Contains("BENEFICIARY")) return ClientEvidenceScreeningSubjectTypes.Beneficiary;
        if (normalized.Contains("DIRECTOR")) return ClientEvidenceScreeningSubjectTypes.Director;
        if (normalized.Contains("CONTROLLER") || normalized.Contains("OWNER")) return ClientEvidenceScreeningSubjectTypes.Controller;
        if (normalized.Contains("AUTHORISED") || normalized.Contains("AUTHORIZED") || normalized.Contains("SIGNATORY")) return ClientEvidenceScreeningSubjectTypes.AuthorisedPerson;
        return ClientEvidenceScreeningSubjectTypes.Other;
    }

    private static string MapRelatedPartySubjectType(IEnumerable<string> roles)
    {
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (roleSet.Contains(ClientRelatedPartyRoles.Trustee)) return ClientEvidenceScreeningSubjectTypes.Trustee;
        if (roleSet.Contains(ClientRelatedPartyRoles.Beneficiary)) return ClientEvidenceScreeningSubjectTypes.Beneficiary;
        if (roleSet.Contains(ClientRelatedPartyRoles.Director)) return ClientEvidenceScreeningSubjectTypes.Director;
        if (roleSet.Overlaps([ClientRelatedPartyRoles.Controller, ClientRelatedPartyRoles.BeneficialOwner, ClientRelatedPartyRoles.MemberShareholder]))
            return ClientEvidenceScreeningSubjectTypes.Controller;
        if (roleSet.Contains(ClientRelatedPartyRoles.AuthorisedPerson)) return ClientEvidenceScreeningSubjectTypes.AuthorisedPerson;
        return ClientEvidenceScreeningSubjectTypes.Other;
    }

    private static void ValidateScreeningReview(string evidenceType, string clientCategory, string subjectType, string outcome, string riskSignal, string? notes)
    {
        if (!ClientEvidenceScreeningSubjectTypes.All.Contains(subjectType))
        {
            throw new ValidationException("Screening subject type is invalid.");
        }

        if (!ClientEvidenceRiskSignals.All.Contains(riskSignal))
        {
            throw new ValidationException("Risk signal is invalid.");
        }

        var allowedOutcomes = ClientEvidenceScreeningOutcomes.ForEvidenceType(evidenceType);
        if (!allowedOutcomes.Contains(outcome))
        {
            throw new ValidationException("Screening outcome is invalid for this requirement.");
        }

        var notesRequired = riskSignal is ClientEvidenceRiskSignals.Medium or ClientEvidenceRiskSignals.High ||
            clientCategory is not ClientCategories.NaturalPerson ||
            subjectType is not ClientEvidenceScreeningSubjectTypes.Client;
        if (notesRequired && string.IsNullOrWhiteSpace(notes))
        {
            throw new ValidationException("Notes are required for medium or high risk, non-natural-person clients, and related-party screening.");
        }
    }

    private static bool IsSanctionsEscalation(string evidenceType, string outcome) =>
        evidenceType == "SanctionsTfs" && outcome == ClientEvidenceScreeningOutcomes.ConfirmedMatch;

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NonAlphaNumericSpaceRegex();

    private sealed record ClientEvidenceReadinessCounts(int RequiredCount, int CompleteCount, int ExceptionCount, int BlockedCount)
    {
        public bool IsReadyForRiskAssessment => BlockedCount == 0;
    }

    private sealed record ClientEvidenceSelectionScore(int Score, string Reason);

    private sealed record ClientEvidenceMatchResult(Client? Client, int CandidateCount, string Reason);
}

public sealed class ClientEvidenceDashboardModel
{
    public string? ScanRootPath { get; set; }
    public ClientEvidenceScanRunModel? LatestScanRun { get; set; }
    public int RequirementCount { get; set; }
    public int ReadyClientCount { get; set; }
    public int BlockedClientCount { get; set; }
    public List<ClientEvidenceClientSummaryModel> Clients { get; set; } = [];
    public List<ClientEvidenceScanFileModel> UnmatchedFiles { get; set; } = [];
    public List<ClientEvidenceSharedFolderModel> SharedFolders { get; set; } = [];
    public List<ClientEvidenceOwnershipReviewModel> OwnershipReviews { get; set; } = [];
}

public sealed record ClientEvidencePortfolioReadiness(
    int RequiredCount,
    int CompleteCount,
    int ExceptionCount,
    int BlockedCount,
    bool IsReady);

public sealed class ClientEvidenceSharedFolderModel
{
    public string FolderPath { get; set; } = "";
    public List<ClientEvidenceFolderClientModel> Clients { get; set; } = [];
}

public sealed class ClientEvidenceFolderClientModel
{
    public int ClientId { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsJoint { get; set; }
    public List<ClientEvidenceOwnershipAliasModel> Aliases { get; set; } = [];
}

public sealed class ClientEvidenceOwnershipAliasModel
{
    public int Id { get; set; }
    public string Alias { get; set; } = "";
}

public sealed class ClientEvidenceOwnershipReviewModel
{
    public int SourceItemId { get; set; }
    public string? RelativePath { get; set; }
    public string EvidenceType { get; set; } = "";
    public string? OwnershipReason { get; set; }
    public List<ClientEvidenceFolderClientModel> CandidateClients { get; set; } = [];
}

public sealed class ClientEvidenceFolderBrowserModel
{
    public string CurrentPath { get; set; } = "";
    public string? ParentPath { get; set; }
    public List<string> Roots { get; set; } = [];
    public List<ClientEvidenceFolderModel> Folders { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public sealed class ClientEvidenceFolderModel
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
}

public sealed class ClientEvidenceClientSummaryModel
{
    public int ClientId { get; set; }
    public string DisplayName { get; set; } = "";
    public string SurnameOrEntityName { get; set; } = "";
    public string? KanaanId { get; set; }
    public string ClientCategory { get; set; } = ClientCategories.NaturalPerson;
    public string? ClientFolder { get; set; }
    public int RequiredCount { get; set; }
    public int CompleteCount { get; set; }
    public int ExceptionCount { get; set; }
    public int BlockedCount { get; set; }
    public int OwnershipBlockedCount { get; set; }
    public int LinkedEvidenceCount { get; set; }
    public int VerifiedEvidenceCount { get; set; }
    public bool IsReadyForRiskAssessment { get; set; }
}

public sealed class ClientEvidenceReadinessModel
{
    public int ClientId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? KanaanId { get; set; }
    public string ClientCategory { get; set; } = ClientCategories.NaturalPerson;
    public string? ClientFolder { get; set; }
    public int RequiredCount { get; set; }
    public int CompleteCount { get; set; }
    public int ExceptionCount { get; set; }
    public int BlockedCount { get; set; }
    public int LinkedEvidenceCount { get; set; }
    public int VerifiedEvidenceCount { get; set; }
    public bool IsReadyForRiskAssessment { get; set; }
    public List<string> OwnershipBlockers { get; set; } = [];
    public List<ClientEvidenceScreeningSubjectModel> ScreeningSubjects { get; set; } = [];
    public List<ClientEvidenceRequirementStatusModel> Requirements { get; set; } = [];
    public List<ClientEvidenceItemModel> EvidenceItems { get; set; } = [];
}

public sealed class ClientEvidenceRequirementStatusModel
{
    public int RequirementId { get; set; }
    public string RequirementGroup { get; set; } = "";
    public string EvidenceType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsBlocking { get; set; }
    public bool RequiresVerification { get; set; }
    public bool RequiresExpiryDate { get; set; }
    public bool IsComplete { get; set; }
    public bool IsExceptioned { get; set; }
    public bool IsBlocked { get; set; }
    public int LinkedItemCount { get; set; }
    public int VerifiedItemCount { get; set; }
    public bool CanRecordReview { get; set; }
    public string? ExceptionReason { get; set; }
    public List<ClientEvidenceItemModel> Items { get; set; } = [];
}

public sealed class ClientEvidenceItemModel
{
    public int Id { get; set; }
    public string EvidenceType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? RelativePath { get; set; }
    public string? FileName { get; set; }
    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? VerifiedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Reviewer { get; set; }
    public DateOnly? ScreeningReviewDate { get; set; }
    public string? ScreeningSubjectType { get; set; }
    public string? ScreeningSubjectName { get; set; }
    public string? ScreeningOutcome { get; set; }
    public string? ScreeningRiskSignal { get; set; }
    public bool EscalationRequired { get; set; }
    public string Status { get; set; } = "";
    public string OwnershipStatus { get; set; } = "";
    public string? OwnershipReason { get; set; }
    public string SelectionStatus { get; set; } = "";
    public int? SelectionConfidence { get; set; }
    public string? SelectionReason { get; set; }
    public string VerificationPolicy { get; set; } = "";
    public int? SupersededByClientEvidenceItemId { get; set; }
    public bool IsCurrentSelection => SelectionStatus == ClientEvidenceSelectionStatuses.Current;
    public bool CanOpen => !string.IsNullOrWhiteSpace(FileName) && IsOpenableFile(FileName);
    public bool IsImage => !string.IsNullOrWhiteSpace(FileName) && IsImageFile(FileName);
    public string FileUrl => $"/client-evidence/items/{Id}/file";

    public static ClientEvidenceItemModel FromItem(ClientEvidenceItem item) => new()
    {
        Id = item.Id,
        EvidenceType = item.EvidenceType,
        Title = item.Title,
        RelativePath = item.RelativePath,
        FileName = item.FileName,
        ReceivedDate = item.ReceivedDate,
        VerifiedDate = item.VerifiedDate,
        ExpiryDate = item.ExpiryDate,
        Reviewer = item.Reviewer,
        ScreeningReviewDate = item.ScreeningReviewDate,
        ScreeningSubjectType = item.ScreeningSubjectType,
        ScreeningSubjectName = item.ScreeningSubjectName,
        ScreeningOutcome = item.ScreeningOutcome,
        ScreeningRiskSignal = item.ScreeningRiskSignal,
        EscalationRequired = item.EscalationRequired,
        Status = item.Status,
        OwnershipStatus = item.OwnershipStatus,
        OwnershipReason = item.OwnershipReason,
        SelectionStatus = item.SelectionStatus,
        SelectionConfidence = item.SelectionConfidence,
        SelectionReason = item.SelectionReason,
        VerificationPolicy = item.VerificationPolicy,
        SupersededByClientEvidenceItemId = item.SupersededByClientEvidenceItemId
    };

    private static bool IsOpenableFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".jpg" or ".jpeg" or ".png" or ".txt" or ".msg" or ".eml";
    }

    private static bool IsImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png";
    }
}

public sealed class ClientEvidenceScreeningSubjectModel
{
    public int? ClientRelatedPartyId { get; set; }
    public string SubjectType { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string Label => $"{SubjectName} ({SubjectType})";
}

public sealed class ClientEvidenceScreeningReviewRequest
{
    public int? ClientRelatedPartyId { get; set; }
    public string? SubjectType { get; set; }
    public string? SubjectName { get; set; }
    public string? Outcome { get; set; }
    public string? RiskSignal { get; set; }
    public DateOnly? ReviewDate { get; set; }
    public string? Notes { get; set; }
}

public static class ClientEvidenceScreeningSubjectTypes
{
    public const string Client = "Client";
    public const string Trustee = "Trustee";
    public const string Beneficiary = "Beneficiary";
    public const string Director = "Director";
    public const string Controller = "Controller";
    public const string AuthorisedPerson = "AuthorisedPerson";
    public const string Other = "Other";

    public static readonly string[] All = [Client, Trustee, Beneficiary, Director, Controller, AuthorisedPerson, Other];
}

public static class ClientEvidenceRiskSignals
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";

    public static readonly string[] All = [Low, Medium, High];
}

public static class ClientEvidenceScreeningOutcomes
{
    public const string NoMatch = "NoMatch";
    public const string PossibleMatch = "PossibleMatch";
    public const string ConfirmedMatch = "ConfirmedMatch";
    public const string NoneFound = "NoneFound";
    public const string MaterialAdverseInfo = "MaterialAdverseInfo";

    public static readonly string[] PepPip = [NoMatch, PossibleMatch, ConfirmedMatch];
    public static readonly string[] SanctionsTfs = [NoMatch, PossibleMatch, ConfirmedMatch];
    public static readonly string[] AdverseInformation = [NoneFound, PossibleMatch, MaterialAdverseInfo];

    public static IReadOnlyList<string> ForEvidenceType(string evidenceType) => evidenceType switch
    {
        "PepPip" => PepPip,
        "SanctionsTfs" => SanctionsTfs,
        "AdverseInformation" => AdverseInformation,
        _ => []
    };
}

public sealed class ClientEvidenceScanRunModel
{
    public int Id { get; set; }
    public string RootPath { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string Status { get; set; } = "";
    public int TotalFiles { get; set; }
    public int LinkedFiles { get; set; }
    public int UnmatchedFiles { get; set; }
    public int AmbiguousFiles { get; set; }
    public int SkippedFiles { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ClientEvidenceScanFileModel
{
    public int Id { get; set; }
    public int? ClientId { get; set; }
    public string? ClientDisplayName { get; set; }
    public string RelativePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string MatchStatus { get; set; } = "";
    public string? SuggestedEvidenceType { get; set; }
    public string? MatchReason { get; set; }
    public int CandidateCount { get; set; }

    public static ClientEvidenceScanFileModel FromFile(ClientEvidenceScanFile file) => new()
    {
        Id = file.Id,
        ClientId = file.ClientId,
        ClientDisplayName = file.Client?.DisplayName,
        RelativePath = file.RelativePath,
        FileName = file.FileName,
        MatchStatus = file.MatchStatus,
        SuggestedEvidenceType = file.SuggestedEvidenceType,
        MatchReason = file.MatchReason,
        CandidateCount = file.CandidateCount
    };
}
