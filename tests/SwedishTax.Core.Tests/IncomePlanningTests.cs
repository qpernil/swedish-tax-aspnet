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
    public void VacationCompensationSaturatesAtNumericLimits()
    {
        var vacation = new VacationCompensation(
            uint.MaxValue,
            uint.MaxValue,
            true);

        Assert.Equal(uint.MaxValue, vacation.Amount(uint.MaxValue));
    }

    [Fact]
    public void WithholdingRulesAndAdditionalAmountCompose()
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

        pension.AdditionalWithholdingPerPayment = 500;
        estimate = EstimateFor(plan, pensionId);
        Assert.Equal(44_000U, estimate.Withheld);
        Assert.Equal(6_000U, estimate.AdditionalWithheld);
        Assert.Equal(AppliedWithholdingKind.AdjustmentPercent, estimate.Rule.Kind);
    }

    [Fact]
    public void ActualWithholdingOverridesEveryIncomeKind()
    {
        var plan = IncomePlan.WithAnnualSalary(700_000);
        var kinds = Enum.GetValues<IncomeKind>();
        for (var index = 0; index < kinds.Length; index++)
        {
            IncomeEntry entry;
            if (index == 0)
            {
                entry = plan.Entries[0];
            }
            else
            {
                var id = plan.AddEntry(kinds[index]);
                entry = plan.Entries.Single(candidate => candidate.Id == id);
            }
            entry.SetKind(kinds[index], false);
            entry.Amount = (uint)(100_000 + index);
            entry.ActualWithholding = (uint)(10_000 + index);
            entry.AdditionalWithholdingPerPayment = 99;
            entry.AdjustmentApplies = true;
            entry.PayerRole = PayerRole.Secondary;
        }
        plan.AdjustmentPercent = 88;

        var withholding = plan.EstimateWithholding(32, TaxAgeGroup.Under66AtYearStart);

        Assert.Equal(kinds.Length, withholding.Entries.Count);
        for (var index = 0; index < withholding.Entries.Count; index++)
        {
            var row = withholding.Entries[index];
            Assert.Equal((uint)(10_000 + index), row.Withheld);
            Assert.Equal(row.Withheld, row.RegularWithheld);
            Assert.Equal(0U, row.SupplementalWithheld);
            Assert.Equal(0U, row.AdditionalWithheld);
            Assert.Equal(AppliedWithholdingKind.ActualAmount, row.Rule.Kind);
        }
    }

    [Fact]
    public void AdditionalWithholdingUsesPaymentCountAndCannotExceedGross()
    {
        var plan = IncomePlan.WithMonthlySalary(10_000);
        var salary = plan.Entries[0];
        salary.Start = new Date2026(3, 15);
        salary.End = new Date2026(5, 1);
        salary.AdditionalWithholdingPerPayment = 1_000;

        var estimate = EstimateFor(plan, salary.Id);
        Assert.Equal(3U, salary.WithholdingPaymentCount);
        Assert.Equal(3_000U, estimate.AdditionalWithheld);

        salary.AdditionalWithholdingPerPayment = uint.MaxValue;
        estimate = EstimateFor(plan, salary.Id);
        Assert.Equal(estimate.Gross, estimate.Withheld);
        Assert.Equal(estimate.Gross - estimate.RegularWithheld, estimate.AdditionalWithheld);
    }

    [Fact]
    public void Preliminary2027DividendAllowanceMatchesRust()
    {
        var plan = IncomePlan.WithAnnualSalary(800_000);
        plan.Entries[0].OwnCompanySourced = true;

        var result = plan.CalculateDividendAllowance2027();

        var allowance = Assert.IsType<DividendAllowance2027>(result.Allowance);
        Assert.Equal(333_600U, allowance.BasicAmount);
        Assert.Equal(132_800U, allowance.JointWageBasisAfterDeduction);
        Assert.Equal(66_400U, allowance.WageAllowance);
        Assert.Equal(400_000U, allowance.Total);
        Assert.Equal(80_000U, allowance.TaxAtTwentyPercent);
    }

    [Fact]
    public void Preliminary2027OwnershipAllocationFollowsSkatteverketWorkedExamples()
    {
        var inputs = new DividendAllowanceInputs2027 { OwnershipBasisPoints = 5_000 };
        Assert.Equal(166_800U, inputs.Calculate(0).Allowance!.Value.BasicAmount);

        inputs.OwnershipBasisPoints = 2_500;
        inputs.OtherQualifiedOwnershipBasisPoints = 3_300;
        Assert.Equal(83_400U, inputs.Calculate(0).Allowance!.Value.BasicAmount);

        inputs.OtherQualifiedOwnershipBasisPoints = 17_500;
        Assert.Equal(41_700U, inputs.Calculate(0).Allowance!.Value.BasicAmount);
    }

    [Fact]
    public void Preliminary2027WageAllowanceFollowsSkatteverketWorkedExamples()
    {
        var agnes = new DividendAllowanceInputs2027
        {
            OnePersonCompany = false,
            OwnershipBasisPoints = 7_000,
            CompanyCashPayroll2026 = 4_000_000,
        };
        var result = agnes.Calculate(400_000).Allowance!.Value;
        Assert.Equal(2_800_000U, result.JointWageBasis);
        Assert.Equal(2_132_800U, result.JointWageBasisAfterDeduction);
        Assert.Equal(1_066_400U, result.WageAllowance);

        var birger = new DividendAllowanceInputs2027
        {
            OnePersonCompany = false,
            OwnershipBasisPoints = 3_000,
            CompanyCashPayroll2026 = 4_000_000,
        };
        result = birger.Calculate(300_000).Allowance!.Value;
        Assert.Equal(1_200_000U, result.JointWageBasis);
        Assert.Equal(532_800U, result.JointWageBasisAfterDeduction);
        Assert.Equal(266_400U, result.WageAllowance);

        var amy = new DividendAllowanceInputs2027
        {
            OnePersonCompany = false,
            OwnershipBasisPoints = 6_000,
            SpouseOwnershipBasisPoints = 4_000,
            CompanyCashPayroll2026 = 4_000_000,
            HighestRelatedCashSalary2026 = 300_000,
        };
        Assert.Equal(999_840U, amy.Calculate(500_000).Allowance!.Value.WageAllowance);

        var gedion = new DividendAllowanceInputs2027
        {
            OnePersonCompany = false,
            OwnershipBasisPoints = 4_000,
            SpouseOwnershipBasisPoints = 6_000,
            CompanyCashPayroll2026 = 4_000_000,
            HighestRelatedCashSalary2026 = 500_000,
        };
        Assert.Equal(666_560U, gedion.Calculate(300_000).Allowance!.Value.WageAllowance);
    }

    [Fact]
    public void Preliminary2027AllowanceFollowsSkatteverketValterAndHelleExamples()
    {
        var valter = new DividendAllowanceInputs2027
        {
            OnePersonCompany = false,
            CompanyCashPayroll2026 = 1_000_000,
            AcquisitionCost = 25_000,
            SavedAllowance = 750_000,
        }.Calculate(600_000).Allowance!.Value;
        Assert.Equal(333_600U, valter.BasicAmount);
        Assert.Equal(166_400U, valter.WageAllowance);
        Assert.Equal(0U, valter.AcquisitionCostInterest);
        Assert.Equal(1_250_000U, valter.Total);
        Assert.Equal(250_000U, valter.TaxAtTwentyPercent);
        Assert.Equal(1_000_000U, valter.NetAfterTwentyPercentTax);

        var helle = new DividendAllowanceInputs2027
        {
            AcquisitionCost = 250_000,
            AcquisitionCostInterestBasisPoints = 1_155,
        }.Calculate(0).Allowance!.Value;
        Assert.Equal(150_000U, helle.AcquisitionCostInterestBasis);
        Assert.Equal(17_325U, helle.AcquisitionCostInterest);
    }

    [Fact]
    public void QualifiedDividendTaxMatchesSkatteverketParisaExample()
    {
        var plan = IncomePlan.WithAnnualSalary(420_000);
        var dividendId = plan.AddEntry(IncomeKind.OwnCompanyDividend);
        plan.Entries.Single(entry => entry.Id == dividendId).Amount = 78_000;

        var calculation = Assert.IsType<TaxProjection>(
            TaxProjection.Calculate(32, TaxAgeGroup.Under66AtYearStart, plan));
        Assert.Equal(15_600U, calculation.DividendTax);
        Assert.Equal(62_400U, 78_000U - calculation.DividendTax);
    }

    [Fact]
    public void SalaryExchangeValidationMatchesRust()
    {
        var plan = IncomePlan.WithAnnualSalary(1_000_000);
        var id = plan.AddEntry(IncomeKind.OneTimeSalary);
        var entry = plan.Entries.Single(candidate => candidate.Id == id);
        entry.Amount = 400_000;
        entry.SalaryExchange = new SalaryExchange(400_000);

        var issue = Assert.IsType<IncomePlanValidationIssue>(plan.ValidationIssue);

        Assert.Equal(IncomePlanValidationIssueKind.SalaryExchangeExceedsAllowance, issue.Kind);
        Assert.Equal(id, issue.EntryId);
        Assert.False(plan.IsValid);
    }

    [Fact]
    public void JämkningDefaultsToMainPayersAndCanBeOverriddenPerEntry()
    {
        var plan = IncomePlan.WithAnnualSalary(700_000);
        var pensionId = plan.AddEntry(IncomeKind.AnnualOccupationalPension);
        var pension = plan.Entries.Single(entry => entry.Id == pensionId);
        pension.PayerRole = PayerRole.Secondary;

        plan.SetAdjustmentEnabled(true);

        Assert.True(plan.Entries[0].AdjustmentApplies);
        Assert.False(pension.AdjustmentApplies);
        pension.SetPayerRole(PayerRole.Main, true);
        Assert.True(pension.AdjustmentApplies);
        pension.AdjustmentApplies = false;
        pension.SetPayerRole(PayerRole.Main, true);
        Assert.False(pension.AdjustmentApplies);
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
        plan.Entries[0].AdditionalWithholdingPerPayment = 1_000;
        Assert.False(plan.HasUniformMonthlyTableReference);
    }

    private static EntryWithholding EstimateFor(IncomePlan plan, ulong entryId) =>
        plan.EstimateWithholding(32, TaxAgeGroup.Under66AtYearStart)
            .Entries.Single(entry => entry.EntryId == entryId);
}
