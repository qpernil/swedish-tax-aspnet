namespace SwedishTax.Core;

/// <summary>Age category used by the 2026 earned-income and basic-allowance rules.</summary>
public enum TaxAgeGroup
{
    Under66AtYearStart,
    AtLeast66AtYearStart,
}

public static class TaxAgeGroupExtensions
{
    public static TaxColumn SalaryColumn(this TaxAgeGroup ageGroup) => ageGroup switch
    {
        TaxAgeGroup.Under66AtYearStart => TaxColumn.Column1,
        TaxAgeGroup.AtLeast66AtYearStart => TaxColumn.Column3,
        _ => throw new ArgumentOutOfRangeException(nameof(ageGroup)),
    };

    public static TaxColumn PensionColumn(this TaxAgeGroup ageGroup) => ageGroup switch
    {
        TaxAgeGroup.Under66AtYearStart => TaxColumn.Column6,
        TaxAgeGroup.AtLeast66AtYearStart => TaxColumn.Column2,
        _ => throw new ArgumentOutOfRangeException(nameof(ageGroup)),
    };
}
