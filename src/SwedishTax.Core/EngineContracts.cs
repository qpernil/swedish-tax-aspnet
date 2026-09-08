namespace SwedishTax.Core;

// Current result models shared with the Rust reference fixtures.
// NativeCalculator maps Rust calculation and planning-support C ABI results.
public sealed record AnnualTax(
    uint AssessedIncome,
    uint BasicAllowance,
    uint TaxableIncome,
    uint StateIncomeTax,
    uint MunicipalIncomeTax,
    uint BurialAndReligiousFee,
    uint PensionFee,
    uint PensionFeeCredit,
    uint WorkIncomeCredit,
    uint SicknessCompensationCredit,
    uint EarnedIncomeCredit,
    uint PublicServiceFee,
    uint Total,
    uint AdditionsTotal,
    uint CreditsTotal);

public sealed record AdjustmentCalibration(
    uint BasisIncome,
    uint Percent,
    uint FormulaTaxAtBasis,
    uint AssumedTaxAtBasis,
    long ImpliedTaxAdjustment,
    uint ProjectedOrdinaryTax);

public sealed record AdjustmentBalanceTrace(
    long FormulaTaxChange,
    long WithholdingChange,
    long OrdinaryBalance);

public sealed record IncomePlanTotals(
    uint WorkIncome,
    uint PensionIncome,
    uint DividendIncome,
    uint SgiAnnualRate,
    uint AdjustmentBasisWorkIncome,
    uint PensionSalaryBasis,
    uint RegularPensionPremiums,
    uint VacationPensionPremiums,
    uint SalaryExchangeSacrifice,
    uint SalaryExchangePensionContributions,
    uint OrdinaryIncome,
    uint MonthlyTaxableIncome,
    uint GrossIncome,
    uint TotalEmployerPensionContributions,
    double EmployerPensionShareOfBasis);

public sealed record SalaryExchangeAllowance(
    uint Ceiling,
    uint PensionSalaryBasisBefore,
    uint PensionSalaryBasisAfter,
    uint? PreviousYearPensionSalaryBasis,
    uint? PensionAndInsuranceCostsBeforeExchange,
    uint PensionContributionsBefore,
    uint RegularPensionPremiums,
    uint VacationPensionPremiums,
    uint OtherExchangeContributions,
    uint SelectedExchangeContribution,
    uint TotalEmployerPensionContributions,
    uint AvailableContribution,
    uint MaximumSacrifice,
    double ContributionShareOfBasis);

public sealed record DividendAllowance2027(
    uint BasicAmount,
    uint OwnerCashSalary,
    uint CompanyCashPayroll,
    uint JointWageBasis,
    uint JointWageBasisAfterDeduction,
    uint WageAllowanceBeforeCap,
    uint WageCapSalary,
    uint WageCap,
    uint WageAllowance,
    uint AcquisitionCostInterestBasis,
    uint AcquisitionCostInterest,
    uint SavedAllowance,
    uint Total,
    uint TaxAtTwentyPercent,
    uint NetAfterTwentyPercentTax);

public sealed record TaxProjection(
    IncomePlanTotals Totals,
    uint MonthlyIncome,
    uint AnnualIncome,
    uint OrdinaryIncome,
    uint WorkIncome,
    uint PensionIncome,
    uint DividendIncome,
    uint SgiAnnualRate,
    TaxDeduction TableDeduction,
    AnnualTax AnnualTax,
    AdjustmentCalibration? AdjustmentCalibration,
    uint OrdinaryFinalTax,
    uint DividendTax,
    uint TotalTax,
    uint WithheldTax,
    uint RegularPensionPremiums,
    uint VacationPensionPremiums,
    uint SalaryExchangeSacrifice,
    uint SalaryExchangePensionContributions,
    uint PensionSalaryBasis,
    uint EmployerPensionContributions,
    double MarginalRate,
    IncomeBasisEstimate PensionProgress,
    IncomeBasisEstimate SgiProgress,
    uint TableReferenceTax,
    uint TableReferenceNet,
    uint AnnualizedTableReferenceTax,
    double EffectiveRate,
    double EmployerPensionShareOfBasis,
    uint AnnualNet,
    uint CashAfterWithholding,
    long TaxBalance,
    AdjustmentBalanceTrace? AdjustmentBalanceTrace);

public sealed record EntryResult(
    ulong EntryId,
    uint AnnualAmount,
    uint TotalAnnualAmount,
    uint WithholdingPaymentCount,
    uint RequestedAdditionalWithholding,
    uint VacationCompensationAmount,
    uint RegularPensionPremiumAmount,
    uint VacationPensionPremiumAmount,
    uint SalaryExchangeSacrifice,
    uint SalaryExchangePensionContribution,
    bool IsValid,
    uint PensionSalaryBasisAmount,
    uint PensionBenchmarkMonthly,
    uint SuggestedVacationDays,
    double VacationAmountPerDay,
    SalaryExchangeAllowance? Allowance,
    uint[] MonthlyAmounts);

public enum TaxDeductionKind { Amount, Percent }
public sealed record TaxDeduction(TaxDeductionKind Kind, uint Value);
public enum IncomeBasisEstimateKind { Estimated, NotBasedOnSelectedIncome, RequiresAdditionalInformation }
public sealed record IncomeBasisProgress(uint EstimatedBasis, uint MaximumBasis, double PercentOfMaximum);
public sealed record IncomeBasisEstimate(IncomeBasisEstimateKind Kind, IncomeBasisProgress? Progress);
public enum AppliedWithholdingKind { ActualAmount, Table, TableAndOneTime, OneTimeTable, Secondary30, AdjustmentPercent, None }
public sealed record AppliedWithholding(AppliedWithholdingKind Kind, byte? Column, uint? Percent);
public sealed record EntryWithholding(ulong EntryId, uint Gross, uint Withheld, uint RegularWithheld, uint SupplementalWithheld, uint AdditionalWithheld, AppliedWithholding Rule);
public sealed record WithholdingSummary(uint Total, IReadOnlyList<EntryWithholding> Entries);
public enum DividendAllowanceIssue { OwnershipExceedsOneHundredPercent, SpouseOwnershipExceedsCompany, PersonalSalaryExceedsCompanyPayroll, MissingAcquisitionCostInterestRate }
public sealed record DividendAllowanceResult(DividendAllowance2027? Allowance, DividendAllowanceIssue? Issue);
public enum EngineIssueKind { InvalidRequest, UnsupportedTaxTable, InvalidPaymentPeriod, SalaryExchangeExceedsAllowance }
public sealed record EngineIssue(EngineIssueKind Kind, ulong? EntryId, uint? Maximum);
public sealed record PlanResult(TaxProjection? Calculation, IncomePlanTotals Totals, IReadOnlyList<EntryResult> Entries, bool HasUniformMonthlyTableReference, byte SalaryColumn, byte PensionColumn, WithholdingSummary Withholding, DividendAllowanceResult Dividend);
public sealed record EngineResponse(EngineIssue? Issue, PlanResult? Result);
public sealed record PlanRequest(byte Table, TaxAgeGroup AgeGroup, IncomePlan Plan);
