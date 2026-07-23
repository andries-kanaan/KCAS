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
                KanaanId = client.KanaanId,
                ClientCategory = client.ClientCategory,
                ClientFolder = client.ClientFolder
            })
            .ToListAsync();

        var requirements = await LoadActiveRequirementsAsync();
        var items = await db.ClientEvidenceItems.AsNoTracking().ToListAsync();
        var exceptions = await db.ClientEvidenceExceptions.AsNoTracking().Where(item => item.IsActive).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var client in clients)
        {
            var readiness = CalculateReadiness(client.ClientId, client.ClientCategory, requirements, items, exceptions, today);
            client.RequiredCount = readiness.RequiredCount;
            client.CompleteCount = readiness.CompleteCount;
            client.BlockedCount = readiness.BlockedCount;
            client.ExceptionCount = readiness.ExceptionCount;
            client.LinkedEvidenceCount = items.Count(item => item.ClientId == client.ClientId);
            client.VerifiedEvidenceCount = items.Count(item => item.ClientId == client.ClientId && item.VerifiedDate is not null);
            client.IsReadyForRiskAssessment = readiness.IsReadyForRiskAssessment;
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
            .Where(file => file.MatchStatus == ClientEvidenceScanFileStatuses.Unmatched || file.MatchStatus == ClientEvidenceScanFileStatuses.Ambiguous)
            .OrderByDescending(file => file.Id)
            .Take(100)
            .Select(file => ClientEvidenceScanFileModel.FromFile(file))
            .ToListAsync();

        return new ClientEvidenceDashboardModel
        {
            ScanRootPath = activeRoot?.RootPath,
            LatestScanRun = latestRun,
            Clients = clients,
            UnmatchedFiles = unmatchedFiles,
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

        var requirementRows = requirements
            .Select(requirement =>
            {
                var matchedItems = items.Where(item => item.ClientEvidenceRequirementId == requirement.Id || item.EvidenceType == requirement.EvidenceType).ToList();
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
            Requirements = requirementRows,
            EvidenceItems = items.Select(ClientEvidenceItemModel.FromItem).ToList(),
            RequiredCount = requirementRows.Count,
            CompleteCount = requirementRows.Count(row => row.IsComplete),
            ExceptionCount = requirementRows.Count(row => row.IsExceptioned),
            BlockedCount = requirementRows.Count(row => row.IsBlocked),
            LinkedEvidenceCount = items.Count,
            VerifiedEvidenceCount = items.Count(item => item.VerifiedDate is not null),
            IsReadyForRiskAssessment = requirementRows.All(row => !row.IsBlocked)
        };
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

    public Task<ClientEvidenceFolderBrowserModel> BrowseServerFoldersAsync(string? requestedPath)
    {
        var roots = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)
            .OrderBy(path => path)
            .ToList();

        var requested = Normalize(requestedPath);
        var currentPath = requested;
        if (currentPath is null || !Directory.Exists(currentPath))
        {
            currentPath = roots.FirstOrDefault() ?? Path.GetPathRoot(Environment.CurrentDirectory) ?? Environment.CurrentDirectory;
        }

        var model = new ClientEvidenceFolderBrowserModel
        {
            CurrentPath = currentPath,
            ParentPath = Directory.GetParent(currentPath)?.FullName,
            Roots = roots
        };

        try
        {
            model.Folders = Directory.EnumerateDirectories(currentPath)
                .Select(path => new DirectoryInfo(path))
                .OrderBy(directory => directory.Name)
                .Select(directory => new ClientEvidenceFolderModel
                {
                    Name = directory.Name,
                    FullPath = directory.FullName
                })
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            model.ErrorMessage = ex.Message;
            model.Folders = [];
        }

        return Task.FromResult(model);
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

        var rootPath = Normalize(selectedClientFolder) ?? throw new ValidationException("Client folder path is required.");
        if (!Directory.Exists(rootPath))
        {
            throw new ValidationException("Client folder path does not exist on the server.");
        }

        var client = await db.Clients.SingleOrDefaultAsync(client => client.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        client.ClientFolder = rootPath;
        client.UpdatedAtUtc = DateTime.UtcNow;

        var run = new ClientEvidenceScanRun
        {
            RootPath = rootPath,
            StartedBy = userName,
            Status = ClientEvidenceScanStatuses.Running
        };
        db.ClientEvidenceScanRuns.Add(run);
        await AddAuditAsync("Client", client.Id, "SetEvidenceClientFolder", new
        {
            client.Id,
            client.DisplayName,
            client.ClientFolder
        }, userName, reason);
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
            var clients = await db.Clients.AsNoTracking().ToListAsync(cancellationToken);
            var requirements = await LoadActiveRequirementsAsync(cancellationToken);
            var forcedClient = forcedClientId.HasValue
                ? clients.SingleOrDefault(client => client.Id == forcedClientId.Value) ?? throw new InvalidOperationException("Client not found.")
                : null;
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

                var match = forcedClient is not null
                    ? new ClientEvidenceMatchResult(forcedClient, 1, "Client-specific folder scan.")
                    : MatchClient(relativePath, clients);
                scanFile.CandidateCount = match.CandidateCount;
                scanFile.MatchReason = match.Reason;
                if (match.Client is null)
                {
                    scanFile.MatchStatus = match.CandidateCount > 1 ? ClientEvidenceScanFileStatuses.Ambiguous : ClientEvidenceScanFileStatuses.Unmatched;
                    if (scanFile.MatchStatus == ClientEvidenceScanFileStatuses.Ambiguous)
                    {
                        run.AmbiguousFiles++;
                    }
                    else
                    {
                        run.UnmatchedFiles++;
                    }

                    db.ClientEvidenceScanFiles.Add(scanFile);
                    await SaveScanProgressIfNeededAsync(run, cancellationToken);
                    continue;
                }

                scanFile.ClientId = match.Client.Id;
                scanFile.MatchStatus = ClientEvidenceScanFileStatuses.Linked;
                db.ClientEvidenceScanFiles.Add(scanFile);
                await ResolvePriorScanFilesForPathAsync(scanFile, match.Client.Id, cancellationToken);

                var evidenceType = scanFile.SuggestedEvidenceType ?? "General";
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
                        ScanFile = scanFile,
                        UpdatedBy = userName
                    });
                }

                run.LinkedFiles++;
                await SaveScanProgressIfNeededAsync(run, cancellationToken);
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
                ScanFile = scanFile,
                UpdatedBy = userName
            });
        }

        await AddAuditAsync("ClientEvidenceScanFile", scanFile.Id, "ResolveScanFile", new
        {
            scanFile.RelativePath,
            ClientId = client.Id,
            EvidenceType = normalizedType
        }, userName, reason);
        await db.SaveChangesAsync();
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
            Requirement("Identity", "Identity", "Identity and verification document", "Current identity, registration or trust instrument evidence.", 10, true, true),
            Requirement("Address", "Address", "Residential or operating address evidence", "Current address evidence or acceptable verification note.", 20, true, true),
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
            var matchedItems = items.Where(item => item.ClientId == clientId && (item.ClientEvidenceRequirementId == requirement.Id || item.EvidenceType == requirement.EvidenceType));
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
        if (ContainsAny(text, "beneficiary", "beneficiaries", "beneficial", "ownership", "director", "trustee")) return "BeneficialOwnership";
        if (ContainsAny(text, "trustdeed", "trustakte")) return "TrustDeed";
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

    private async Task VerifyEvidenceItemAsync(ClientEvidenceItem item, DateOnly? receivedDate, DateOnly? expiryDate, string? userName, string reason)
    {
        item.ReceivedDate = receivedDate;
        item.VerifiedDate = DateOnly.FromDateTime(DateTime.Today);
        item.ExpiryDate = expiryDate;
        item.Reviewer = userName;
        item.Status = ClientEvidenceStatuses.Verified;
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
                    file.MatchStatus == ClientEvidenceScanFileStatuses.Ambiguous))
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

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NonAlphaNumericSpaceRegex();

    private sealed record ClientEvidenceReadinessCounts(int RequiredCount, int CompleteCount, int ExceptionCount, int BlockedCount)
    {
        public bool IsReadyForRiskAssessment => BlockedCount == 0;
    }

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
    public string Status { get; set; } = "";

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
        Status = item.Status
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
