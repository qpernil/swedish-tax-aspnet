using System.Text.Json.Serialization;

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

    [JsonIgnore]
    public Date2026 Clamped
    {
        get
        {
            var month = Math.Clamp(Month, (byte)1, (byte)12);
            return new Date2026(month, Math.Clamp(Day, (byte)1, DaysInMonth(month)));
        }
    }

    [JsonIgnore]
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

public readonly record struct RegularPensionPremium(uint? MonthlyOverride = null);
public readonly record struct SalaryExchange(
    uint SacrificedSalary = 0, bool EmployerAddsUplift = true, uint UpliftBasisPoints = 576,
    uint? PreviousYearPensionSalaryBasis = null, uint? PensionAndInsuranceCostsBeforeExchange = null)
{
    public SalaryExchange() : this(0, true, 576, null, null) { }
}
public readonly record struct VacationCompensation(
    uint AnnualEntitlementDays, uint PayoutDays, bool IncludedInPensionSalaryBasis,
    uint? PensionPremiumOverride = null, uint RateBasisPoints = 540);
public sealed class DividendAllowanceInputs2027
{
    public bool OnePersonCompany { get; set; } = true;
    public uint OwnershipBasisPoints { get; set; } = 10_000;
    public uint OtherQualifiedOwnershipBasisPoints { get; set; }
    public uint SpouseOwnershipBasisPoints { get; set; }
    [JsonPropertyName("company_cash_payroll_2026")]
    public uint CompanyCashPayroll2026 { get; set; }
    [JsonPropertyName("highest_related_cash_salary_2026")]
    public uint HighestRelatedCashSalary2026 { get; set; }
    public uint AcquisitionCost { get; set; }
    public uint? AcquisitionCostInterestBasisPoints { get; set; }
    public uint SavedAllowance { get; set; }

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

    public ulong Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public IncomeKind Kind { get; set; }
    public uint Amount { get; set; }
    public Date2026 Start { get; set; } = new(1, 1);
    public Date2026 End { get; set; } = new(12, 31);
    public bool UseAnnualDailyRateForPartialMonths { get; set; }
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

    [JsonIgnore] public EntryResult? Result { get; set; }
    [JsonIgnore] public uint AnnualAmount => Result?.AnnualAmount ?? 0;
    [JsonIgnore] public uint TotalAnnualAmount => Result?.TotalAnnualAmount ?? 0;
    [JsonIgnore] public uint WithholdingPaymentCount => Result?.WithholdingPaymentCount ?? 0;
    [JsonIgnore] public uint RequestedAdditionalWithholding => Result?.RequestedAdditionalWithholding ?? 0;
    [JsonIgnore] public uint VacationCompensationAmount => Result?.VacationCompensationAmount ?? 0;
    [JsonIgnore] public uint RegularPensionPremiumAmount => Result?.RegularPensionPremiumAmount ?? 0;
    [JsonIgnore] public uint VacationPensionPremiumAmount => Result?.VacationPensionPremiumAmount ?? 0;
    [JsonIgnore] public uint SalaryExchangeSacrifice => Result?.SalaryExchangeSacrifice ?? 0;
    [JsonIgnore] public uint SalaryExchangePensionContribution => Result?.SalaryExchangePensionContribution ?? 0;
    [JsonIgnore] public bool IsValid => Result?.IsValid ?? true;
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
        if (!kind.IsMonthly()) UseAnnualDailyRateForPartialMonths = false;
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

}

public sealed class IncomePlan
{
    private ulong nextId = 2;

    public IncomePlan() { }
    private IncomePlan(IncomeEntry entry) => Entries.Add(entry);

    public List<IncomeEntry> Entries { get; set; } = [];
    public uint? AdjustmentPercent { get; set; }
    public DividendAllowanceInputs2027 DividendAllowance { get; set; } = new();

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
        var largest = Entries.Select(e => e.Id).DefaultIfEmpty(0UL).Max();
        if (largest == ulong.MaxValue) throw new InvalidOperationException("No income identifiers remain.");
        nextId = Math.Max(nextId, largest + 1);
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

        AdjustmentPercent = enabled ? 30U : null;
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

}
