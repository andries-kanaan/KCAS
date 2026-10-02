using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class EmployeeTransferService(
    IDbContextFactory<ApplicationDbContext> factory, EmployeeEvidenceFiles files,
    IConfiguration configuration, IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string StorageRoot => Path.GetFullPath(configuration["EmployeeTransfers:StorageRoot"]
        ?? (Directory.Exists(@"D:\Deploy\KCAS\shared") ? @"D:\Deploy\KCAS\shared\employee-packages"
            : Path.Combine(environment.ContentRootPath, "..", "..", "backups", "employee-packages")));

    public async Task<EmployeeTransferExport> ExportAsync(IEnumerable<int> employeeIds, string passphrase, string reason, ClaimsPrincipal principal)
    {
        EmployeeTransferEncryption.ValidatePassphrase(passphrase);
        Require(reason, "An export reason");
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await AuthorizeAsync(db, principal);
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count is < 1 or > 100) throw new ValidationException("Select between 1 and 100 employees.");
        var package = new EmployeeTransferPackage { ExportedBy = actor.Email ?? actor.UserName ?? actor.Id, Reason = reason.Trim(),
            SourceSystemKey = Hash(Environment.MachineName + "\n" + db.Database.GetDbConnection().DataSource + "\n" + db.Database.GetDbConnection().Database)[..32] };
        foreach (var id in ids) package.Employees.Add(await SnapshotAsync(db, id));
        var peopleIds = ids.Concat(package.Employees.SelectMany(x => x.Decisions).Select(x => x.ReviewerEmployeeProfileId)).Distinct();
        foreach (var p in await db.EmployeeProfiles.Where(x => peopleIds.Contains(x.Id)).ToListAsync())
            package.People[p.Id] = new(p.TransferKey, p.LegalName, p.Email);
        var actorIds = package.Employees.SelectMany(ActorIds).Distinct().ToList();
        foreach (var user in (await db.Users.AsNoTracking().Select(x => new { x.Id, x.Email, x.UserName }).ToListAsync()).Where(x => actorIds.Contains(x.Id)))
            package.Actors[user.Id] = user.Email ?? user.UserName ?? user.Id;
        foreach (var record in await db.EmployeeTransferRecords.Where(x => x.Direction == "Incoming").ToListAsync())
            foreach (var (id, label) in JsonSerializer.Deserialize<Dictionary<string, string>>(record.ActorNamesJson, JsonOptions) ?? [])
                if (actorIds.Contains(id)) package.Actors.TryAdd(id, label);
        foreach (var id in actorIds) package.Actors.TryAdd(id, "Retained source account " + id);
        foreach (var (reference, hash) in EvidenceReferences(package).Distinct())
        {
            var path = files.Resolve(reference);
            if (!File.Exists(path) || new FileInfo(path).Length > 20 * 1024 * 1024) throw new ValidationException("Employee evidence is missing or exceeds the 20 MB file limit: " + reference);
            var content = await File.ReadAllBytesAsync(path);
            if (Hash(content) != hash) throw new ValidationException("Employee evidence changed; re-verify before exporting: " + reference);
            package.Files.Add(new(reference, hash, content));
        }
        Validate(package);
        var encrypted = EmployeeTransferEncryption.Encrypt(JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions), passphrase);
        var name = $"KCAS-employees-{DateTime.UtcNow:yyyyMMdd}-{package.PackageId[..12]}.kcas-employees";
        var pathOut = Path.Combine(StorageRoot, "outgoing", name);
        Directory.CreateDirectory(Path.GetDirectoryName(pathOut)!);
        await File.WriteAllBytesAsync(pathOut, encrypted);
        foreach (var item in package.Employees)
        {
            db.EmployeeTransferRecords.Add(new() { PackageId = package.PackageId, Direction = "Outgoing", EmployeeKey = item.Profile.TransferKey,
                SourceSystemKey = package.SourceSystemKey, EmployeeProfileId = item.Profile.Id, SourceDigest = Digest(item), StoragePath = pathOut,
                FileName = name, UserId = actor.Id, Reason = reason.Trim(), PackageCreatedAtUtc = package.CreatedAtUtc });
            Audit(db, item.Profile.Id, actor.Id, "EmployeeTransferExported", reason, new { package.PackageId, name });
        }
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return new(package.PackageId, name, ids.Count, encrypted.Length);
    }

    public async Task<EmployeeTransferPreview> PreviewAsync(byte[] encrypted, string passphrase, ClaimsPrincipal principal)
    {
        var package = ReadPackage(encrypted, passphrase);
        await using var db = await factory.CreateDbContextAsync();
        await AuthorizeAsync(db, principal);
        return await InspectAsync(db, package);
    }

    public async Task<int> ApplyAsync(byte[] encrypted, string passphrase, string previewStamp, string reason, ClaimsPrincipal principal)
    {
        Require(reason, "An import reason");
        var package = ReadPackage(encrypted, passphrase);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await AuthorizeAsync(db, principal);
        var preview = await InspectAsync(db, package);
        if (!preview.CanApply) throw new ValidationException(string.Join(" ", preview.Conflicts));
        if (preview.TargetStamp != previewStamp) throw new ValidationException("Live data or the package changed after preview. Preview again before applying.");
        var users = await MapActorsAsync(db, package);
        var people = new Dictionary<int, int>();
        var changed = new List<(EmployeeTransferSnapshot Source, EmployeeProfile Target, EmployeeTransferRecord? Previous)>();
        foreach (var source in package.Employees)
        {
            var target = await MatchAsync(db, new(source.Profile.TransferKey, source.Profile.LegalName, source.Profile.Email));
            var previous = target is null ? null : await PreviousAsync(db, target.Id, package.SourceSystemKey);
            if (target is not null && (await AppliedAsync(db, package.PackageId, source.Profile.TransferKey) || previous?.SourceDigest == Digest(source)))
            { people[source.Profile.Id] = target.Id; continue; }
            if (target is null) { target = Clone(source.Profile); target.Id = 0; db.EmployeeProfiles.Add(target); }
            else { var copy = Clone(source.Profile); copy.Id = target.Id; db.Entry(target).CurrentValues.SetValues(copy); }
            target.CreatedByUserId = MapUser(users, source.Profile.CreatedByUserId);
            await db.SaveChangesAsync();
            people[source.Profile.Id] = target.Id;
            changed.Add((source, target, previous));
        }
        foreach (var (sourceId, identity) in package.People)
            if (!people.ContainsKey(sourceId)) people[sourceId] = (await MatchAsync(db, identity))!.Id;
        var batches = new Dictionary<int, int>();
        foreach (var source in package.Employees.SelectMany(x => x.Batches).DistinctBy(x => x.Id))
        {
            var batch = await db.EmployeeTfsBatches.SingleOrDefaultAsync(x => x.SourceVersion == source.SourceVersion);
            if (batch is null) { batch = Clone(source); batch.Id = 0; batch.CreatedByUserId = MapUser(users, source.CreatedByUserId); db.EmployeeTfsBatches.Add(batch); await db.SaveChangesAsync(); }
            batches[source.Id] = batch.Id;
        }
        var importedPath = Path.Combine(StorageRoot, "incoming", package.PackageId + ".kcas-employees");
        if (changed.Count > 0) { Directory.CreateDirectory(Path.GetDirectoryName(importedPath)!); await File.WriteAllBytesAsync(importedPath, encrypted); }
        var actorLabels = package.Actors.GroupBy(x => MapUser(users, x.Key)).ToDictionary(x => x.Key, x => x.First().Value);
        foreach (var (source, target, previous) in changed)
        {
            var mapping = previous is null ? new EmployeeTransferMapping() : JsonSerializer.Deserialize<EmployeeTransferMapping>(previous.MappingJson, JsonOptions)!;
            foreach (var account in source.Accounts)
            {
                var userId = MapUser(users, account.UserId);
                if (!await db.Users.AnyAsync(x => x.Id == userId)) continue;
                if (!await db.EmployeeAccountLinks.AnyAsync(x => x.UserId == userId))
                { var link = Clone(account); link.Id = 0; link.EmployeeProfileId = target.Id; link.UserId = userId; link.LinkedByUserId = MapUser(users, account.LinkedByUserId); db.EmployeeAccountLinks.Add(link); }
            }
            foreach (var review in source.Reviews)
                await UpsertAsync(db, review, mapping.Reviews, r => { r.EmployeeProfileId = target.Id; r.PreparedByUserId = MapUser(users, r.PreparedByUserId); });
            foreach (var check in source.Checks.OrderBy(x => x.Id))
                await UpsertAsync(db, check, mapping.Checks, c => { c.EmployeeComplianceReviewId = mapping.Reviews[c.EmployeeComplianceReviewId];
                    c.RecordedByUserId = MapUser(users, c.RecordedByUserId); c.SupersedesCheckId = c.SupersedesCheckId.HasValue ? mapping.Checks[c.SupersedesCheckId.Value] : null;
                    c.EmployeeTfsBatchId = c.EmployeeTfsBatchId.HasValue ? batches[c.EmployeeTfsBatchId.Value] : null;
                    c.EvidencePath = c.EvidencePath is null ? null : Materialize(package, c.EvidencePath, c.EvidenceSha256!); });
            foreach (var access in source.Access)
                await UpsertAsync(db, access, mapping.Access, a => { a.EmployeeComplianceReviewId = mapping.Reviews[a.EmployeeComplianceReviewId]; a.RecordedByUserId = MapUser(users, a.RecordedByUserId); });
            foreach (var doc in source.Documents)
                await UpsertAsync(db, doc, mapping.Documents, d => { d.EmployeeProfileId = target.Id; d.LinkedByUserId = MapUser(users, d.LinkedByUserId); d.EvidencePath = Materialize(package, d.EvidencePath, d.EvidenceSha256); });
            foreach (var decision in source.Decisions)
                await UpsertAsync(db, decision, mapping.Decisions, d => { d.EmployeeComplianceReviewId = mapping.Reviews[d.EmployeeComplianceReviewId];
                    d.ReviewerEmployeeProfileId = people[d.ReviewerEmployeeProfileId]; d.ReviewerUserId = MapUser(users, d.ReviewerUserId); });
            foreach (var task in source.Tasks)
                await UpsertAsync(db, task, mapping.Tasks, t => { t.EmployeeProfileId = target.Id; t.TriggerKey = "import:" + package.SourceSystemKey + ":" + task.Id;
                    t.AcknowledgedByUserId = t.AcknowledgedByUserId is null ? null : MapUser(users, t.AcknowledgedByUserId);
                    t.RecipientUserIdsJson = JsonSerializer.Serialize((JsonSerializer.Deserialize<List<string>>(t.RecipientUserIdsJson) ?? []).Select(id => MapUser(users, id)), JsonOptions);
                    t.EmployeeTfsBatchId = t.EmployeeTfsBatchId.HasValue ? batches[t.EmployeeTfsBatchId.Value] : null; });
            foreach (var audit in source.Audit)
                await UpsertAsync(db, audit, mapping.Audit, a => { a.EmployeeProfileId = target.Id; a.UserId = MapUser(users, a.UserId); });
            // Preserve source approvals, but do not certify laptop permissions as live access.
            if (source.Reviews.Count > 0) target.Version = Guid.NewGuid().ToString("N");
            await QueueTargetTaskAsync(db, target.Id, package.PackageId, actor.Id,
                source.Reviews.Count == 0 ? "Imported employee baseline; actual review remains pending." : "Imported source review history retained. Verify live access in a fresh review; source approval is not live access confirmation.");
            Audit(db, target.Id, actor.Id, "EmployeeTransferApplied", reason, new { package.PackageId, package.ExportedBy, SourceIdentity = source.Profile.Id });
            mapping.ImmutableDigests = ImmutableRows(source);
            await db.SaveChangesAsync();
            db.EmployeeTransferRecords.Add(new() { PackageId = package.PackageId, EmployeeKey = source.Profile.TransferKey,
                SourceSystemKey = package.SourceSystemKey, EmployeeProfileId = target.Id, SourceDigest = Digest(source),
                LocalDigest = Digest(await SnapshotAsync(db, target.Id)), MappingJson = JsonSerializer.Serialize(mapping, JsonOptions),
                ActorNamesJson = JsonSerializer.Serialize(actorLabels, JsonOptions), StoragePath = importedPath, FileName = Path.GetFileName(importedPath),
                UserId = actor.Id, Reason = reason.Trim(), PackageCreatedAtUtc = package.CreatedAtUtc });
        }
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return changed.Count;
    }

    public async Task<(Stream Content, string FileName)?> DownloadAsync(string packageId, ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(packageId, out _)) return null;
        await using var db = await factory.CreateDbContextAsync();
        await AuthorizeAsync(db, principal);
        var record = await db.EmployeeTransferRecords.AsNoTracking().FirstOrDefaultAsync(x => x.PackageId == packageId && x.Direction == "Outgoing");
        if (record is null) return null;
        var path = Path.Combine(StorageRoot, "outgoing", Path.GetFileName(record.FileName));
        return File.Exists(path) ? (File.OpenRead(path), record.FileName) : null;
    }

    private async Task<EmployeeTransferPreview> InspectAsync(ApplicationDbContext db, EmployeeTransferPackage package)
    {
        var preview = new EmployeeTransferPreview { Package = package };
        var stamps = new List<string> { Hash(JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions)) };
        var users = await MapActorsAsync(db, package);
        stamps.Add(JsonSerializer.Serialize(users.OrderBy(x => x.Key), JsonOptions));
        foreach (var source in package.Employees)
        {
            var p = source.Profile;
            var target = await MatchAsync(db, new(p.TransferKey, p.LegalName, p.Email));
            var previous = target is null ? null : await PreviousAsync(db, target.Id, package.SourceSystemKey);
            var digest = Digest(source);
            var skip = target is not null && (await AppliedAsync(db, package.PackageId, p.TransferKey) || previous?.SourceDigest == digest);
            var action = skip ? "Already imported" : target is null ? "Create employee" : "Update transferred employee";
            if (target is not null)
            {
                var local = await SnapshotAsync(db, target.Id);
                stamps.Add(target.Id + ":" + Digest(local));
                if (!skip)
                {
                    if (previous is null) preview.Conflicts.Add($"{p.DisplayName}: a live employee already exists without this source lineage; automatic replacement is blocked.");
                    else
                    {
                        if (previous.LocalDigest != Digest(local)) preview.Conflicts.Add($"{p.DisplayName}: live employee work changed since the previous import; it will not be overwritten.");
                        if (package.CreatedAtUtc <= previous.PackageCreatedAtUtc) preview.Conflicts.Add($"{p.DisplayName}: this package is older than the previously applied source revision.");
                        var oldMapping = JsonSerializer.Deserialize<EmployeeTransferMapping>(previous.MappingJson, JsonOptions)!;
                        var incoming = ImmutableRows(source);
                        if (oldMapping.ImmutableDigests.Any(x => !incoming.TryGetValue(x.Key, out var hash) || hash != x.Value))
                            preview.Conflicts.Add($"{p.DisplayName}: previously imported evidence/check/decision history was removed or altered in the source.");
                        if (oldMapping.Reviews.Keys.Except(source.Reviews.Select(x => x.Id)).Any() || oldMapping.Tasks.Keys.Except(source.Tasks.Select(x => x.Id)).Any())
                            preview.Conflicts.Add($"{p.DisplayName}: transferred review/task history is missing from the new source revision.");
                    }
                }
            }
            else stamps.Add(p.TransferKey + ":new");
            foreach (var account in source.Accounts)
            {
                var id = MapUser(users, account.UserId);
                var link = await db.EmployeeAccountLinks.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id);
                if (link is not null && link.EmployeeProfileId != target?.Id) preview.Conflicts.Add($"{p.DisplayName}: matched login already belongs to a different live employee.");
                if (!await db.Users.AnyAsync(x => x.Id == id)) preview.Warnings.Add($"{p.DisplayName}: login '{package.Actors[account.UserId]}' is missing on live; it will not be created or granted permissions.");
            }
            foreach (var decision in source.Decisions)
            {
                var identity = package.People[decision.ReviewerEmployeeProfileId];
                if (!package.Employees.Any(x => x.Profile.TransferKey == identity.Key) && await MatchAsync(db, identity) is null)
                    preview.Conflicts.Add($"{p.DisplayName}: reviewer '{identity.LegalName}' is missing from live. Include that employee in the bundle or transfer them first.");
            }
            if (source.Reviews.Count > 0 && !skip) preview.Warnings.Add($"{p.DisplayName}: source checks and approvals are retained; live access must be verified before the imported review can govern new live grants.");
            preview.Rows.Add(new(p.DisplayName, action, source.Reviews.Count, source.Checks.Count, source.Documents.Count));
        }
        foreach (var batch in package.Employees.SelectMany(x => x.Batches).DistinctBy(x => x.SourceVersion))
        {
            var existing = await db.EmployeeTfsBatches.AsNoTracking().SingleOrDefaultAsync(x => x.SourceVersion == batch.SourceVersion);
            stamps.Add(batch.SourceVersion + ":" + (existing?.SourceUrl ?? "new"));
            if (existing is not null && existing.SourceUrl != batch.SourceUrl) preview.Conflicts.Add("The live TFS list/version has a different source: " + batch.SourceVersion);
        }
        foreach (var file in package.Files)
        {
            var path = files.Resolve(file.Reference);
            if (File.Exists(path) && await files.HashAsync(file.Reference) != file.Sha256) preview.Conflicts.Add("The live evidence file differs; it will not be replaced: " + file.Reference);
            else if (!File.Exists(path))
            {
                var staged = StagedReference(file.Reference, file.Sha256);
                if (File.Exists(files.Resolve(staged)) && await files.HashAsync(staged) != file.Sha256) preview.Conflicts.Add("Previously transferred evidence changed: " + file.Reference);
                preview.Warnings.Add("Evidence will be restored to restricted transfer storage: " + file.Reference);
            }
        }
        preview.TargetStamp = Hash(string.Join("\n", stamps));
        return preview;
    }

    private static async Task<EmployeeTransferSnapshot> SnapshotAsync(ApplicationDbContext db, int employeeId)
    {
        var snapshot = new EmployeeTransferSnapshot { Profile = await db.EmployeeProfiles.AsNoTracking().SingleAsync(x => x.Id == employeeId) };
        snapshot.Accounts = await db.EmployeeAccountLinks.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId).OrderBy(x => x.Id).ToListAsync();
        snapshot.Reviews = await db.EmployeeComplianceReviews.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId).OrderBy(x => x.Id).ToListAsync();
        var ids = snapshot.Reviews.Select(x => x.Id).ToList();
        snapshot.Checks = await db.EmployeeComplianceChecks.AsNoTracking().Where(x => ids.Contains(x.EmployeeComplianceReviewId)).OrderBy(x => x.Id).ToListAsync();
        snapshot.Access = await db.EmployeeAccessConfirmations.AsNoTracking().Where(x => ids.Contains(x.EmployeeComplianceReviewId)).OrderBy(x => x.Id).ToListAsync();
        snapshot.Decisions = await db.EmployeeReviewDecisions.AsNoTracking().Where(x => ids.Contains(x.EmployeeComplianceReviewId)).OrderBy(x => x.Id).ToListAsync();
        snapshot.Documents = await db.EmployeeEvidenceDocuments.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId).OrderBy(x => x.Id).ToListAsync();
        snapshot.Tasks = await db.EmployeeComplianceTasks.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId).OrderBy(x => x.Id).ToListAsync();
        var batchIds = snapshot.Tasks.Select(x => x.EmployeeTfsBatchId).Concat(snapshot.Checks.Select(x => x.EmployeeTfsBatchId)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        snapshot.Batches = await db.EmployeeTfsBatches.AsNoTracking().Where(x => batchIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
        snapshot.Audit = await db.EmployeeComplianceAuditEvents.AsNoTracking().Where(x => x.EmployeeProfileId == employeeId && !x.Action.StartsWith("EmployeeTransfer")).OrderBy(x => x.Id).ToListAsync();
        return snapshot;
    }

    private string Materialize(EmployeeTransferPackage package, string reference, string hash)
    {
        if (File.Exists(files.Resolve(reference)))
        {
            if (Hash(File.ReadAllBytes(files.Resolve(reference))) != hash) throw new ValidationException("Live evidence changed after preview: " + reference);
            return Path.GetRelativePath(files.Root, files.Resolve(reference)).Replace('\\', '/');
        }
        var file = package.Files.Single(x => x.Reference == reference && x.Sha256 == hash);
        var staged = StagedReference(reference, hash);
        var path = files.Resolve(staged);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(file.Content);
        }
        if (Hash(File.ReadAllBytes(path)) != hash) throw new ValidationException("Transferred evidence differs: " + reference);
        return staged;
    }

    private static string StagedReference(string reference, string hash)
    {
        var extension = Path.GetExtension(reference.Replace('\\', '/'));
        if (extension.Length > 12 || extension.Any(x => !char.IsAsciiLetterOrDigit(x) && x != '.')) extension = ".bin";
        return "KCAS employee transfers/Evidence/" + hash + extension;
    }
    private static async Task<EmployeeProfile?> MatchAsync(ApplicationDbContext db, EmployeeTransferIdentity identity)
    {
        var candidates = await db.EmployeeProfiles.Where(x => x.TransferKey == identity.Key || x.LegalName == identity.LegalName || identity.Email != null && x.Email == identity.Email).ToListAsync();
        if (candidates.Count > 1) throw new ValidationException("Employee identity cannot be matched uniquely on live: " + identity.LegalName);
        var candidate = candidates.SingleOrDefault();
        if (candidate is not null && candidate.TransferKey != identity.Key &&
            (!string.Equals(candidate.LegalName, identity.LegalName, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(candidate.Email, identity.Email, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Employee identity conflicts with the live profile: " + identity.LegalName);
        return candidate;
    }
    private static async Task<Dictionary<string, string>> MapActorsAsync(ApplicationDbContext db, EmployeeTransferPackage package)
    {
        var users = await db.Users.AsNoTracking().ToListAsync();
        var result = new Dictionary<string, string>();
        foreach (var (id, label) in package.Actors)
        {
            var matches = users.Where(x => string.Equals(x.Email, label, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1) throw new ValidationException("Account email cannot be matched uniquely on live: " + label);
            result[id] = matches.SingleOrDefault()?.Id ?? "external-" + Hash(package.SourceSystemKey + ":" + id)[..40];
        }
        return result;
    }
    private static IEnumerable<string> ActorIds(EmployeeTransferSnapshot s)
    {
        var records = s.Accounts.Cast<object>().Concat(s.Reviews).Concat(s.Checks).Concat(s.Access).Concat(s.Decisions).Concat(s.Documents).Concat(s.Tasks).Concat(s.Batches).Concat(s.Audit).Append(s.Profile);
        foreach (var record in records)
            foreach (var property in record.GetType().GetProperties().Where(p => p.Name.EndsWith("UserId", StringComparison.Ordinal)))
                if (property.GetValue(record) is string id && !string.IsNullOrWhiteSpace(id)) yield return id;
        foreach (var task in s.Tasks)
            foreach (var id in JsonSerializer.Deserialize<List<string>>(task.RecipientUserIdsJson) ?? []) yield return id;
    }
    private static string MapUser(Dictionary<string, string> users, string id) => users[id];
    private static async Task UpsertAsync<T>(ApplicationDbContext db, T source, Dictionary<int, int> mapping, Action<T> remap) where T : class
    {
        var idProperty = typeof(T).GetProperty("Id")!;
        var sourceId = (int)idProperty.GetValue(source)!;
        var copy = Clone(source); remap(copy);
        if (mapping.TryGetValue(sourceId, out var targetId))
        {
            idProperty.SetValue(copy, targetId);
            var target = await db.Set<T>().FindAsync(targetId) ?? throw new ValidationException("Transferred history is missing on live.");
            db.Entry(target).CurrentValues.SetValues(copy);
        }
        else
        {
            idProperty.SetValue(copy, 0); db.Set<T>().Add(copy); await db.SaveChangesAsync();
            mapping[sourceId] = (int)idProperty.GetValue(copy)!;
        }
    }
    private static async Task QueueTargetTaskAsync(ApplicationDbContext db, int employeeId, string packageId, string actor, string reason)
    {
        var recipients = await (from user in db.Users where user.IsApproved join role in db.UserRoles on user.Id equals role.UserId
            join claim in db.RoleClaims on role.RoleId equals claim.RoleId where claim.ClaimType == KcasClaimTypes.Permission &&
                (claim.ClaimValue == KcasPermissions.EmployeesManage || claim.ClaimValue == KcasPermissions.EmployeesReview) select user.Id).Distinct().ToListAsync();
        db.EmployeeComplianceTasks.Add(new() { EmployeeProfileId = employeeId, TriggerKey = "transfer:" + packageId, Kind = "TransferReview", Reason = reason,
            RecipientUserIdsJson = JsonSerializer.Serialize(recipients, JsonOptions) });
    }
    private static Task<EmployeeTransferRecord?> PreviousAsync(ApplicationDbContext db, int employeeId, string sourceSystem)
        => db.EmployeeTransferRecords.AsNoTracking().Where(x => x.Direction == "Incoming" && x.EmployeeProfileId == employeeId && x.SourceSystemKey == sourceSystem).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
    private static Task<bool> AppliedAsync(ApplicationDbContext db, string packageId, string key)
        => db.EmployeeTransferRecords.AnyAsync(x => x.Direction == "Incoming" && x.PackageId == packageId && x.EmployeeKey == key);
    private static async Task<ApplicationUser> AuthorizeAsync(ApplicationDbContext db, ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        var allowed = await (from assignment in db.UserRoles where assignment.UserId == id join role in db.Roles on assignment.RoleId equals role.Id where role.Name == KcasRoles.Administrator select role).AnyAsync();
        if (principal.Identity?.IsAuthenticated != true || user?.IsApproved != true || !allowed) throw new UnauthorizedAccessException("Current approved Administrator access is required for personnel transfers.");
        return user;
    }
    private static EmployeeTransferPackage ReadPackage(byte[] encrypted, string passphrase)
    {
        try
        {
            var p = JsonSerializer.Deserialize<EmployeeTransferPackage>(EmployeeTransferEncryption.Decrypt(encrypted, passphrase), JsonOptions)
                ?? throw new ValidationException("Empty employee package.");
            Validate(p); return p;
        }
        catch (JsonException) { throw new ValidationException("Invalid employee package data."); }
        catch (NullReferenceException) { throw new ValidationException("Incomplete employee package data."); }
    }
    private static void Validate(EmployeeTransferPackage p)
    {
        if (p.FormatVersion != 1 || !Guid.TryParse(p.PackageId, out _) || p.SourceSystemKey.Length != 32 ||
            p.CreatedAtUtc > DateTime.UtcNow.AddMinutes(1) || p.Employees.Count is < 1 or > 100 ||
            p.Employees.Select(x => x.Profile.TransferKey).Distinct().Count() != p.Employees.Count)
            throw new ValidationException("Unsupported or incomplete employee package.");
        foreach (var s in p.Employees)
        {
            if (!Guid.TryParse(s.Profile.TransferKey, out _) || !p.People.ContainsKey(s.Profile.Id)) throw new ValidationException("Missing employee identity.");
            foreach (var entity in s.Accounts.Cast<object>().Concat(s.Reviews).Concat(s.Checks).Concat(s.Access).Concat(s.Decisions).Concat(s.Documents).Concat(s.Tasks).Concat(s.Batches).Concat(s.Audit).Append(s.Profile))
            {
                Validator.ValidateObject(entity, new ValidationContext(entity), true);
                if ((int)entity.GetType().GetProperty("Id")!.GetValue(entity)! <= 0) throw new ValidationException("Invalid source record identifier.");
                var owner = entity.GetType().GetProperty("EmployeeProfileId");
                if (owner is not null && (int)owner.GetValue(entity)! != s.Profile.Id) throw new ValidationException("A record belongs to a different employee.");
            }
            foreach (var collection in new[] { s.Reviews.Select(x => x.Id), s.Checks.Select(x => x.Id), s.Documents.Select(x => x.Id), s.Access.Select(x => x.Id), s.Decisions.Select(x => x.Id), s.Tasks.Select(x => x.Id), s.Audit.Select(x => x.Id) })
                if (collection.Count() != collection.Distinct().Count()) throw new ValidationException("Duplicate employee history records.");
            var reviews = s.Reviews.Select(x => x.Id).ToHashSet();
            if (s.Checks.Any(x => !reviews.Contains(x.EmployeeComplianceReviewId) || x.SupersedesCheckId.HasValue && !s.Checks.Any(c => c.Id == x.SupersedesCheckId && c.Id < x.Id)) ||
                s.Access.Any(x => !reviews.Contains(x.EmployeeComplianceReviewId)) || s.Decisions.Any(x => !reviews.Contains(x.EmployeeComplianceReviewId) || !p.People.ContainsKey(x.ReviewerEmployeeProfileId)))
                throw new ValidationException("Incomplete review/check/reviewer history.");
            if (s.Tasks.Any(x => x.EmployeeTfsBatchId.HasValue && !s.Batches.Any(b => b.Id == x.EmployeeTfsBatchId)) || s.Checks.Any(x => x.EmployeeTfsBatchId.HasValue && !s.Batches.Any(b => b.Id == x.EmployeeTfsBatchId)))
                throw new ValidationException("Missing TFS batch history.");
            if (ActorIds(s).Any(id => !p.Actors.ContainsKey(id))) throw new ValidationException("Missing source performer/account provenance.");
            foreach (var r in s.Reviews.Where(x => x.Status == "Approved"))
            {
                var d = s.Decisions.SingleOrDefault(x => x.EmployeeComplianceReviewId == r.Id);
                if (d?.Decision != "Approved" || d.ReviewerEmployeeProfileId == s.Profile.Id || !r.CompletedAtUtc.HasValue || d.ApprovedReviewMonths is null or < 1 or > 60 ||
                    r.NextReviewDate != DateOnly.FromDateTime(r.CompletedAtUtc.Value.ToLocalTime()).AddMonths(d.ApprovedReviewMonths.Value))
                    throw new ValidationException("An approved source review has incomplete or inconsistent approval evidence.");
            }
        }
        if (p.Files.GroupBy(x => x.Reference).Any(x => x.Count() > 1)) throw new ValidationException("Duplicate evidence paths.");
        foreach (var f in p.Files)
            if (f.Content.Length > 20 * 1024 * 1024 || f.Sha256.Length != 64 || Hash(f.Content) != f.Sha256) throw new ValidationException("Invalid employee evidence content/hash.");
        foreach (var (path, hash) in EvidenceReferences(p))
            if (!p.Files.Any(x => x.Reference == path && x.Sha256 == hash)) throw new ValidationException("Evidence referenced by a check or document is missing from the package.");
    }
    private static IEnumerable<(string Reference, string Hash)> EvidenceReferences(EmployeeTransferPackage p)
        => p.Employees.SelectMany(s => s.Documents.Select(x => (x.EvidencePath, x.EvidenceSha256)).Concat(s.Checks.Where(x => x.EvidencePath is not null).Select(x => (x.EvidencePath!, x.EvidenceSha256!))));
    private static Dictionary<string, string> ImmutableRows(EmployeeTransferSnapshot s)
        => s.Checks.Cast<object>().Concat(s.Access).Concat(s.Decisions).Concat(s.Documents).Concat(s.Audit).ToDictionary(
            x => x.GetType().Name + ":" + x.GetType().GetProperty("Id")!.GetValue(x), x => Hash(JsonSerializer.SerializeToUtf8Bytes(x, JsonOptions)));
    private static T Clone<T>(T item) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions), JsonOptions)!;
    private static string Digest(EmployeeTransferSnapshot s) => Hash(JsonSerializer.SerializeToUtf8Bytes(s, JsonOptions));
    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static void Require(string value, string label) { if (string.IsNullOrWhiteSpace(value) || value.Length > 20000) throw new ValidationException(label + " is required (maximum 20000 characters)."); }
    private static void Audit(ApplicationDbContext db, int employeeId, string user, string action, string reason, object data)
        => db.EmployeeComplianceAuditEvents.Add(new() { EmployeeProfileId = employeeId, UserId = user, Action = action, Reason = reason.Trim(), SnapshotJson = JsonSerializer.Serialize(data, JsonOptions) });
}
