using SwedishTax.Core;
using static SwedishTax.Native.NativeTax;
using static SwedishTax.Core.DisplayArithmetic;

namespace SwedishTax.Native;

public sealed class TaxEngineException(string message) : Exception(message);

public static unsafe class NativeCalculator
{
    private static uint Flag(bool value) => value ? 1U : 0;
    private static SwedishTaxOptionalU32 Optional(uint? value) => new() { is_some = Flag(value.HasValue), value = value ?? 0 };

    public static EngineResponse Calculate(byte table, TaxAgeGroup age, IncomePlan plan)
    {
        EngineResponse Failure(EngineIssueKind kind) => new(new(kind, null, null), null);
        if (table is < 29 or > 42) return Failure(EngineIssueKind.UnsupportedTaxTable);
        if (!Enum.IsDefined(age) || plan.Entries.Count is 0 or > 1000 || plan.AdjustmentPercent is > 100
            || plan.Entries.Select(e => e.Id).Distinct().Count() != plan.Entries.Count
            || plan.Entries.Any(e => e.Id == 0 || !Enum.IsDefined(e.Kind) || !Enum.IsDefined(e.PayerRole)
                || e.Start != e.Start.Clamped || e.End != e.End.Clamped)) return Failure(EngineIssueKind.InvalidRequest);
        var entries = plan.Entries.Select(Input).ToArray();
        fixed (SwedishTaxIncomeEntry* pinned = entries)
        {
            var request = new SwedishTaxPlanRequest
            {
                table = table, age_group = (uint)age,
                entries = pinned, entries_count = (nuint)entries.Length,
                adjustment_percent = Optional(plan.AdjustmentPercent), dividend_allowance = Input(plan.DividendAllowance),
            };
            var supporting = Support(&request);
            var dividend = DividendAllowanceForPlan(&request);
            Check(dividend.status);
            var dividendResult = dividend.issue_kind == 0
                ? new DividendAllowanceResult(new(dividend.basic_amount, dividend.owner_cash_salary, dividend.company_cash_payroll,
                    dividend.joint_wage_basis, dividend.joint_wage_basis_after_deduction, dividend.wage_allowance_before_cap,
                    dividend.wage_cap_salary, dividend.wage_cap, dividend.wage_allowance, dividend.acquisition_cost_interest_basis,
                    dividend.acquisition_cost_interest, dividend.saved_allowance, dividend.total,
                    dividend.tax_at_twenty_percent, dividend.net_after_twenty_percent_tax), null)
                : new DividendAllowanceResult(null, dividend.issue_kind is >= 1 and <= 4 ? (DividendAllowanceIssue)(dividend.issue_kind - 1) : throw new TaxEngineException("Unknown dividend issue from Rust."));
            TaxProjection? projection = null;
            WithholdingSummary withholding = new(0, []);
            if (supporting.Issue is null)
            {
                var native = CalculatePlan(&request);
                try
                {
                    Check(native.status);
                    projection = Projection(native, supporting.Totals);
                    if (native.withholding_entries_count > native.withholding_entries_capacity || native.withholding_entries_count > 1000
                        || (native.withholding_entries_count != 0 && native.withholding_entries == null))
                        throw new TaxEngineException("Invalid withholding buffer returned by Rust.");
                    var rows = new ReadOnlySpan<SwedishTaxWithholdingEntry>(native.withholding_entries, checked((int)native.withholding_entries_count)).ToArray();
                    withholding = new(native.withholding_total, rows.Select(Withholding).ToArray());
                }
                finally { CalculationResultFree(native); }
            }
            return new(supporting.Issue,
                new(projection, supporting.Totals, supporting.Entries, supporting.UniformMonthly,
                    supporting.SalaryColumn, supporting.PensionColumn, withholding, dividendResult));
        }
    }

    private sealed record SupportResult(IncomePlanTotals Totals, EntryResult[] Entries, EngineIssue? Issue,
        bool UniformMonthly, byte SalaryColumn, byte PensionColumn);

    private static SupportResult Support(SwedishTaxPlanRequest* request)
    {
        var r = PlanSupport(request);
        try
        {
            Check(r.status);
            if (r.entries_count != request->entries_count || r.entries_count > r.entries_capacity
                || (r.entries_count != 0 && r.entries == null))
                throw new TaxEngineException("Invalid planning buffer returned by Rust.");
            EngineIssue? issue = r.issue_kind switch
            {
                0 => null,
                1 => new(EngineIssueKind.InvalidPaymentPeriod, r.issue_entry_id, null),
                2 => new(EngineIssueKind.SalaryExchangeExceedsAllowance, r.issue_entry_id, r.issue_maximum),
                _ => throw new TaxEngineException("Unknown Rust planning issue."),
            };
            var rows = new ReadOnlySpan<SwedishTaxEntrySupport>(r.entries, checked((int)r.entries_count)).ToArray();
            return new(Totals(r.totals), rows.Select(Entry).ToArray(), issue,
                r.has_uniform_monthly_table_reference != 0, checked((byte)r.salary_column), checked((byte)r.pension_column));
        }
        finally { PlanSupportFree(r); }
    }

    private static IncomePlanTotals Totals(SwedishTaxPlanTotals r) => new(
        r.work_income,
        r.pension_income,
        r.dividend_income,
        r.sgi_annual_rate,
        r.adjustment_basis_work_income,
        r.pension_salary_basis,
        r.regular_pension_premiums,
        r.vacation_pension_premiums,
        r.salary_exchange_sacrifice,
        r.salary_exchange_pension_contributions,
        r.ordinary_income,
        r.monthly_taxable_income,
        r.gross_income,
        r.total_employer_pension_contributions,
        r.employer_pension_share_of_basis);

    private static SalaryExchangeAllowance Allowance(SwedishTaxExchangeAllowance r) => new(
        r.ceiling,
        r.pension_salary_basis_before,
        r.pension_salary_basis_after,
        r.previous_year_pension_salary_basis.is_some == 0 ? null : r.previous_year_pension_salary_basis.value,
        r.pension_and_insurance_costs_before_exchange.is_some == 0 ? null : r.pension_and_insurance_costs_before_exchange.value,
        r.pension_contributions_before,
        r.regular_pension_premiums,
        r.vacation_pension_premiums,
        r.other_exchange_contributions,
        r.selected_exchange_contribution,
        r.total_employer_pension_contributions,
        r.available_contribution,
        r.maximum_sacrifice,
        r.contribution_share_of_basis);

    private static EntryResult Entry(SwedishTaxEntrySupport r)
    {
        Check(r.status);
        return new(r.entry_id, r.annual_amount, r.total_annual_amount, r.withholding_payment_count,
            r.requested_additional_withholding, r.vacation_compensation_amount, r.regular_pension_premium_amount,
            r.vacation_pension_premium_amount, r.salary_exchange_sacrifice, r.salary_exchange_pension_contribution,
            r.is_valid != 0, r.pension_salary_basis_amount, r.pension_benchmark_monthly,
            r.suggested_vacation_days, r.vacation_amount_per_day,
            r.has_allowance == 0 ? null : Allowance(r.allowance),
            [r.monthly_amounts.january, r.monthly_amounts.february, r.monthly_amounts.march,
             r.monthly_amounts.april, r.monthly_amounts.may, r.monthly_amounts.june,
             r.monthly_amounts.july, r.monthly_amounts.august, r.monthly_amounts.september,
             r.monthly_amounts.october, r.monthly_amounts.november, r.monthly_amounts.december]);
    }

    public static SalaryExchange NewSalaryExchange() => new(UpliftBasisPoints: PlanningPolicy().default_exchange_uplift_basis_points);
    public static uint DefaultVacationRateBasisPoints => PlanningPolicy().default_vacation_rate_basis_points;
    public static VacationCompensation NewVacationCompensation(uint days, uint payout) =>
        new(days, payout, true, RateBasisPoints: DefaultVacationRateBasisPoints);

    private static void Check(uint status)
    {
        if (status != SWEDISH_TAX_STATUS_OK)
            throw new TaxEngineException(status == SWEDISH_TAX_STATUS_INVALID_INPUT ? "The Rust engine rejected the plan." : "The Rust engine reported an internal error.");
    }

    private static SwedishTaxIncomeEntry Input(IncomeEntry e) => new()
    {
        id = e.Id, kind = (uint)e.Kind, amount = e.Amount,
        start = new() { month = e.Start.Month, day = e.Start.Day }, end = new() { month = e.End.Month, day = e.End.Day },
        use_annual_daily_rate_for_partial_months = Flag(e.UseAnnualDailyRateForPartialMonths), payer_role = (uint)e.PayerRole,
        own_company_sourced = Flag(e.OwnCompanySourced), adjustment_applies = Flag(e.AdjustmentApplies),
        use_full_year_projection_as_adjustment_basis = Flag(e.UseFullYearProjectionAsAdjustmentBasis),
        additional_withholding_per_payment = Optional(e.AdditionalWithholdingPerPayment), actual_withholding = Optional(e.ActualWithholding),
        included_in_pension_salary_basis = Flag(e.IncludedInPensionSalaryBasis),
        regular_pension_premium = e.RegularPensionPremium is { } premium ? new() { is_some = 1, monthly_override = Optional(premium.MonthlyOverride) } : default,
        vacation_compensation = e.VacationCompensation is { } vacation ? new()
        {
            is_some = 1, annual_entitlement_days = vacation.AnnualEntitlementDays, payout_days = vacation.PayoutDays,
            rate_basis_points = vacation.RateBasisPoints, included_in_pension_salary_basis = Flag(vacation.IncludedInPensionSalaryBasis),
            pension_premium_override = Optional(vacation.PensionPremiumOverride),
        } : default,
        salary_exchange = e.SalaryExchange is { } exchange ? new()
        {
            is_some = 1, sacrificed_salary = exchange.SacrificedSalary, employer_adds_uplift = Flag(exchange.EmployerAddsUplift),
            uplift_basis_points = exchange.UpliftBasisPoints, previous_year_pension_salary_basis = Optional(exchange.PreviousYearPensionSalaryBasis),
            pension_and_insurance_costs_before_exchange = Optional(exchange.PensionAndInsuranceCostsBeforeExchange),
        } : default,
    };

    private static SwedishTaxDividendAllowanceInputs Input(DividendAllowanceInputs2027 d) => new()
    {
        one_person_company = Flag(d.OnePersonCompany), ownership_basis_points = d.OwnershipBasisPoints,
        other_qualified_ownership_basis_points = d.OtherQualifiedOwnershipBasisPoints, spouse_ownership_basis_points = d.SpouseOwnershipBasisPoints,
        company_cash_payroll_2026 = d.CompanyCashPayroll2026, highest_related_cash_salary_2026 = d.HighestRelatedCashSalary2026,
        acquisition_cost = d.AcquisitionCost, acquisition_cost_interest_basis_points = Optional(d.AcquisitionCostInterestBasisPoints), saved_allowance = d.SavedAllowance,
    };

    private static EntryWithholding Withholding(SwedishTaxWithholdingEntry r) => new(r.entry_id, r.gross, r.withheld,
        r.regular_withheld, r.supplemental_withheld, r.additional_withheld, new(
            r.rule_kind <= 6 ? (AppliedWithholdingKind)r.rule_kind : throw new TaxEngineException("Unknown Rust withholding rule."),
            r.rule_kind is 1 or 2 ? checked((byte)r.rule_column) : null,
            r.rule_kind is 2 or 3 or 5 ? r.rule_percent : r.rule_kind == 4 ? 30U : null));

    private static IncomeBasisEstimate Basis(SwedishTaxIncomeBasis b) => new(
        b.kind <= 2 ? (IncomeBasisEstimateKind)b.kind : throw new TaxEngineException("Unknown Rust income basis."),
        b.kind == 0 ? new(b.estimated_basis, b.maximum_basis, Share(b.estimated_basis, b.maximum_basis)) : null);

    private static TaxProjection Projection(SwedishTaxCalculationResult r, IncomePlanTotals totals)
    {
        var a = r.annual_tax;
        uint additions = Add(Add(Add(Add(a.state_income_tax, a.municipal_income_tax), a.burial_and_religious_fee), a.pension_fee), a.public_service_fee);
        uint credits = Add(Add(Add(a.pension_fee_credit, a.work_income_credit), a.sickness_compensation_credit), a.earned_income_credit);
        var tax = new AnnualTax(a.assessed_income, a.basic_allowance, a.taxable_income, a.state_income_tax, a.municipal_income_tax,
            a.burial_and_religious_fee, a.pension_fee, a.pension_fee_credit, a.work_income_credit, a.sickness_compensation_credit,
            a.earned_income_credit, a.public_service_fee, a.total, additions, credits);
        var c = r.adjustment_calibration;
        AdjustmentCalibration? adjustment = r.has_adjustment_calibration == 0 ? null : new(c.basis_income, c.percent,
            c.formula_tax_at_basis, c.assumed_tax_at_basis, c.implied_tax_adjustment, c.projected_ordinary_tax);
        uint referenceTax = r.deduction_kind == 0 ? r.deduction_value : (uint)Math.Min((ulong)r.monthly_income * r.deduction_value / 100, uint.MaxValue);
        long formulaChange = (long)a.total - c.formula_tax_at_basis;
        long withholdingChange = (long)r.withheld_tax - c.assumed_tax_at_basis;
        return new(totals, r.monthly_income, r.annual_income, r.ordinary_income, r.work_income, r.pension_income, r.dividend_income,
            r.sgi_annual_rate, new((TaxDeductionKind)r.deduction_kind, r.deduction_value), tax, adjustment, r.ordinary_final_tax,
            r.dividend_tax, r.total_tax, r.withheld_tax, r.regular_pension_premiums, r.vacation_pension_premiums,
            r.salary_exchange_sacrifice, r.salary_exchange_pension_contributions, r.pension_salary_basis, r.employer_pension_contributions,
            r.marginal_rate, Basis(r.pension_progress), Basis(r.sgi_progress), referenceTax, Sub(r.monthly_income, referenceTax),
            Mul(referenceTax, 12), Share(r.ordinary_final_tax, r.ordinary_income), Share(r.employer_pension_contributions, r.pension_salary_basis),
            Sub(r.annual_income, r.total_tax), Sub(r.annual_income, r.withheld_tax), (long)r.total_tax - r.withheld_tax,
            adjustment is null ? null : new(formulaChange, withholdingChange, formulaChange - withholdingChange));
    }
}
