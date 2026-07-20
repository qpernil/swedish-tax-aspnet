using System.Globalization;
using System.Reflection;

namespace SwedishTax.Core;

internal enum TaxRowKind
{
    Amount,
    Percent,
}

internal sealed record TaxRow(
    uint Minimum,
    uint Maximum,
    uint[] Values,
    TaxRowKind Kind);

internal static class TaxTables
{
    private const string ResourceName =
        "SwedishTax.Core.Data.allmanna-tabeller-manad-2026.txt";
    private const int RecordCount = 7_966;
    private const int RecordLength = 49;

    private static readonly Lazy<IReadOnlyDictionary<byte, TaxRow[]>> LazyTables =
        new(LoadAndValidate);

    internal static IReadOnlyDictionary<byte, TaxRow[]> All => LazyTables.Value;

    internal static TaxRow[]? GetRows(byte table) =>
        All.TryGetValue(table, out var rows) ? rows : null;

    private static IReadOnlyDictionary<byte, TaxRow[]> LoadAndValidate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var tables = Enumerable.Range(TaxCalculator.MinTaxTable,
                TaxCalculator.MaxTaxTable - TaxCalculator.MinTaxTable + 1)
            .ToDictionary(table => checked((byte)table), _ => new List<TaxRow>());

        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.Length != RecordLength)
            {
                throw SourceError(lineNumber, $"expected {RecordLength} characters");
            }

            if (line[..2] != "30")
            {
                throw SourceError(lineNumber, "unsupported record type");
            }

            var kind = line[2] switch
            {
                'B' => TaxRowKind.Amount,
                '%' => TaxRowKind.Percent,
                _ => throw SourceError(lineNumber, $"unsupported row kind {line[2]}")
            };
            var table = checked((byte)Parse(line.AsSpan(3, 2), lineNumber, "table"));
            if (!tables.TryGetValue(table, out var rows))
            {
                throw SourceError(lineNumber, $"table {table} is outside the supported range");
            }

            var minimum = Parse(line.AsSpan(5, 7), lineNumber, "minimum");
            var maximumField = line.AsSpan(12, 7).Trim();
            var maximum = maximumField.IsEmpty
                ? uint.MaxValue
                : Parse(maximumField, lineNumber, "maximum");
            var values = new uint[6];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = Parse(
                    line.AsSpan(19 + index * 5, 5),
                    lineNumber,
                    "column value");
            }

            rows.Add(new TaxRow(minimum, maximum, values, kind));
        }

        if (lineNumber != RecordCount)
        {
            throw new InvalidOperationException(
                $"Tax table source has {lineNumber} records; expected {RecordCount}.");
        }

        foreach (var (table, rows) in tables)
        {
            if (rows.Count == 0 || rows[0].Minimum != 1)
            {
                throw new InvalidOperationException($"Table {table} does not start at income 1.");
            }

            if (rows[^1].Maximum != uint.MaxValue)
            {
                throw new InvalidOperationException($"Table {table} has no open-ended final row.");
            }

            for (var index = 1; index < rows.Count; index++)
            {
                if (rows[index - 1].Maximum + 1 != rows[index].Minimum)
                {
                    throw new InvalidOperationException(
                        $"Table {table} contains a gap or overlap at row {index + 1}.");
                }
            }
        }

        return tables.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static uint Parse(ReadOnlySpan<char> value, int lineNumber, string name)
    {
        if (!uint.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var result))
        {
            throw SourceError(lineNumber, $"invalid {name}");
        }

        return result;
    }

    private static InvalidOperationException SourceError(int lineNumber, string message) =>
        new($"Tax table source line {lineNumber}: {message}.");
}
