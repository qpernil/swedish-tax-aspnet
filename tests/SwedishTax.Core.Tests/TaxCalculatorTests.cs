using SwedishTax.Core;

namespace SwedishTax.Core.Tests;

public sealed class TaxCalculatorTests
{
    private static readonly TaxColumn[] Columns = Enum.GetValues<TaxColumn>();

    [Fact]
    public void TablesCoverEveryPositiveIncomeWithoutGaps()
    {
        Assert.Equal(7_966, TaxTables.All.Values.Sum(rows => rows.Length));

        for (var table = TaxCalculator.MinTaxTable; table <= TaxCalculator.MaxTaxTable; table++)
        {
            var rows = Assert.IsType<TaxRow[]>(TaxTables.GetRows(table));
            Assert.Equal(1U, rows[0].Minimum);
            Assert.Equal(uint.MaxValue, rows[^1].Maximum);

            for (var index = 1; index < rows.Length; index++)
            {
                Assert.Equal(rows[index - 1].Maximum + 1, rows[index].Minimum);
            }

            foreach (var row in rows)
            {
                for (var index = 0; index < Columns.Length; index++)
                {
                    var expected = row.Kind == TaxRowKind.Amount
                        ? TaxDeduction.Amount(row.Values[index])
                        : TaxDeduction.Percent(row.Values[index]);
                    Assert.Equal(
                        expected,
                        TaxCalculator.GetMonthlyDeduction(table, Columns[index], row.Minimum));
                    Assert.Equal(
                        expected,
                        TaxCalculator.GetMonthlyDeduction(table, Columns[index], row.Maximum));
                }
            }
        }
    }

    [Fact]
    public void OfficialBoundaryValuesMatchTheSourceFile()
    {
        Assert.Equal(TaxDeduction.Amount(2),
            TaxCalculator.GetMonthlyDeduction(29, TaxColumn.Column6, 2_001));
        Assert.Equal(TaxDeduction.Percent(48),
            TaxCalculator.GetMonthlyDeduction(29, TaxColumn.Column1, uint.MaxValue));
        Assert.Equal(TaxDeduction.Amount(25_944),
            TaxCalculator.GetMonthlyDeduction(32, TaxColumn.Column1, 80_000));
        Assert.Equal(TaxDeduction.Percent(32),
            TaxCalculator.GetMonthlyDeduction(32, TaxColumn.Column1, 80_001));
        Assert.Equal(TaxDeduction.Amount(23_386),
            TaxCalculator.GetMonthlyDeduction(33, TaxColumn.Column4, 80_000));
        Assert.Equal(TaxDeduction.Percent(39),
            TaxCalculator.GetMonthlyDeduction(33, TaxColumn.Column6, 80_001));
        Assert.Equal(TaxDeduction.Amount(24_065),
            TaxCalculator.GetMonthlyDeduction(34, TaxColumn.Column3, 80_000));
        Assert.Equal(TaxDeduction.Percent(45),
            TaxCalculator.GetMonthlyDeduction(34, TaxColumn.Column4, uint.MaxValue));
        Assert.Equal(TaxDeduction.Amount(3),
            TaxCalculator.GetMonthlyDeduction(42, TaxColumn.Column6, 2_001));
        Assert.Equal(TaxDeduction.Percent(51),
            TaxCalculator.GetMonthlyDeduction(42, TaxColumn.Column4, uint.MaxValue));
    }

    [Fact]
    public void AnnualFormulaMatchesSkv433WorkedExamples()
    {
        Assert.Equal(
            new AnnualTax(
                216_000, 42_400, 173_600, 0, 57_010, 2_013, 15_100, 15_100,
                23_316, 0, 1_002, 1_184, 35_889),
            TaxCalculator.CalculateAnnualTax(34, TaxColumn.Column1, 216_000));

        Assert.Equal(
            new AnnualTax(
                31_200, 25_100, 6_100, 0, 2_003, 70, 2_200, 2_003,
                0, 0, 0, 61, 2_331),
            TaxCalculator.CalculateAnnualTax(34, TaxColumn.Column1, 31_200));

        Assert.Equal(
            new AnnualTax(
                1_020_000, 17_400, 1_002_600, 71_920, 329_253, 11_630,
                47_100, 47_100, 53_134, 0, 1_500, 1_184, 359_353),
            TaxCalculator.CalculateAnnualTax(34, TaxColumn.Column1, 1_020_000));
    }

    [Fact]
    public void AnnualizedFormulaMatchesEveryMonthlyAmountEntry()
    {
        for (var table = TaxCalculator.MinTaxTable; table <= TaxCalculator.MaxTaxTable; table++)
        {
            foreach (var row in TaxTables.GetRows(table)!)
            {
                if (row.Kind != TaxRowKind.Amount)
                {
                    continue;
                }

                var annualIncome = row.Maximum * 12;
                for (var index = 0; index < Columns.Length; index++)
                {
                    var annual = TaxCalculator.CalculateAnnualTax(table, Columns[index], annualIncome);
                    Assert.NotNull(annual);
                    Assert.True(
                        annual.Total / 12 == row.Values[index],
                        $"Table {table}, column {index + 1}, bracket {row.Minimum}..{row.Maximum}.");
                }
            }
        }
    }

    [Fact]
    public void AnnualizedFormulaMatchesEveryMonthlyPercentageEntry()
    {
        for (var table = TaxCalculator.MinTaxTable; table <= TaxCalculator.MaxTaxTable; table++)
        {
            foreach (var row in TaxTables.GetRows(table)!)
            {
                if (row.Kind != TaxRowKind.Percent)
                {
                    continue;
                }

                var firstBracketMaximum = (row.Minimum + 199) / 200 * 200;
                var monthlyIncomes = row.Maximum == uint.MaxValue
                    ? new[] { firstBracketMaximum }
                    : new[] { firstBracketMaximum, row.Maximum };
                foreach (var monthlyIncome in monthlyIncomes)
                {
                    var annualIncome = monthlyIncome * 12;
                    for (var index = 0; index < Columns.Length; index++)
                    {
                        var annual = TaxCalculator.CalculateAnnualTax(
                            table,
                            Columns[index],
                            annualIncome);
                        Assert.NotNull(annual);
                        var denominator = (ulong)annualIncome;
                        var calculated = (ulong)annual.Total * 100;
                        var published = row.Values[index] * denominator;
                        var difference = calculated > published
                            ? calculated - published
                            : published - calculated;
                        Assert.True(
                            difference * 1_000 <= denominator * 501,
                            $"Table {table}, column {index + 1}, income {monthlyIncome}.");
                    }
                }
            }
        }
    }

    [Fact]
    public void UnsupportedInputsReturnNullAndZeroIncomeHasZeroTax()
    {
        Assert.Null(TaxCalculator.GetMonthlyDeduction(28, TaxColumn.Column1, 50_000));
        Assert.Null(TaxCalculator.GetMonthlyDeduction(43, TaxColumn.Column1, 50_000));
        Assert.Null(TaxCalculator.GetMonthlyDeduction(32, (TaxColumn)0, 50_000));
        Assert.Equal(
            TaxDeduction.Amount(0),
            TaxCalculator.GetMonthlyDeduction(32, TaxColumn.Column1, 0));
        Assert.Null(TaxCalculator.CalculateAnnualTax(28, TaxColumn.Column1, 50_000));
        Assert.Null(TaxCalculator.CalculateAnnualTax(43, TaxColumn.Column1, 50_000));
        Assert.Equal(0U, TaxCalculator.CalculateAnnualTax(32, TaxColumn.Column1, 0)!.Total);
    }

    [Fact]
    public void MarginalRateUsesOfficialTableWithholding()
    {
        Assert.Equal(
            25.1,
            TaxCalculator.CalculateMarginalRate(34, TaxColumn.Column1, 18_000));
        Assert.Equal(
            48.0,
            TaxCalculator.CalculateMarginalRate(29, TaxColumn.Column1, uint.MaxValue));
        Assert.Null(TaxCalculator.CalculateMarginalRate(28, TaxColumn.Column1, 18_000));
    }
}
