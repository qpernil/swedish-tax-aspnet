using SwedishTax.Core;

namespace SwedishTax.Core.Tests;

public sealed class TaxCalculatorTests
{
    private static readonly TaxColumn[] Columns = Enum.GetValues<TaxColumn>();
    private static readonly uint[] AnnualFormulaGrossBreakpoints =
    [
        25_042, 53_872, 58_608, 65_712, 103_600, 116_328, 161_024, 184_112, 191_808,
        296_000, 310_208, 466_496, 478_336, 660_672, 673_038, 760_128,
    ];
    private static readonly uint[] TaxableIncomeBreakpoints = [40_000, 118_400, 240_000, 643_200];

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
    public void MarginalRateUsesAnnualFormulaAtAllIncomes()
    {
        var expected = (38_894 - 35_889) * 100.0 / 12_000;
        Assert.Equal(
            expected,
            TaxCalculator.CalculateMarginalRate(34, TaxColumn.Column1, 18_000));
        Assert.Null(TaxCalculator.CalculateMarginalRate(29, TaxColumn.Column1, uint.MaxValue));
        Assert.Null(TaxCalculator.CalculateMarginalRate(28, TaxColumn.Column1, 18_000));
    }

    [Fact]
    public void MarginalRateCoversEveryAnnualFormulaRangeTransition()
    {
        for (var table = TaxCalculator.MinTaxTable; table <= TaxCalculator.MaxTaxTable; table++)
        {
            foreach (var column in Columns)
            {
                foreach (var breakpoint in AnnualFormulaGrossBreakpoints)
                {
                    AssertFormulaTransition(table, column, breakpoint);
                }

                foreach (var taxableBreakpoint in TaxableIncomeBreakpoints)
                {
                    AssertFormulaTransition(
                        table,
                        column,
                        FindGrossIncomeForTaxableBreakpoint(table, column, taxableBreakpoint));
                }
            }
        }
    }

    private static uint FindGrossIncomeForTaxableBreakpoint(
        byte table,
        TaxColumn column,
        uint taxableBreakpoint)
    {
        for (uint grossIncome = 0; grossIncome <= 1_000_000; grossIncome += 100)
        {
            var tax = TaxCalculator.CalculateAnnualTax(table, column, grossIncome);
            if (tax is not null && tax.TaxableIncome >= taxableBreakpoint)
            {
                return grossIncome;
            }
        }

        throw new InvalidOperationException($"Taxable breakpoint {taxableBreakpoint} was not reached.");
    }

    private static void AssertFormulaTransition(byte table, TaxColumn column, uint breakpoint)
    {
        var transition = (breakpoint + 99) / 100 * 100;
        var before = TaxCalculator.CalculateAnnualTax(table, column, transition - 100)!;
        var at = TaxCalculator.CalculateAnnualTax(table, column, transition)!;
        var after = TaxCalculator.CalculateAnnualTax(table, column, transition + 100)!;
        Assert.True(before.Total <= before.AssessedIncome);
        Assert.True(at.Total <= at.AssessedIncome);
        Assert.True(after.Total <= after.AssessedIncome);

        var monthlyIncome = (transition > 6_000 ? transition - 6_000 : 0) / 12;
        var lowerAnnualIncome = monthlyIncome * 12;
        var upperAnnualIncome = (monthlyIncome + 1_000) * 12;
        Assert.True(lowerAnnualIncome <= transition && transition <= upperAnnualIncome);
        var lowerTax = TaxCalculator.CalculateAnnualTax(table, column, lowerAnnualIncome)!;
        var upperTax = TaxCalculator.CalculateAnnualTax(table, column, upperAnnualIncome)!;
        var expected = ((long)upperTax.Total - lowerTax.Total) * 100.0
            / (upperAnnualIncome - lowerAnnualIncome);
        var actual = TaxCalculator.CalculateMarginalRate(table, column, monthlyIncome);
        Assert.Equal(expected, actual);
        Assert.InRange(actual!.Value, 0.0, 100.0);
    }
}
