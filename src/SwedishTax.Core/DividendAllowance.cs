namespace SwedishTax.Core;

public static class DividendAllowanceRules2027
{
    public const uint BasicAmount = 333_600;
    public const uint WageDeduction = 667_200;
    public const uint WageAllowancePercent = 50;
    public const uint WageCapMultiplier = 50;
    public const uint AcquisitionCostThreshold = 100_000;
    public const uint QualifiedDividendTaxPercent = 20;
}

public enum DividendAllowanceIssue
{
    OwnershipExceedsOneHundredPercent,
    SpouseOwnershipExceedsCompany,
    PersonalSalaryExceedsCompanyPayroll,
    MissingAcquisitionCostInterestRate,
}

public readonly record struct DividendAllowanceResult(
    DividendAllowance2027? Allowance,
    DividendAllowanceIssue? Issue)
{
    public bool IsSuccess => Allowance.HasValue;

    public static DividendAllowanceResult Success(DividendAllowance2027 value) =>
        new(value, null);

    public static DividendAllowanceResult Failure(DividendAllowanceIssue issue) =>
        new(null, issue);
}

public readonly record struct DividendAllowance2027(
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
    uint Total)
{
    public uint TaxAtTwentyPercent => Arithmetic.Percentage(
        Total,
        DividendAllowanceRules2027.QualifiedDividendTaxPercent);

    public uint NetAfterTwentyPercentTax =>
        Arithmetic.SaturatingSubtract(Total, TaxAtTwentyPercent);
}

public sealed class DividendAllowanceInputs2027
{
    public bool OnePersonCompany { get; set; } = true;
    public uint OwnershipBasisPoints { get; set; } = 10_000;
    public uint OtherQualifiedOwnershipBasisPoints { get; set; }
    public uint SpouseOwnershipBasisPoints { get; set; }
    public uint CompanyCashPayroll2026 { get; set; }
    public uint HighestRelatedCashSalary2026 { get; set; }
    public uint AcquisitionCost { get; set; }
    public uint? AcquisitionCostInterestBasisPoints { get; set; }
    public uint SavedAllowance { get; set; }

    public DividendAllowanceResult Calculate(uint ownerCashSalary2026)
    {
        if (OwnershipBasisPoints > 10_000)
        {
            return DividendAllowanceResult.Failure(
                DividendAllowanceIssue.OwnershipExceedsOneHundredPercent);
        }

        var jointOwnership = Arithmetic.SaturatingAdd(
            OwnershipBasisPoints,
            SpouseOwnershipBasisPoints);
        if (jointOwnership > 10_000)
        {
            return DividendAllowanceResult.Failure(
                DividendAllowanceIssue.SpouseOwnershipExceedsCompany);
        }

        var companyCashPayroll = OnePersonCompany
            ? ownerCashSalary2026
            : CompanyCashPayroll2026;
        var highestRelatedCashSalary = OnePersonCompany ? 0 : HighestRelatedCashSalary2026;
        if (ownerCashSalary2026 > companyCashPayroll
            || highestRelatedCashSalary > companyCashPayroll)
        {
            return DividendAllowanceResult.Failure(
                DividendAllowanceIssue.PersonalSalaryExceedsCompanyPayroll);
        }

        var basicDenominator = Math.Max(
            Arithmetic.SaturatingAdd(
                OwnershipBasisPoints,
                OtherQualifiedOwnershipBasisPoints),
            10_000);
        var basicAmount = ProportionFloor(
            DividendAllowanceRules2027.BasicAmount,
            OwnershipBasisPoints,
            basicDenominator);
        var jointWageBasis = ProportionFloor(
            companyCashPayroll,
            jointOwnership,
            10_000);
        var jointWageBasisAfterDeduction = Arithmetic.SaturatingSubtract(
            jointWageBasis,
            DividendAllowanceRules2027.WageDeduction);
        var jointWageAllowance = Arithmetic.Percentage(
            jointWageBasisAfterDeduction,
            DividendAllowanceRules2027.WageAllowancePercent);
        var wageAllowanceBeforeCap = jointOwnership == 0
            ? 0
            : ProportionFloor(jointWageAllowance, OwnershipBasisPoints, jointOwnership);
        var wageCapSalary = Math.Max(ownerCashSalary2026, highestRelatedCashSalary);
        var wageCap = Arithmetic.SaturatingMultiply(
            wageCapSalary,
            DividendAllowanceRules2027.WageCapMultiplier);
        var wageAllowance = Math.Min(wageAllowanceBeforeCap, wageCap);

        var acquisitionCostInterestBasis = Arithmetic.SaturatingSubtract(
            AcquisitionCost,
            DividendAllowanceRules2027.AcquisitionCostThreshold);
        uint acquisitionCostInterest;
        if (acquisitionCostInterestBasis == 0)
        {
            acquisitionCostInterest = 0;
        }
        else if (AcquisitionCostInterestBasisPoints is { } rate)
        {
            acquisitionCostInterest = ProportionFloor(
                acquisitionCostInterestBasis,
                rate,
                10_000);
        }
        else
        {
            return DividendAllowanceResult.Failure(
                DividendAllowanceIssue.MissingAcquisitionCostInterestRate);
        }

        var total = Arithmetic.SaturatingAdd(
            Arithmetic.SaturatingAdd(basicAmount, wageAllowance),
            Arithmetic.SaturatingAdd(acquisitionCostInterest, SavedAllowance));
        return DividendAllowanceResult.Success(new DividendAllowance2027(
            basicAmount,
            ownerCashSalary2026,
            companyCashPayroll,
            jointWageBasis,
            jointWageBasisAfterDeduction,
            wageAllowanceBeforeCap,
            wageCapSalary,
            wageCap,
            wageAllowance,
            acquisitionCostInterestBasis,
            acquisitionCostInterest,
            SavedAllowance,
            total));
    }

    private static uint ProportionFloor(uint amount, uint numerator, uint denominator)
    {
        if (denominator == 0)
        {
            return 0;
        }

        return (uint)Math.Min(
            (ulong)amount * numerator / denominator,
            uint.MaxValue);
    }
}
