using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static class LegacyImportReconciler
{
    public static LegacyImportRowState Compare(
        long runId,
        string sourceTable,
        long sourceId,
        string incomingPayloadJson,
        string? baselinePayloadJson,
        string? targetEntityType = null,
        long? targetEntityId = null,
        DateTime? sourceUpdatedAt = null)
    {
        var incoming = CanonicalizePayload(incomingPayloadJson);
        var baseline = string.IsNullOrWhiteSpace(baselinePayloadJson)
            ? null
            : CanonicalizePayload(baselinePayloadJson);

        var row = new LegacyImportRowState
        {
            LegacyImportRunId = runId,
            SourceTable = sourceTable,
            SourceId = sourceId,
            IncomingPayloadJson = incoming,
            BaselinePayloadJson = baseline,
            IncomingFingerprint = Fingerprint(incoming),
            BaselineFingerprint = baseline is null ? null : Fingerprint(baseline),
            TargetEntityType = targetEntityType,
            TargetEntityId = targetEntityId,
            SourceUpdatedAt = sourceUpdatedAt
        };

        if (baseline is null)
        {
            row.Classification = LegacyImportClassifications.New;
            row.ApplyStatus = LegacyImportApplyStatuses.PendingReview;
            return row;
        }

        if (row.IncomingFingerprint == row.BaselineFingerprint)
        {
            row.Classification = LegacyImportClassifications.Unchanged;
            row.ApplyStatus = LegacyImportApplyStatuses.NotApplicable;
            return row;
        }

        row.Classification = LegacyImportClassifications.Changed;
        row.ApplyStatus = LegacyImportApplyStatuses.PendingReview;
        foreach (var difference in Differences(baseline, incoming))
        {
            row.Differences.Add(difference);
        }

        return row;
    }

    public static LegacyImportRowState Missing(
        long runId,
        string sourceTable,
        long sourceId,
        string baselinePayloadJson,
        string? targetEntityType = null,
        long? targetEntityId = null)
    {
        var baseline = CanonicalizePayload(baselinePayloadJson);
        return new LegacyImportRowState
        {
            LegacyImportRunId = runId,
            SourceTable = sourceTable,
            SourceId = sourceId,
            Classification = LegacyImportClassifications.MissingFromSource,
            ApplyStatus = LegacyImportApplyStatuses.PendingReview,
            IncomingPayloadJson = "{}",
            BaselinePayloadJson = baseline,
            IncomingFingerprint = Fingerprint("{}"),
            BaselineFingerprint = Fingerprint(baseline),
            TargetEntityType = targetEntityType,
            TargetEntityId = targetEntityId
        };
    }

    public static string CanonicalizePayload(string payloadJson)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Fingerprint(string canonicalPayloadJson)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayloadJson)));

    private static IEnumerable<LegacyImportDifference> Differences(string baselineJson, string incomingJson)
    {
        using var baselineDocument = JsonDocument.Parse(baselineJson);
        using var incomingDocument = JsonDocument.Parse(incomingJson);

        var baseline = Flatten(baselineDocument.RootElement);
        var incoming = Flatten(incomingDocument.RootElement);
        foreach (var field in baseline.Keys.Union(incoming.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            baseline.TryGetValue(field, out var previous);
            incoming.TryGetValue(field, out var current);
            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                continue;
            }

            yield return new LegacyImportDifference
            {
                FieldName = field,
                BaselineValue = previous,
                IncomingValue = current
            };
        }
    }

    private static SortedDictionary<string, string?> Flatten(JsonElement element)
    {
        var result = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        FlattenInto(result, string.Empty, element);
        return result;
    }

    private static void FlattenInto(IDictionary<string, string?> result, string path, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                FlattenInto(result, string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}", property.Value);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FlattenInto(result, $"{path}[{index++}]", item);
            }

            return;
        }

        result[path] = element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString()?.Trim(),
            _ => element.GetRawText()
        };
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString()?.Trim());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
