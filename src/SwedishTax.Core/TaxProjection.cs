namespace SwedishTax.Core;

public readonly record struct AdjustmentCalibration(
    uint BasisIncome,
    uint Percent,
    uint FormulaTaxAtBasis,
    uint AssumedTaxAtBasis,
    long ImpliedTaxAdjustment,
    uint ProjectedOrdinaryTax);

/// <summary>
/// Complete, UI-independent projection for an annual income plan.
/// </summary>
public sealed record TaxProjection(
    IncomePlanTotals Totals,
    uint MonthlyIncome,
    TaxDeduction TableDeduction,
    AnnualTax AnnualTax,
    AdjustmentCalibration? AdjustmentCalibration,
    uint OrdinaryFinalTax,
    uint DividendTax,
    uint TotalTax,
    uint WithheldTax,
    double MarginalRate,
    IncomeBasisEstimate PensionProgress,
    IncomeBasisEstimate SgiProgress)
{
    public const uint OwnCompanyDividendTaxPercent = 20;

    public uint AnnualNet => Arithmetic.SaturatingSubtract(Totals.GrossIncome, TotalTax);

    public uint CashAfterWithholding =>
        Arithmetic.SaturatingSubtract(Totals.GrossIncome, WithheldTax);

    public long TaxBalance => (long)TotalTax - WithheldTax;

    public double EffectiveRate => Totals.OrdinaryIncome == 0
        ? 0
        : OrdinaryFinalTax * 100.0 / Totals.OrdinaryIncome;

    public uint TableReferenceTax => TableDeduction.Kind == TaxDeductionKind.Amount
        ? TableDeduction.Value
        : Arithmetic.Percentage(MonthlyIncome, TableDeduction.Value);

    public uint AnnualizedTableReferenceTax =>
        Arithmetic.SaturatingMultiply(TableReferenceTax, 12);

    public uint TableReferenceNet =>
        Arithmetic.SaturatingSubtract(MonthlyIncome, TableReferenceTax);

    public static TaxProjection? Calculate(
        byte table,
        TaxAgeGroup ageGroup,
        IncomePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsValid)
        {
            return null;
        }

        var totals = plan.Totals;
        var monthlyIncome = totals.MonthlyTaxableIncome;
        var tableDeduction = TaxCalculator.GetMonthlyDeduction(
            table,
            ageGroup.SalaryColumn(),
            monthlyIncome);
        var annualTax = TaxCalculator.CalculateAnnualTax(table, ageGroup, totals.AnnualProfile);
        if (tableDeduction is not { } deduction || annualTax is null)
        {
            return null;
        }

        AdjustmentCalibration? adjustmentCalibration = null;
        if (plan.AdjustmentPercent is { } percent && totals.AdjustmentBasisWorkIncome > 0)
        {
            var basisTax = TaxCalculator.CalculateAnnualTax(
                table,
                ageGroup,
                new AnnualIncomeProfile(totals.AdjustmentBasisWorkIncome, 0));
            if (basisTax is null)
            {
                return null;
            }

            var assumedTaxAtBasis = Arithmetic.Percentage(
                totals.AdjustmentBasisWorkIncome,
                percent);
            var impliedTaxAdjustment = (long)basisTax.Total - assumedTaxAtBasis;
            var projectedOrdinaryTax = (uint)Math.Clamp(
                (long)annualTax.Total - impliedTaxAdjustment,
                0,
                uint.MaxValue);
            adjustmentCalibration = new AdjustmentCalibration(
                totals.AdjustmentBasisWorkIncome,
                percent,
                basisTax.Total,
                assumedTaxAtBasis,
                impliedTaxAdjustment,
                projectedOrdinaryTax);
        }

        var ordinaryFinalTax = adjustmentCalibration?.ProjectedOrdinaryTax ?? annualTax.Total;
        var dividendTax = Arithmetic.Percentage(
            totals.DividendIncome,
            OwnCompanyDividendTaxPercent);
        var totalTax = Arithmetic.SaturatingAdd(ordinaryFinalTax, dividendTax);
        var withheldTax = plan.EstimateWithholding(table, ageGroup).Total;
        var upperTax = TaxCalculator.CalculateAnnualTax(
            table,
            ageGroup,
            new AnnualIncomeProfile(
                Arithmetic.SaturatingAdd(totals.WorkIncome, 12_000),
                totals.PensionIncome));
        if (upperTax is null)
        {
            return null;
        }

        var marginalRate = ((long)upperTax.Total - annualTax.Total) * 100.0 / 12_000;
        return new TaxProjection(
            totals,
            monthlyIncome,
            deduction,
            annualTax,
            adjustmentCalibration,
            ordinaryFinalTax,
            dividendTax,
            totalTax,
            withheldTax,
            marginalRate,
            IncomeBases.PublicPensionProgressForIncome(totals.WorkIncome),
            IncomeBases.EstimatedSgiProgressForIncome(totals.SgiAnnualRate));
    }
}
