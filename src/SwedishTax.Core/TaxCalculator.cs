using System.Numerics;

namespace SwedishTax.Core;

public static class TaxCalculator
{
    public const byte MinTaxTable = 29;
    public const byte MaxTaxTable = 42;

    private const uint PriceBaseAmount = 59_200;
    private const uint StateTaxThreshold = 643_000;
    private const uint BurialAndReligiousRate = 116;
    private const uint PublicServiceFeeMaximum = 1_184;
    private const uint MarginalIncomeInterval = 1_000;
    private static readonly BigInteger Scale = 100_000_000;

    public static TaxDeduction? GetMonthlyDeduction(
        byte table,
        TaxColumn column,
        uint grossMonthlyIncome)
    {
        var rows = TaxTables.GetRows(table);
        var columnIndex = ColumnIndex(column);
        if (rows is null || columnIndex < 0)
        {
            return null;
        }

        if (grossMonthlyIncome == 0)
        {
            return TaxDeduction.Amount(0);
        }

        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var row = rows[middle];
            if (grossMonthlyIncome < row.Minimum)
            {
                high = middle - 1;
            }
            else if (grossMonthlyIncome > row.Maximum)
            {
                low = middle + 1;
            }
            else
            {
                var value = row.Values[columnIndex];
                return row.Kind == TaxRowKind.Amount
                    ? TaxDeduction.Amount(value)
                    : TaxDeduction.Percent(value);
            }
        }

        return null;
    }

    public static AnnualTax? CalculateAnnualTax(
        byte table,
        TaxColumn column,
        uint grossYearlyIncome)
    {
        var bases = column switch
        {
            TaxColumn.Column1 => new AnnualTaxBases(
                grossYearlyIncome, grossYearlyIncome, grossYearlyIncome, false,
                WorkCreditKind.Under66, false),
            TaxColumn.Column2 => new AnnualTaxBases(
                grossYearlyIncome, 0, 0, true, WorkCreditKind.None, false),
            TaxColumn.Column3 => new AnnualTaxBases(
                grossYearlyIncome, grossYearlyIncome, grossYearlyIncome, true,
                WorkCreditKind.Over66, false),
            TaxColumn.Column4 => new AnnualTaxBases(
                grossYearlyIncome, 0, 0, false, WorkCreditKind.None, true),
            TaxColumn.Column5 => new AnnualTaxBases(
                grossYearlyIncome, grossYearlyIncome, 0, false, WorkCreditKind.None, false),
            TaxColumn.Column6 => new AnnualTaxBases(
                grossYearlyIncome, 0, 0, false, WorkCreditKind.None, false),
            _ => default,
        };
        return ColumnIndex(column) < 0 ? null : CalculateAnnualTax(table, bases);
    }

    /// <summary>Calculates annual tax for a mixture of salary and pension income.</summary>
    public static AnnualTax? CalculateAnnualTax(
        byte table,
        TaxAgeGroup ageGroup,
        AnnualIncomeProfile profile)
    {
        var workCredit = ageGroup switch
        {
            TaxAgeGroup.Under66AtYearStart => WorkCreditKind.Under66,
            TaxAgeGroup.AtLeast66AtYearStart => WorkCreditKind.Over66,
            _ => WorkCreditKind.Invalid,
        };
        if (workCredit == WorkCreditKind.Invalid)
        {
            return null;
        }

        return CalculateAnnualTax(table, new AnnualTaxBases(
            profile.Total,
            profile.WorkIncome,
            profile.WorkIncome,
            ageGroup == TaxAgeGroup.AtLeast66AtYearStart,
            workCredit,
            false));
    }

    private static AnnualTax? CalculateAnnualTax(byte table, AnnualTaxBases bases)
    {
        if (table is < MinTaxTable or > MaxTaxTable)
        {
            return null;
        }

        var assessedIncome = RoundDownHundred(bases.GrossIncome);
        var assessedWorkIncome = RoundDownHundred(Math.Min(bases.WorkIncome, assessedIncome));
        var assessedPensionFeeIncome = RoundDownHundred(
            Math.Min(bases.PensionFeeIncome, assessedIncome));
        var basicAllowance = BasicAllowance(assessedIncome, bases.EnhancedAllowance);
        var taxableIncome = SaturatingSubtract(assessedIncome, basicAllowance);

        var stateIncomeTax = taxableIncome >= StateTaxThreshold + 200
            ? (taxableIncome - StateTaxThreshold) * 20 / 100
            : 0;
        var municipalRate = (uint)table * 100 - BurialAndReligiousRate;
        var municipalIncomeTax = PercentageFloor(taxableIncome, municipalRate, 10_000);
        var burialAndReligiousFee =
            PercentageFloor(taxableIncome, BurialAndReligiousRate, 10_000);
        var pensionFee = PensionFee(assessedPensionFeeIncome);
        var publicServiceFee = Math.Min(taxableIncome / 100, PublicServiceFeeMaximum);

        var pensionFeeCredit = Math.Min(
            pensionFee,
            checked(stateIncomeTax + municipalIncomeTax));
        var pensionCreditAgainstMunicipal =
            SaturatingSubtract(pensionFeeCredit, stateIncomeTax);
        var municipalTaxLeft =
            SaturatingSubtract(municipalIncomeTax, pensionCreditAgainstMunicipal);

        uint calculatedWorkCredit = bases.WorkCredit switch
        {
            WorkCreditKind.Under66 => WorkIncomeCreditUnder66(
                assessedWorkIncome,
                basicAllowance,
                municipalRate),
            WorkCreditKind.Over66 => WorkIncomeCreditOver66(assessedWorkIncome),
            _ => 0,
        };
        var workIncomeCredit = Math.Min(calculatedWorkCredit, municipalTaxLeft);
        municipalTaxLeft -= workIncomeCredit;

        uint calculatedSicknessCredit = bases.HasSicknessCredit
            ? SicknessCompensationCredit(assessedIncome, basicAllowance, municipalRate)
            : 0;
        var sicknessCompensationCredit = Math.Min(calculatedSicknessCredit, municipalTaxLeft);
        municipalTaxLeft -= sicknessCompensationCredit;

        uint calculatedEarnedCredit = taxableIncome switch
        {
            <= 40_000 => 0,
            <= 240_000 => (taxableIncome - 40_000) * 75 / 10_000,
            _ => 1_500,
        };
        var earnedIncomeCredit = Math.Min(calculatedEarnedCredit, municipalTaxLeft);

        var additions = (ulong)stateIncomeTax
            + municipalIncomeTax
            + burialAndReligiousFee
            + pensionFee
            + publicServiceFee;
        var credits = (ulong)pensionFeeCredit
            + workIncomeCredit
            + sicknessCompensationCredit
            + earnedIncomeCredit;
        var total = checked((uint)(additions - credits));

        return new AnnualTax(
            assessedIncome,
            basicAllowance,
            taxableIncome,
            stateIncomeTax,
            municipalIncomeTax,
            burialAndReligiousFee,
            pensionFee,
            pensionFeeCredit,
            workIncomeCredit,
            sicknessCompensationCredit,
            earnedIncomeCredit,
            publicServiceFee,
            total);
    }

    internal static uint CalculatePensionFee(uint income) => PensionFee(income);

    /// <summary>
    /// Calculates marginal tax using the annual tax formula.
    /// </summary>
    /// <remarks>
    /// The current monthly income and an income 1,000 SEK higher are annualized.
    /// Their annual-tax difference is divided by the 12,000 SEK annual interval.
    /// </remarks>
    public static double? CalculateMarginalRate(
        byte table,
        TaxColumn column,
        uint monthlyIncome)
    {
        if (monthlyIncome > uint.MaxValue - MarginalIncomeInterval)
        {
            return null;
        }

        var upperIncome = monthlyIncome + MarginalIncomeInterval;
        var lowerAnnualIncome = (ulong)monthlyIncome * 12;
        var upperAnnualIncome = (ulong)upperIncome * 12;
        if (upperAnnualIncome > uint.MaxValue)
        {
            return null;
        }

        var lowerTax = CalculateAnnualTax(table, column, (uint)lowerAnnualIncome);
        var upperTax = CalculateAnnualTax(table, column, (uint)upperAnnualIncome);
        if (lowerTax is null || upperTax is null)
        {
            return null;
        }

        var taxDifference = (long)upperTax.Total - lowerTax.Total;
        return taxDifference * 100.0 / (upperAnnualIncome - lowerAnnualIncome);
    }

    private static int ColumnIndex(TaxColumn column) => column switch
    {
        TaxColumn.Column1 => 0,
        TaxColumn.Column2 => 1,
        TaxColumn.Column3 => 2,
        TaxColumn.Column4 => 3,
        TaxColumn.Column5 => 4,
        TaxColumn.Column6 => 5,
        _ => -1,
    };

    private static uint RoundDownHundred(uint value) => value / 100 * 100;

    private static uint BasicAllowance(uint income, bool enhanced)
    {
        var ordinary = OrdinaryBasicAllowanceScaled(income);
        var raw = enhanced
            ? ordinary + EnhancedBasicAllowancePartScaled(income)
            : ordinary;
        return RoundScaledUpToHundred(BigInteger.Min(raw, Scaled(income)));
    }

    private static BigInteger OrdinaryBasicAllowanceScaled(uint income) => income switch
    {
        <= 58_608 => Pbb(423, 1_000),
        <= 161_024 => Pbb(423, 1_000) + Ratio(Scaled(income - 58_608), 20, 100),
        <= 184_112 => Pbb(77, 100),
        <= 466_496 => Pbb(77, 100) - Ratio(Scaled(income - 184_112), 10, 100),
        _ => Pbb(293, 1_000),
    };

    private static BigInteger EnhancedBasicAllowancePartScaled(uint income) => income switch
    {
        <= 53_872 => Pbb(687, 1_000),
        <= 65_712 => Pbb(885, 1_000) - Ratio(Scaled(income), 20, 100),
        <= 116_328 => Pbb(600, 1_000) + Ratio(Scaled(income), 57, 1_000),
        <= 161_024 => Pbb(333, 1_000) + Ratio(Scaled(income), 1_949, 10_000),
        <= 184_112 => Ratio(Scaled(income), 3_949, 10_000) - Pbb(212, 1_000),
        <= 191_808 => Ratio(Scaled(income), 4_949, 10_000) - Pbb(523, 1_000),
        <= 296_000 => Ratio(Scaled(income), 356, 1_000) - Pbb(73, 1_000),
        <= 466_496 => Pbb(17, 1_000) + Ratio(Scaled(income), 338, 1_000),
        <= 478_336 => Pbb(703, 1_000) + Ratio(Scaled(income), 251, 1_000),
        <= 660_672 => Pbb(2_732, 1_000),
        <= 760_128 => Pbb(9_651, 1_000) - Ratio(Scaled(income), 62, 100),
        _ => Pbb(1_691, 1_000),
    };

    private static uint PensionFee(uint income)
    {
        if (income < 25_042)
        {
            return 0;
        }

        var raw = Ratio(Scaled(Math.Min(income, 673_038)), 7, 100);
        return checked((uint)(((raw + 50 * Scale - 1) / (100 * Scale)) * 100));
    }

    private static uint WorkIncomeCreditUnder66(
        uint income,
        uint allowance,
        uint municipalRate)
    {
        var baseValue = (income switch
        {
            <= 53_872 => Scaled(income),
            <= 191_808 => Pbb(91, 100) + Ratio(Scaled(income - 53_872), 3_874, 10_000),
            <= 478_336 => Pbb(1_813, 1_000) + Ratio(Scaled(income - 191_808), 251, 1_000),
            _ => Pbb(3_027, 1_000),
        }) - Scaled(allowance);
        return ScaledPercentageFloor(BigInteger.Max(baseValue, BigInteger.Zero), municipalRate, 10_000);
    }

    private static uint WorkIncomeCreditOver66(uint income)
    {
        var credit = income switch
        {
            <= 103_600 => Ratio(Scaled(income), 22, 100),
            <= 310_208 => Pbb(2_635, 10_000) + Ratio(Scaled(income), 7, 100),
            _ => Pbb(6_293, 10_000),
        };
        return checked((uint)(credit / Scale));
    }

    private static uint SicknessCompensationCredit(
        uint income,
        uint allowance,
        uint municipalRate)
    {
        var baseValue = (income switch
        {
            <= 53_872 => Scaled(income),
            <= 191_808 => Pbb(91, 100) + Ratio(Scaled(income - 53_872), 3_874, 10_000),
            _ => Pbb(1_813, 1_000) + Ratio(Scaled(income - 191_808), 251, 1_000),
        }) - Scaled(allowance);
        var calculated = ScaledPercentageFloor(
            BigInteger.Max(baseValue, BigInteger.Zero),
            municipalRate,
            10_000);
        var minimumBase = Ratio(Scaled(income), 45, 1_000);
        var minimum = ScaledPercentageFloor(minimumBase, municipalRate, 10_000);
        return Math.Max(calculated, minimum);
    }

    private static BigInteger Scaled(uint value) => value * Scale;

    private static BigInteger Pbb(int numerator, int denominator) =>
        PriceBaseAmount * Scale * numerator / denominator;

    private static BigInteger Ratio(BigInteger value, int numerator, int denominator) =>
        value * numerator / denominator;

    private static uint RoundScaledUpToHundred(BigInteger value) =>
        checked((uint)(((value + 100 * Scale - 1) / (100 * Scale)) * 100));

    private static uint PercentageFloor(uint value, uint numerator, uint denominator) =>
        checked((uint)((ulong)value * numerator / denominator));

    private static uint ScaledPercentageFloor(
        BigInteger value,
        uint numerator,
        uint denominator) =>
        checked((uint)(value * numerator / denominator / Scale));

    private static uint SaturatingSubtract(uint value, uint subtract) =>
        value > subtract ? value - subtract : 0;

    private readonly record struct AnnualTaxBases(
        uint GrossIncome,
        uint PensionFeeIncome,
        uint WorkIncome,
        bool EnhancedAllowance,
        WorkCreditKind WorkCredit,
        bool HasSicknessCredit);

    private enum WorkCreditKind
    {
        None,
        Under66,
        Over66,
        Invalid,
    }
}
