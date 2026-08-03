namespace SwedishTax.Core;

public readonly record struct Date2026(byte Month, byte Day) : IComparable<Date2026>
{
    private static readonly ushort[] MonthStarts =
        [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];

    public static byte DaysInMonth(byte month) => month switch
    {
        1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
        2 => 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    public Date2026 Clamped
    {
        get
        {
            var month = Math.Clamp(Month, (byte)1, (byte)12);
            return new Date2026(month, Math.Clamp(Day, (byte)1, DaysInMonth(month)));
        }
    }

    public ushort Ordinal
    {
        get
        {
            var date = Clamped;
            return checked((ushort)(MonthStarts[date.Month - 1] + date.Day));
        }
    }

    public int CompareTo(Date2026 other) => Ordinal.CompareTo(other.Ordinal);
}

public enum IncomeKind
{
    AnnualSalary,
    MonthlySalary,
    OneTimeSalary,
    MonthlyOccupationalPension,
    AnnualOccupationalPension,
    OwnCompanyDividend,
}

public static class IncomeKindExtensions
{
    public static string Label(this IncomeKind kind) => kind switch
    {
        IncomeKind.AnnualSalary => "Ordinary salary — annual total",
        IncomeKind.MonthlySalary => "Salary — monthly over a period",
        IncomeKind.OneTimeSalary => "One-time salary / termination payment",
        IncomeKind.MonthlyOccupationalPension => "Tjänstepension — monthly over a period",
        IncomeKind.AnnualOccupationalPension => "Tjänstepension — annual total",
        IncomeKind.OwnCompanyDividend => "Dividend from own AB",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool IsMonthly(this IncomeKind kind) =>
        kind is IncomeKind.MonthlySalary or IncomeKind.MonthlyOccupationalPension;

    public static bool IsDividend(this IncomeKind kind) => kind == IncomeKind.OwnCompanyDividend;

    public static bool IsSalary(this IncomeKind kind) => kind is
        IncomeKind.AnnualSalary or IncomeKind.MonthlySalary or IncomeKind.OneTimeSalary;

    public static bool IsPension(this IncomeKind kind) =>
        kind is IncomeKind.MonthlyOccupationalPension or IncomeKind.AnnualOccupationalPension;
}

public enum PayerRole
{
    Main,
    Secondary,
}

public readonly record struct RegularPensionPremium(uint? MonthlyOverride = null)
{
    public const uint MonthlyThreshold = 52_125;
    public const uint LowerRateBasisPoints = 450;
    public const uint UpperRateBasisPoints = 3_000;

    public static uint BenchmarkMonthly(uint monthlySalary)
    {
        var lower = Math.Min(monthlySalary, MonthlyThreshold);
        var upper = Arithmetic.SaturatingSubtract(monthlySalary, MonthlyThreshold);
        var numerator = (ulong)lower * LowerRateBasisPoints + (ulong)upper * UpperRateBasisPoints;
        return (uint)Math.Min((numerator + 5_000) / 10_000, uint.MaxValue);
    }

    public uint MonthlyAmount(uint monthlySalary) =>
        MonthlyOverride ?? BenchmarkMonthly(monthlySalary);
}

public readonly record struct SalaryExchange(
    uint SacrificedSalary = 0,
    bool EmployerAddsUplift = true,
    uint UpliftBasisPoints = 576)
{
    public const uint EmployerPensionAllowanceRateBasisPoints = 3_500;
    public const uint EmployerPensionAllowanceMaximum = 592_000;
    public const uint DefaultUpliftBasisPoints = 576;

    public SalaryExchange() : this(0, true, DefaultUpliftBasisPoints)
    {
    }

    public uint PensionContribution => EmployerAddsUplift
        ? Arithmetic.BasisPointsRounded(
            SacrificedSalary,
            Arithmetic.SaturatingAdd(10_000, UpliftBasisPoints))
        : SacrificedSalary;

    public static uint AllowanceCeiling(uint pensionSalaryBasis) => Math.Min(
        Arithmetic.BasisPointsRounded(
            pensionSalaryBasis,
            EmployerPensionAllowanceRateBasisPoints),
        EmployerPensionAllowanceMaximum);

    public uint MaximumSacrifice(
        uint paymentAmount,
        uint pensionSalaryBasisBefore,
        uint pensionContributionsBefore,
        bool paymentIsPensionable)
    {
        uint low = 0;
        var high = paymentAmount;
        while (low < high)
        {
            var candidateSacrifice = low + (uint)(((ulong)high - low + 1) / 2);
            var pensionSalaryBasisAfter = paymentIsPensionable
                ? Arithmetic.SaturatingSubtract(pensionSalaryBasisBefore, candidateSacrifice)
                : pensionSalaryBasisBefore;
            var ceiling = AllowanceCeiling(pensionSalaryBasisAfter);
            var candidate = this with { SacrificedSalary = candidateSacrifice };
            var valid = Arithmetic.SaturatingAdd(
                pensionContributionsBefore,
                candidate.PensionContribution) <= ceiling;
            if (valid)
            {
                low = candidateSacrifice;
            }
            else
            {
                high = candidateSacrifice - 1;
            }
        }
        return low;
    }
}

public readonly record struct VacationCompensation(
    uint AnnualEntitlementDays,
    uint PayoutDays,
    bool IncludedInPensionSalaryBasis,
    uint? PensionPremiumOverride = null)
{
    public static VacationCompensation Suggested(
        uint annualEntitlementDays,
        Date2026 start,
        Date2026 end) => new(
            annualEntitlementDays,
            SuggestedDays(annualEntitlementDays, start, end),
            true);

    public static uint SuggestedDays(uint annualEntitlementDays, Date2026 start, Date2026 end)
    {
        start = start.Clamped;
        end = end.Clamped;
        if (start.CompareTo(end) > 0)
        {
            return 0;
        }

        var employmentDays = (uint)(end.Ordinal - start.Ordinal + 1);
        return (uint)Math.Min(
            ((ulong)annualEntitlementDays * employmentDays + 364) / 365,
            uint.MaxValue);
    }

    /// <summary>Statutory same-pay estimate: monthly salary / 21 plus 0.43% per day.</summary>
    public uint Amount(uint monthlySalary)
    {
        const ulong denominator = 21 * 10_000;
        const ulong numeratorPerDay = 10_000 + 43 * 21;
        var salaryDays = (ulong)monthlySalary * PayoutDays;
        var numerator = salaryDays > ulong.MaxValue / numeratorPerDay
            ? ulong.MaxValue
            : salaryDays * numeratorPerDay;
        var roundedNumerator = numerator > ulong.MaxValue - denominator / 2
            ? ulong.MaxValue
            : numerator + denominator / 2;
        return (uint)Math.Min(roundedNumerator / denominator, uint.MaxValue);
    }
}

public sealed class IncomeEntry
{
    public IncomeEntry(ulong id, IncomeKind kind)
    {
        Id = id;
        Kind = kind;
        RegularPensionPremium = kind is IncomeKind.AnnualSalary or IncomeKind.MonthlySalary
            ? new RegularPensionPremium()
            : null;
        IncludedInPensionSalaryBasis =
            kind is IncomeKind.AnnualSalary or IncomeKind.MonthlySalary;
    }

    public ulong Id { get; }
    public string Description { get; set; } = string.Empty;
    public IncomeKind Kind { get; set; }
    public uint Amount { get; set; }
    public Date2026 Start { get; set; } = new(1, 1);
    public Date2026 End { get; set; } = new(12, 31);
    public PayerRole PayerRole { get; set; } = PayerRole.Main;
    public bool OwnCompanySourced { get; set; }
    public bool AdjustmentApplies { get; set; }
    public bool UseFullYearProjectionAsAdjustmentBasis { get; set; }
    /// <summary>Voluntary extra tax requested from the payer for each payment.</summary>
    public uint? AdditionalWithholdingPerPayment { get; set; }
    public uint? ActualWithholding { get; set; }
    public VacationCompensation? VacationCompensation { get; set; }
    public RegularPensionPremium? RegularPensionPremium { get; set; }
    public SalaryExchange? SalaryExchange { get; set; }
    public bool IncludedInPensionSalaryBasis { get; set; }

    public uint AnnualAmount => Kind.IsMonthly()
        ? Enumerable.Range(1, 12)
            .Select(month => AmountForMonth((byte)month))
            .Aggregate(0U, Arithmetic.SaturatingAdd)
        : Amount;

    public uint WithholdingPaymentCount => Kind switch
    {
        IncomeKind.AnnualSalary or IncomeKind.AnnualOccupationalPension => 12,
        IncomeKind.MonthlySalary or IncomeKind.MonthlyOccupationalPension
            when Start.Clamped.CompareTo(End.Clamped) <= 0 =>
            (uint)(End.Clamped.Month - Start.Clamped.Month + 1),
        IncomeKind.OneTimeSalary => 1,
        _ => 0,
    };

    public uint RequestedAdditionalWithholding => Arithmetic.SaturatingMultiply(
        AdditionalWithholdingPerPayment ?? 0,
        WithholdingPaymentCount);

    public uint AmountForMonth(byte month)
    {
        if (!Kind.IsMonthly()
            || Start.CompareTo(End) > 0
            || month is < 1 or > 12)
        {
            return 0;
        }

        var start = Start.Clamped;
        var end = End.Clamped;
        if (month < start.Month || month > end.Month)
        {
            return 0;
        }

        var firstDay = month == start.Month ? start.Day : (byte)1;
        var lastDay = month == end.Month ? end.Day : Date2026.DaysInMonth(month);
        var activeDays = (uint)(lastDay - firstDay + 1);
        return (uint)((ulong)Amount * activeDays / Date2026.DaysInMonth(month));
    }

    public bool IsValid => !Kind.IsMonthly() || Start.Clamped.CompareTo(End.Clamped) <= 0;

    public uint TotalAnnualAmount => Arithmetic.SaturatingSubtract(
        Arithmetic.SaturatingAdd(AnnualAmount, VacationCompensationAmount),
        SalaryExchangeSacrifice);

    public uint FullYearAdjustmentBasisAmount =>
        !UseFullYearProjectionAsAdjustmentBasis ? 0 : Kind switch
        {
            IncomeKind.MonthlySalary => Arithmetic.SaturatingMultiply(Amount, 12),
            IncomeKind.AnnualSalary => Amount,
            _ => 0,
        };

    public uint VacationCompensationAmount => Kind == IncomeKind.MonthlySalary
        ? VacationCompensation?.Amount(Amount) ?? 0
        : 0;

    public uint RegularPensionPremiumAmount
    {
        get
        {
            if (RegularPensionPremium is not { } premium)
            {
                return 0;
            }

            return Kind switch
            {
                IncomeKind.AnnualSalary => Arithmetic.SaturatingMultiply(
                    premium.MonthlyAmount(Amount / 12),
                    12),
                IncomeKind.MonthlySalary => Enumerable.Range(1, 12)
                    .Select(month => ProratedMonthlyValue(
                        (byte)month,
                        premium.MonthlyAmount(Amount)))
                    .Aggregate(0U, Arithmetic.SaturatingAdd),
                _ => 0,
            };
        }
    }

    public uint VacationPensionPremiumAmount
    {
        get
        {
            if (Kind != IncomeKind.MonthlySalary
                || VacationCompensation is not { IncludedInPensionSalaryBasis: true } vacation)
            {
                return 0;
            }

            return vacation.PensionPremiumOverride
                ?? Arithmetic.SaturatingSubtract(
                    global::SwedishTax.Core.RegularPensionPremium.BenchmarkMonthly(
                        Arithmetic.SaturatingAdd(Amount, VacationCompensationAmount)),
                    global::SwedishTax.Core.RegularPensionPremium.BenchmarkMonthly(Amount));
        }
    }

    public uint PensionSalaryBasisAmount
    {
        get
        {
            var regular = IncludedInPensionSalaryBasis
                ? Arithmetic.SaturatingSubtract(AnnualAmount, SalaryExchangeSacrifice)
                : 0;
            var vacation = VacationCompensation is { IncludedInPensionSalaryBasis: true }
                ? VacationCompensationAmount
                : 0;
            return Arithmetic.SaturatingAdd(regular, vacation);
        }
    }

    public uint SalaryExchangeSacrifice => Kind == IncomeKind.OneTimeSalary
        ? Math.Min(SalaryExchange?.SacrificedSalary ?? 0, Amount)
        : 0;

    public uint SalaryExchangePensionContribution
    {
        get
        {
            if (Kind != IncomeKind.OneTimeSalary || SalaryExchange is not { } exchange)
            {
                return 0;
            }

            return (exchange with
            {
                SacrificedSalary = Math.Min(exchange.SacrificedSalary, Amount),
            }).PensionContribution;
        }
    }

    public void SetKind(IncomeKind kind, bool adjustmentAvailable)
    {
        if (Kind == kind)
        {
            return;
        }

        var previous = Kind;
        Kind = kind;
        var regularSalary = kind is IncomeKind.AnnualSalary or IncomeKind.MonthlySalary;
        IncludedInPensionSalaryBasis = regularSalary;
        if (regularSalary && RegularPensionPremium is null)
        {
            RegularPensionPremium = new RegularPensionPremium();
        }
        if (!regularSalary)
        {
            UseFullYearProjectionAsAdjustmentBasis = false;
        }
        if (kind != IncomeKind.OneTimeSalary)
        {
            SalaryExchange = null;
        }
        if (kind != IncomeKind.MonthlySalary)
        {
            VacationCompensation = null;
        }
        if (kind.IsDividend())
        {
            AdjustmentApplies = false;
            AdditionalWithholdingPerPayment = null;
        }
        else if (previous.IsDividend() && PayerRole == PayerRole.Main)
        {
            AdjustmentApplies = adjustmentAvailable;
        }
        if (!kind.IsSalary())
        {
            OwnCompanySourced = false;
        }
    }

    public void SetPayerRole(PayerRole payerRole, bool adjustmentAvailable)
    {
        if (PayerRole == payerRole)
        {
            return;
        }

        PayerRole = payerRole;
        AdjustmentApplies = adjustmentAvailable
            && payerRole == PayerRole.Main
            && !Kind.IsDividend();
    }

    private uint ProratedMonthlyValue(byte month, uint monthlyValue)
    {
        if (Start.CompareTo(End) > 0 || month is < 1 or > 12)
        {
            return 0;
        }

        var start = Start.Clamped;
        var end = End.Clamped;
        if (month < start.Month || month > end.Month)
        {
            return 0;
        }

        var firstDay = month == start.Month ? start.Day : (byte)1;
        var lastDay = month == end.Month ? end.Day : Date2026.DaysInMonth(month);
        var activeDays = (uint)(lastDay - firstDay + 1);
        return (uint)((ulong)monthlyValue * activeDays / Date2026.DaysInMonth(month));
    }
}

public readonly record struct IncomePlanTotals(
    uint WorkIncome,
    uint PensionIncome,
    uint DividendIncome,
    uint SgiAnnualRate,
    uint AdjustmentBasisWorkIncome,
    uint PensionSalaryBasis,
    uint RegularPensionPremiums,
    uint VacationPensionPremiums,
    uint SalaryExchangeSacrifice,
    uint SalaryExchangePensionContributions)
{
    public uint OrdinaryIncome => Arithmetic.SaturatingAdd(WorkIncome, PensionIncome);
    public uint MonthlyTaxableIncome => OrdinaryIncome / 12;
    public uint GrossIncome => Arithmetic.SaturatingAdd(OrdinaryIncome, DividendIncome);
    public AnnualIncomeProfile AnnualProfile => new(WorkIncome, PensionIncome);
    public uint TotalEmployerPensionContributions => Arithmetic.SaturatingAdd(
        Arithmetic.SaturatingAdd(RegularPensionPremiums, VacationPensionPremiums),
        SalaryExchangePensionContributions);
}

public readonly record struct SalaryExchangeAllowance(
    uint Ceiling,
    uint PensionSalaryBasisBefore,
    uint PensionSalaryBasisAfter,
    uint RegularPensionPremiums,
    uint VacationPensionPremiums,
    uint OtherExchangeContributions,
    uint AvailableContribution,
    uint MaximumSacrifice);

public enum AppliedWithholdingKind
{
    ActualAmount,
    Table,
    TableAndOneTime,
    OneTimeTable,
    Secondary30,
    AdjustmentPercent,
    None,
}

public readonly record struct AppliedWithholding(
    AppliedWithholdingKind Kind,
    TaxColumn? Column = null,
    uint? Percent = null);

public readonly record struct EntryWithholding(
    ulong EntryId,
    uint Gross,
    uint Withheld,
    uint RegularWithheld,
    uint SupplementalWithheld,
    uint AdditionalWithheld,
    AppliedWithholding Rule);

public sealed record WithholdingSummary(uint Total, IReadOnlyList<EntryWithholding> Entries);

public enum IncomePlanValidationIssueKind
{
    InvalidPaymentPeriod,
    SalaryExchangeExceedsAllowance,
}

public readonly record struct IncomePlanValidationIssue(
    IncomePlanValidationIssueKind Kind,
    ulong EntryId,
    uint? Maximum = null);

public sealed class IncomePlan
{
    private ulong nextId = 2;

    private IncomePlan(IncomeEntry entry) => Entries.Add(entry);

    public List<IncomeEntry> Entries { get; } = [];
    public uint? AdjustmentPercent { get; set; }
    public DividendAllowanceInputs2027 DividendAllowance { get; } = new();

    public static IncomePlan WithAnnualSalary(uint amount)
    {
        var entry = new IncomeEntry(1, IncomeKind.AnnualSalary)
        {
            Description = "Ordinary income",
            Amount = amount,
        };
        return new IncomePlan(entry);
    }

    public static IncomePlan WithMonthlySalary(uint amount)
    {
        var entry = new IncomeEntry(1, IncomeKind.MonthlySalary)
        {
            Description = "Ordinary income",
            Amount = amount,
        };
        return new IncomePlan(entry);
    }

    public ulong AddEntry(IncomeKind kind)
    {
        var id = nextId;
        nextId = nextId == ulong.MaxValue ? ulong.MaxValue : nextId + 1;
        var entry = new IncomeEntry(id, kind)
        {
            AdjustmentApplies = AdjustmentPercent.HasValue && !kind.IsDividend(),
        };
        Entries.Add(entry);
        return id;
    }

    public void SetAdjustmentEnabled(bool enabled)
    {
        if (enabled == AdjustmentPercent.HasValue)
        {
            return;
        }

        AdjustmentPercent = enabled ? Withholding.SecondaryPayerRate : null;
        foreach (var entry in Entries)
        {
            entry.AdjustmentApplies = enabled
                && entry.PayerRole == PayerRole.Main
                && !entry.Kind.IsDividend();
        }
    }

    public void RemoveEntry(ulong id)
    {
        Entries.RemoveAll(entry => entry.Id == id);
        if (Entries.Count == 0)
        {
            AddEntry(IncomeKind.AnnualSalary);
        }
    }

    public IncomePlanValidationIssue? ValidationIssue
    {
        get
        {
            var invalidPeriod = Entries.FirstOrDefault(entry => !entry.IsValid);
            if (invalidPeriod is not null)
            {
                return new IncomePlanValidationIssue(
                    IncomePlanValidationIssueKind.InvalidPaymentPeriod,
                    invalidPeriod.Id);
            }

            foreach (var entry in Entries.Where(entry => entry.SalaryExchange.HasValue))
            {
                var allowance = GetSalaryExchangeAllowance(entry.Id);
                if (allowance is { } value
                    && entry.SalaryExchangeSacrifice > value.MaximumSacrifice)
                {
                    return new IncomePlanValidationIssue(
                        IncomePlanValidationIssueKind.SalaryExchangeExceedsAllowance,
                        entry.Id,
                        value.MaximumSacrifice);
                }
            }

            return null;
        }
    }

    public bool IsValid => ValidationIssue is null;

    public uint OwnCompanySourcedWorkIncome => Entries
        .Where(entry => entry.Kind.IsSalary() && entry.OwnCompanySourced)
        .Select(entry => entry.TotalAnnualAmount)
        .Aggregate(0U, Arithmetic.SaturatingAdd);

    public DividendAllowanceResult CalculateDividendAllowance2027() =>
        DividendAllowance.Calculate(OwnCompanySourcedWorkIncome);

    /// <summary>
    /// Whether the plan represents one uniform full-year main salary for which
    /// a monthly table result is directly comparable.
    /// </summary>
    public bool HasUniformMonthlyTableReference
    {
        get
        {
            if (Entries is not [var entry]
                || entry.PayerRole != PayerRole.Main
                || entry.AdjustmentApplies
                || entry.AdditionalWithholdingPerPayment.HasValue
                || entry.ActualWithholding.HasValue
                || entry.VacationCompensationAmount > 0)
            {
                return false;
            }

            return entry.Kind == IncomeKind.AnnualSalary
                || entry.Kind == IncomeKind.MonthlySalary
                    && entry.Start.Clamped == new Date2026(1, 1)
                    && entry.End.Clamped == new Date2026(12, 31);
        }
    }

    public SalaryExchangeAllowance? GetSalaryExchangeAllowance(ulong entryId)
    {
        var entry = Entries.Find(candidate => candidate.Id == entryId);
        if (entry?.SalaryExchange is not { } exchange)
        {
            return null;
        }

        var totals = Totals;
        var selectedSacrifice = entry.SalaryExchangeSacrifice;
        var otherExchangeContributions = Arithmetic.SaturatingSubtract(
            totals.SalaryExchangePensionContributions,
            entry.SalaryExchangePensionContribution);
        var pensionContributionsBefore = Arithmetic.SaturatingAdd(
            Arithmetic.SaturatingAdd(
                totals.RegularPensionPremiums,
                totals.VacationPensionPremiums),
            otherExchangeContributions);
        var sacrificeInBasis = entry.IncludedInPensionSalaryBasis ? selectedSacrifice : 0;
        var pensionSalaryBasisBefore = Arithmetic.SaturatingAdd(
            totals.PensionSalaryBasis,
            sacrificeInBasis);
        var pensionSalaryBasisAfter = Arithmetic.SaturatingSubtract(
            pensionSalaryBasisBefore,
            sacrificeInBasis);
        var ceiling = SalaryExchange.AllowanceCeiling(pensionSalaryBasisAfter);
        return new SalaryExchangeAllowance(
            ceiling,
            pensionSalaryBasisBefore,
            pensionSalaryBasisAfter,
            totals.RegularPensionPremiums,
            totals.VacationPensionPremiums,
            otherExchangeContributions,
            Arithmetic.SaturatingSubtract(ceiling, pensionContributionsBefore),
            exchange.MaximumSacrifice(
                entry.Amount,
                pensionSalaryBasisBefore,
                pensionContributionsBefore,
                entry.IncludedInPensionSalaryBasis));
    }

    public IncomePlanTotals Totals
    {
        get
        {
            uint workIncome = 0;
            uint pensionIncome = 0;
            uint dividendIncome = 0;
            uint sgiAnnualRate = 0;
            uint adjustmentBasisWorkIncome = 0;
            uint pensionSalaryBasis = 0;
            uint regularPensionPremiums = 0;
            uint vacationPensionPremiums = 0;
            uint salaryExchangeSacrifice = 0;
            uint salaryExchangePensionContributions = 0;

            foreach (var entry in Entries)
            {
                var amount = entry.TotalAnnualAmount;
                regularPensionPremiums = Arithmetic.SaturatingAdd(
                    regularPensionPremiums,
                    entry.RegularPensionPremiumAmount);
                vacationPensionPremiums = Arithmetic.SaturatingAdd(
                    vacationPensionPremiums,
                    entry.VacationPensionPremiumAmount);
                pensionSalaryBasis = Arithmetic.SaturatingAdd(
                    pensionSalaryBasis,
                    entry.PensionSalaryBasisAmount);
                adjustmentBasisWorkIncome = Arithmetic.SaturatingAdd(
                    adjustmentBasisWorkIncome,
                    entry.FullYearAdjustmentBasisAmount);
                salaryExchangeSacrifice = Arithmetic.SaturatingAdd(
                    salaryExchangeSacrifice,
                    entry.SalaryExchangeSacrifice);
                salaryExchangePensionContributions = Arithmetic.SaturatingAdd(
                    salaryExchangePensionContributions,
                    entry.SalaryExchangePensionContribution);

                switch (entry.Kind)
                {
                    case IncomeKind.AnnualSalary:
                    case IncomeKind.MonthlySalary:
                    case IncomeKind.OneTimeSalary:
                        workIncome = Arithmetic.SaturatingAdd(workIncome, amount);
                        break;
                    case IncomeKind.MonthlyOccupationalPension:
                    case IncomeKind.AnnualOccupationalPension:
                        pensionIncome = Arithmetic.SaturatingAdd(pensionIncome, amount);
                        break;
                    case IncomeKind.OwnCompanyDividend:
                        dividendIncome = Arithmetic.SaturatingAdd(dividendIncome, amount);
                        break;
                }

                sgiAnnualRate = entry.Kind switch
                {
                    IncomeKind.AnnualSalary => Arithmetic.SaturatingAdd(sgiAnnualRate, amount),
                    IncomeKind.MonthlySalary => Arithmetic.SaturatingAdd(
                        sgiAnnualRate,
                        Arithmetic.SaturatingMultiply(entry.Amount, 12)),
                    _ => sgiAnnualRate,
                };
            }

            return new IncomePlanTotals(
                workIncome,
                pensionIncome,
                dividendIncome,
                sgiAnnualRate,
                adjustmentBasisWorkIncome,
                pensionSalaryBasis,
                regularPensionPremiums,
                vacationPensionPremiums,
                salaryExchangeSacrifice,
                salaryExchangePensionContributions);
        }
    }

    public WithholdingSummary EstimateWithholding(byte table, TaxAgeGroup ageGroup)
    {
        var totals = Totals;
        var entries = new List<EntryWithholding>(Entries.Count);
        uint total = 0;
        foreach (var entry in Entries)
        {
            var gross = entry.TotalAnnualAmount;
            var (withheld, regularWithheld, supplementalWithheld, additionalWithheld, rule) =
                CalculateEntryWithholding(entry, gross, totals, table, ageGroup);
            total = Arithmetic.SaturatingAdd(total, withheld);
            entries.Add(new EntryWithholding(
                entry.Id,
                gross,
                withheld,
                regularWithheld,
                supplementalWithheld,
                additionalWithheld,
                rule));
        }
        return new WithholdingSummary(total, entries);
    }

    private (uint Withheld, uint RegularWithheld, uint SupplementalWithheld, uint AdditionalWithheld, AppliedWithholding Rule)
        CalculateEntryWithholding(
        IncomeEntry entry,
        uint gross,
        IncomePlanTotals totals,
        byte table,
        TaxAgeGroup ageGroup)
    {
        if (entry.ActualWithholding is { } actualWithholding)
        {
            return (
                actualWithholding,
                actualWithholding,
                0,
                0,
                new AppliedWithholding(AppliedWithholdingKind.ActualAmount));
        }
        if (entry.Kind.IsDividend())
        {
            return (0, 0, 0, 0, new AppliedWithholding(AppliedWithholdingKind.None));
        }
        var (baseWithheld, regularWithheld, supplementalWithheld, rule) =
            CalculateBaseEntryWithholding(entry, gross, totals, table, ageGroup);
        var additionalWithheld = Math.Min(
            entry.RequestedAdditionalWithholding,
            Arithmetic.SaturatingSubtract(gross, baseWithheld));
        return (
            Arithmetic.SaturatingAdd(baseWithheld, additionalWithheld),
            regularWithheld,
            supplementalWithheld,
            additionalWithheld,
            rule);
    }

    private (uint Withheld, uint RegularWithheld, uint SupplementalWithheld, AppliedWithholding Rule)
        CalculateBaseEntryWithholding(
        IncomeEntry entry,
        uint gross,
        IncomePlanTotals totals,
        byte table,
        TaxAgeGroup ageGroup)
    {
        if (entry.AdjustmentApplies && AdjustmentPercent is { } adjustmentPercent)
        {
            var withheld = Arithmetic.Percentage(gross, adjustmentPercent);
            return (
                withheld,
                withheld,
                0,
                new AppliedWithholding(
                    AppliedWithholdingKind.AdjustmentPercent,
                    Percent: adjustmentPercent));
        }
        if (entry.PayerRole == PayerRole.Secondary)
        {
            var withheld = Arithmetic.Percentage(gross, Withholding.SecondaryPayerRate);
            return (
                withheld,
                withheld,
                0,
                new AppliedWithholding(
                    AppliedWithholdingKind.Secondary30,
                    Percent: Withholding.SecondaryPayerRate));
        }

        var column = entry.Kind.IsPension()
            ? ageGroup.PensionColumn()
            : ageGroup.SalaryColumn();
        if (entry.Kind == IncomeKind.OneTimeSalary)
        {
            var percent = Withholding.OneTimeWithholdingRate(column, totals.WorkIncome);
            var withheld = Arithmetic.Percentage(gross, percent);
            return (
                withheld,
                withheld,
                0,
                new AppliedWithholding(AppliedWithholdingKind.OneTimeTable, Percent: percent));
        }

        var regularGross = entry.AnnualAmount;
        var regularWithheld = entry.Kind.IsMonthly()
            ? Enumerable.Range(1, 12)
                .Select(month => TableWithholding(
                    table,
                    column,
                    entry.AmountForMonth((byte)month)))
                .Aggregate(0U, Arithmetic.SaturatingAdd)
            : AnnualizedTableWithholding(table, column, regularGross);
        var vacationGross = entry.VacationCompensationAmount;
        if (vacationGross == 0)
        {
            return (
                regularWithheld,
                regularWithheld,
                0,
                new AppliedWithholding(AppliedWithholdingKind.Table, column));
        }

        var vacationPercent = Withholding.OneTimeWithholdingRate(column, totals.WorkIncome);
        var supplementalWithheld = Arithmetic.Percentage(vacationGross, vacationPercent);
        return (
            Arithmetic.SaturatingAdd(regularWithheld, supplementalWithheld),
            regularWithheld,
            supplementalWithheld,
            new AppliedWithholding(
                AppliedWithholdingKind.TableAndOneTime,
                column,
                vacationPercent));
    }

    private static uint TableWithholding(byte table, TaxColumn column, uint income)
    {
        var deduction = TaxCalculator.GetMonthlyDeduction(table, column, income);
        return deduction?.Kind switch
        {
            TaxDeductionKind.Amount => deduction.Value.Value,
            TaxDeductionKind.Percent => Arithmetic.Percentage(income, deduction.Value.Value),
            _ => 0,
        };
    }

    private static uint AnnualizedTableWithholding(
        byte table,
        TaxColumn column,
        uint annualIncome)
    {
        var deduction = TaxCalculator.GetMonthlyDeduction(table, column, annualIncome / 12);
        return deduction?.Kind switch
        {
            TaxDeductionKind.Amount => Arithmetic.SaturatingMultiply(deduction.Value.Value, 12),
            TaxDeductionKind.Percent => Arithmetic.Percentage(annualIncome, deduction.Value.Value),
            _ => 0,
        };
    }
}
