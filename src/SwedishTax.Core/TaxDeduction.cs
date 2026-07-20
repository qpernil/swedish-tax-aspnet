namespace SwedishTax.Core;

public enum TaxDeductionKind
{
    Amount,
    Percent,
}

public readonly record struct TaxDeduction(TaxDeductionKind Kind, uint Value)
{
    public static TaxDeduction Amount(uint value) => new(TaxDeductionKind.Amount, value);

    public static TaxDeduction Percent(uint value) => new(TaxDeductionKind.Percent, value);
}
