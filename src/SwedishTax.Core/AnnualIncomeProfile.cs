namespace SwedishTax.Core;

/// <summary>Annual income categories needed to calculate mixed salary and pension tax.</summary>
public readonly record struct AnnualIncomeProfile(uint WorkIncome, uint PensionIncome)
{
    public uint Total => Arithmetic.SaturatingAdd(WorkIncome, PensionIncome);
}
