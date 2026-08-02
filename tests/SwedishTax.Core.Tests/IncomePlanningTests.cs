using SwedishTax.Core;

namespace SwedishTax.Core.Tests;

public sealed class IncomePlanningTests
{
    [Fact]
    public void MixedProfilesPreservePureSalaryAndPensionCalculations()
    {
        const uint income = 420_000;

        Assert.Equal(
            TaxCalculator.CalculateAnnualTax(32, TaxColumn.Column1, income),
            TaxCalculator.CalculateAnnualTax(
                32,
                TaxAgeGroup.Under66AtYearStart,
                new AnnualIncomeProfile(income, 0)));
        Assert.Equal(
            TaxCalculator.CalculateAnnualTax(32, TaxColumn.Column6, income),
            TaxCalculator.CalculateAnnualTax(
                32,
                TaxAgeGroup.Under66AtYearStart,
                new AnnualIncomeProfile(0, income)));
        Assert.Equal(
            TaxCalculator.CalculateAnnualTax(32, TaxColumn.Column3, income),
            TaxCalculator.CalculateAnnualTax(
                32,
                TaxAgeGroup.AtLeast66AtYearStart,
                new AnnualIncomeProfile(income, 0)));
        Assert.Equal(
            TaxCalculator.CalculateAnnualTax(32, TaxColumn.Column2, income),
            TaxCalculator.CalculateAnnualTax(
                32,
                TaxAgeGroup.AtLeast66AtYearStart,
                new AnnualIncomeProfile(0, income)));
    }

    [Fact]
    public void IncomeBasesMatch2026MinimumsAndCeilings()
    {
        Assert.Equal(
            IncomeBasisEstimate.Estimated(474_300, IncomeBases.MaximumPensionableIncome),
            IncomeBases.PublicPensionProgress(TaxColumn.Column1, 510_000));
        Assert.Equal(
            IncomeBasisEstimate.Estimated(0, IncomeBases.MaximumPensionableIncome),
            IncomeBases.PublicPensionProgress(TaxColumn.Column1, 25_000));
        Assert.Equal(
            IncomeBasisEstimate.Estimated(
                IncomeBases.MaximumPensionableIncome,
                IncomeBases.MaximumPensionableIncome),
            IncomeBases.PublicPensionProgress(TaxColumn.Column1, 672_600));
        Assert.Equal(
            IncomeBasisEstimate.Estimated(0, IncomeBases.MaximumSgi),
            IncomeBases.EstimatedSgiProgress(TaxColumn.Column1, 14_199));
        Assert.Equal(
            IncomeBasisEstimate.Estimated(14_201, IncomeBases.MaximumSgi),
            IncomeBases.EstimatedSgiProgress(TaxColumn.Column1, 14_201));
        Assert.Equal(
            IncomeBasisEstimate.Estimated(IncomeBases.MaximumSgi, IncomeBases.MaximumSgi),
            IncomeBases.EstimatedSgiProgress(TaxColumn.Column3, 700_000));
    }

    [Fact]
    public void OneTimeWithholdingMatchesOfficial2026Boundaries()
    {
        Assert.Equal(0U, Withholding.OneTimeWithholdingRate(TaxColumn.Column1, 25_041));
        Assert.Equal(10U, Withholding.OneTimeWithholdingRate(TaxColumn.Column1, 25_042));
        Assert.Equal(10U, Withholding.OneTimeWithholdingRate(TaxColumn.Column1, 82_800));
        Assert.Equal(21U, Withholding.OneTimeWithholdingRate(TaxColumn.Column1, 82_801));
        Assert.Equal(34U, Withholding.OneTimeWithholdingRate(TaxColumn.Column1, 660_000));
        Assert.Equal(54U, Withholding.OneTimeWithholdingRate(TaxColumn.Column1, 660_001));
    }

    [Fact]
    public void MonthlyIncomeUsesActualDaysAndFullYearAdjustmentBasis()
    {
        var plan = IncomePlan.WithMonthlySalary(93_000);
        plan.Entries[0].End = new Date2026(10, 18);
        plan.Entries[0].UseFullYearProjectionAsAdjustmentBasis = true;

        Assert.Equal(93_000U, plan.Entries[0].AmountForMonth(9));
        Assert.Equal(54_000U, plan.Entries[0].AmountForMonth(10));
        Assert.Equal(0U, plan.Entries[0].AmountForMonth(11));
        Assert.Equal(891_000U, plan.Entries[0].AnnualAmount);
        Assert.Equal(891_000U, plan.Totals.WorkIncome);
        Assert.Equal(1_116_000U, plan.Totals.AdjustmentBasisWorkIncome);
    }

    [Fact]
    public void CompletePensionAndMaximumSalaryExchangeScenarioMatchesRustProject()
    {
        const uint monthlySalary = 93_000;
        var plan = IncomePlan.WithMonthlySalary(monthlySalary);
        var salary = plan.Entries[0];
        salary.End = new Date2026(10, 18);
        salary.VacationCompensation = VacationCompensation.Suggested(
            30,
            salary.Start,
            salary.End);

        Assert.Equal(891_000U, salary.AnnualAmount);
        Assert.Equal(139_954U, salary.RegularPensionPremiumAmount);
        Assert.Equal(24U, salary.VacationCompensation!.Value.PayoutDays);
        Assert.Equal(115_883U, salary.VacationCompensationAmount);
        Assert.Equal(34_765U, salary.VacationPensionPremiumAmount);

        var lumpId = plan.AddEntry(IncomeKind.OneTimeSalary);
        var lump = plan.Entries.Single(entry => entry.Id == lumpId);
        lump.Amount = monthlySalary * 4;
        lump.SalaryExchange = new SalaryExchange();

        var allowance = Assert.IsType<SalaryExchangeAllowance>(
            plan.GetSalaryExchangeAllowance(lumpId));
        Assert.Equal(1_006_883U, allowance.PensionSalaryBasisBefore);
        Assert.Equal(352_409U, allowance.Ceiling);
        Assert.Equal(177_690U, allowance.AvailableContribution);
        Assert.Equal(168_012U, allowance.MaximumSacrifice);

        lump.SalaryExchange = lump.SalaryExchange.Value with
        {
            SacrificedSalary = allowance.MaximumSacrifice,
        };
        Assert.Equal(203_988U, lump.TotalAnnualAmount);
        Assert.Equal(177_689U, lump.SalaryExchangePensionContribution);

        var totals = plan.Totals;
        Assert.Equal(1_210_871U, totals.WorkIncome);
        Assert.Equal(1_006_883U, totals.PensionSalaryBasis);
        Assert.Equal(139_954U, totals.RegularPensionPremiums);
        Assert.Equal(34_765U, totals.VacationPensionPremiums);
        Assert.Equal(168_012U, totals.SalaryExchangeSacrifice);
        Assert.Equal(177_689U, totals.SalaryExchangePensionContributions);
        Assert.Equal(352_408U, totals.TotalEmployerPensionContributions);
    }

    [Fact]
    public void WithholdingPrecedenceIsCustomThenAdjustmentThenSecondary()
    {
        var plan = IncomePlan.WithAnnualSalary(700_000);
        var pensionId = plan.AddEntry(IncomeKind.AnnualOccupationalPension);
        var pension = plan.Entries.Single(entry => entry.Id == pensionId);
        pension.Amount = 100_000;
        pension.PayerRole = PayerRole.Secondary;

        var estimate = EstimateFor(plan, pensionId);
        Assert.Equal(30_000U, estimate.Withheld);
        Assert.Equal(AppliedWithholdingKind.Secondary30, estimate.Rule.Kind);

        plan.AdjustmentPercent = 38;
        pension.AdjustmentApplies = true;
        estimate = EstimateFor(plan, pensionId);
        Assert.Equal(38_000U, estimate.Withheld);
        Assert.Equal(AppliedWithholdingKind.AdjustmentPercent, estimate.Rule.Kind);

        pension.CustomWithholdingPercent = 42;
        estimate = EstimateFor(plan, pensionId);
        Assert.Equal(42_000U, estimate.Withheld);
        Assert.Equal(AppliedWithholdingKind.CustomPercent, estimate.Rule.Kind);
    }

    [Fact]
    public void ProjectionIncludesDividendTaxWithoutDefaultDividendWithholding()
    {
        var plan = IncomePlan.WithAnnualSalary(700_000);
        var dividendId = plan.AddEntry(IncomeKind.OwnCompanyDividend);
        plan.Entries.Single(entry => entry.Id == dividendId).Amount = 200_000;

        var projection = Assert.IsType<TaxProjection>(
            TaxProjection.Calculate(32, TaxAgeGroup.Under66AtYearStart, plan))!;
        var dividendWithholding = plan.EstimateWithholding(
                32,
                TaxAgeGroup.Under66AtYearStart)
            .Entries.Single(entry => entry.EntryId == dividendId);

        Assert.Equal(40_000U, projection.DividendTax);
        Assert.Equal(0U, dividendWithholding.Withheld);
        Assert.Equal(AppliedWithholdingKind.None, dividendWithholding.Rule.Kind);
        Assert.Equal(
            projection.OrdinaryFinalTax + projection.DividendTax,
            projection.TotalTax);
    }

    [Fact]
    public void ProjectionCalibratesFinalTaxFromJämkningBasis()
    {
        var plan = IncomePlan.WithMonthlySalary(93_000);
        plan.Entries[0].End = new Date2026(10, 18);
        plan.Entries[0].UseFullYearProjectionAsAdjustmentBasis = true;
        plan.Entries[0].AdjustmentApplies = true;
        plan.AdjustmentPercent = 38;

        var projection = Assert.IsType<TaxProjection>(
            TaxProjection.Calculate(32, TaxAgeGroup.Under66AtYearStart, plan))!;
        var calibration = Assert.IsType<AdjustmentCalibration>(
            projection.AdjustmentCalibration);

        Assert.Equal(1_116_000U, calibration.BasisIncome);
        Assert.Equal(38U, calibration.Percent);
        Assert.Equal(
            (uint)((ulong)calibration.BasisIncome * calibration.Percent / 100),
            calibration.AssumedTaxAtBasis);
        Assert.Equal(
            (long)calibration.FormulaTaxAtBasis - calibration.AssumedTaxAtBasis,
            calibration.ImpliedTaxAdjustment);
        Assert.Equal(calibration.ProjectedOrdinaryTax, projection.OrdinaryFinalTax);
    }

    [Fact]
    public void UniformMonthlyReferenceDecisionIsCoreBehavior()
    {
        var plan = IncomePlan.WithMonthlySalary(55_033);
        Assert.True(plan.HasUniformMonthlyTableReference);

        plan.Entries[0].End = new Date2026(10, 18);
        Assert.False(plan.HasUniformMonthlyTableReference);

        plan.Entries[0].End = new Date2026(12, 31);
        plan.Entries[0].CustomWithholdingPercent = 31;
        Assert.False(plan.HasUniformMonthlyTableReference);
    }

    private static EntryWithholding EstimateFor(IncomePlan plan, ulong entryId) =>
        plan.EstimateWithholding(32, TaxAgeGroup.Under66AtYearStart)
            .Entries.Single(entry => entry.EntryId == entryId);
}
