using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientCodeGenerator(ApplicationDbContext db)
{
    private const string Prefix = "KCAS-";

    public async Task<string> GenerateAsync()
    {
        var existingCodes = await db.Clients
            .AsNoTracking()
            .Where(client => client.KanaanId != null && client.KanaanId.StartsWith(Prefix))
            .Select(client => client.KanaanId!)
            .ToListAsync();

        var nextNumber = existingCodes
            .Select(ParseNumber)
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return Prefix + nextNumber.ToString("000000", CultureInfo.InvariantCulture);
    }

    private static int? ParseNumber(string code)
    {
        if (!code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(code[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }
}
