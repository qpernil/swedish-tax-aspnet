namespace SwedishTax.Core;

public static class Withholding
{
    public const uint SecondaryPayerRate = 30;

    /// <summary>Returns the 2026 preliminary withholding rate for a one-time payment.</summary>
    public static uint OneTimeWithholdingRate(TaxColumn column, uint annualIncome)
    {
        ReadOnlySpan<(uint Maximum, uint Rate)> thresholds = column switch
        {
            TaxColumn.Column1 =>
            [
                (25_041, 0), (82_800, 10), (192_000, 21), (477_600, 26),
                (660_000, 34), (uint.MaxValue, 54),
            ],
            TaxColumn.Column2 =>
                [(65_800, 0), (477_600, 26), (660_000, 34), (uint.MaxValue, 55)],
            TaxColumn.Column3 =>
            [
                (25_041, 0), (331_200, 10), (477_600, 26), (660_000, 34),
                (uint.MaxValue, 55),
            ],
            TaxColumn.Column4 =>
            [
                (25_041, 0), (54_000, 3), (192_000, 22), (660_000, 26),
                (uint.MaxValue, 46),
            ],
            TaxColumn.Column5 =>
            [
                (25_041, 0), (32_400, 10), (160_800, 29), (184_800, 34),
                (660_000, 38), (uint.MaxValue, 54),
            ],
            TaxColumn.Column6 =>
            [
                (25_041, 0), (160_800, 29), (184_800, 34), (660_000, 38),
                (uint.MaxValue, 54),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };

        foreach (var (maximum, rate) in thresholds)
        {
            if (annualIncome <= maximum)
            {
                return rate;
            }
        }

        throw new InvalidOperationException("Every one-time table ends at uint.MaxValue.");
    }
}
