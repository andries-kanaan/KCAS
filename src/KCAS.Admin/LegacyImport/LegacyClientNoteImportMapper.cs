using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static partial class LegacyClientNoteImportMapper
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false
    };

    public static ClientNote Map(IReadOnlyDictionary<string, string?> row, int clientId, DateTime importedAtUtc)
    {
        var legacyId = ReadInt(row, "id") ?? throw new InvalidOperationException("Legacy client note row is missing a numeric id.");

        return new ClientNote
        {
            ClientId = clientId,
            LegacyClientNoteId = legacyId,
            NoteDate = ReadDateOnly(row, "note_date"),
            Title = Read(row, "note_title"),
            Details = StripTags(Read(row, "note_details")),
            IsDeleted = string.Equals(Read(row, "del"), "y", StringComparison.OrdinalIgnoreCase),
            IsFinal = ReadBool(row, "final") ?? true,
            OpenedBy = Read(row, "opened_by"),
            UpdatedBy = Read(row, "updated_by"),
            LegacyOpenedByUserId = ReadInt(row, "opened_by_id"),
            LegacyUpdatedByUserId = ReadInt(row, "updated_by_id"),
            LegacyOpenedAt = ReadDateTime(row, "date_opened"),
            LegacyUpdatedAt = ReadDateTime(row, "date_updated"),
            PayloadJson = JsonSerializer.Serialize(
                new SortedDictionary<string, string?>(row.ToDictionary(item => item.Key, item => item.Value), StringComparer.OrdinalIgnoreCase),
                SnapshotJsonOptions),
            ImportedAtUtc = importedAtUtc
        };
    }

    public static void ApplyUpdatedValues(ClientNote target, ClientNote source)
    {
        target.NoteDate = source.NoteDate;
        target.Title = source.Title;
        target.Details = source.Details;
        target.IsDeleted = source.IsDeleted;
        target.IsFinal = source.IsFinal;
        target.OpenedBy = source.OpenedBy;
        target.UpdatedBy = source.UpdatedBy;
        target.LegacyOpenedByUserId = source.LegacyOpenedByUserId;
        target.LegacyUpdatedByUserId = source.LegacyUpdatedByUserId;
        target.LegacyOpenedAt = source.LegacyOpenedAt;
        target.LegacyUpdatedAt = source.LegacyUpdatedAt;
        target.PayloadJson = source.PayloadJson;
        target.ImportedAtUtc = source.ImportedAtUtc;
    }

    private static string? Read(IReadOnlyDictionary<string, string?> row, string name)
    {
        return row.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, string?> row, string name)
    {
        return Read(row, name)?.ToUpperInvariant() switch
        {
            "Y" or "YES" or "TRUE" or "1" => true,
            "N" or "NO" or "FALSE" or "0" => false,
            _ => null
        };
    }

    private static DateOnly? ReadDateOnly(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return parsedDate;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedDateTime)
            ? DateOnly.FromDateTime(parsedDateTime)
            : null;
    }

    private static DateTime? ReadDateTime(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static string? StripTags(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : HtmlTagRegex().Replace(value, string.Empty).Trim();
    }

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();
}
