using System.Text.Json;
using KCAS.Admin.LegacyImport;

namespace KCAS.Admin.Tests;

public sealed class LegacyClientNoteImportMapperTests
{
    [Fact]
    public void Map_preserves_note_status_audit_and_snapshot()
    {
        var note = LegacyClientNoteImportMapper.Map(SampleRow(), clientId: 10, new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(10, note.ClientId);
        Assert.Equal(88, note.LegacyClientNoteId);
        Assert.Equal(new DateOnly(2026, 5, 30), note.NoteDate);
        Assert.Equal("Review meeting", note.Title);
        Assert.Equal("Line 1Line 2", note.Details);
        Assert.False(note.IsFinal);
        Assert.True(note.IsDeleted);
        Assert.Equal("legacy user", note.OpenedBy);
        Assert.Equal(7, note.LegacyOpenedByUserId);

        using var document = JsonDocument.Parse(note.PayloadJson);
        Assert.Equal("Review meeting", document.RootElement.GetProperty("note_title").GetString());
    }

    [Fact]
    public void ApplyUpdatedValues_replaces_imported_note_fields()
    {
        var target = LegacyClientNoteImportMapper.Map(SampleRow(), clientId: 10, DateTime.UtcNow);
        var row = SampleRow();
        row["note_title"] = "Updated";
        row["note_details"] = "Updated details";
        row["final"] = "1";
        row["del"] = "n";
        var source = LegacyClientNoteImportMapper.Map(row, clientId: 10, DateTime.UtcNow);

        LegacyClientNoteImportMapper.ApplyUpdatedValues(target, source);

        Assert.Equal("Updated", target.Title);
        Assert.Equal("Updated details", target.Details);
        Assert.True(target.IsFinal);
        Assert.False(target.IsDeleted);
    }

    private static Dictionary<string, string?> SampleRow()
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "88",
            ["note_date"] = "2026-05-30",
            ["note_title"] = "Review meeting",
            ["note_details"] = "<p>Line 1</p><strong>Line 2</strong>",
            ["client_id"] = "42",
            ["del"] = "y",
            ["final"] = "0",
            ["opened_by"] = "legacy user",
            ["updated_by"] = "legacy updater",
            ["opened_by_id"] = "7",
            ["updated_by_id"] = "8",
            ["date_opened"] = "2026-05-30 09:00:00",
            ["date_updated"] = "2026-05-31 10:00:00"
        };
    }
}
