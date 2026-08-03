namespace SwedishTax.Core;

public readonly record struct IncomeBasisProgress(uint EstimatedBasis, uint MaximumBasis)
{
    public double PercentOfMaximum => EstimatedBasis * 100.0 / MaximumBasis;
}

public enum IncomeBasisEstimateKind
{
    Estimated,
    NotBasedOnSelectedIncome,
    RequiresAdditionalInformation,
}

public readonly record struct IncomeBasisEstimate(
    IncomeBasisEstimateKind Kind,
    IncomeBasisProgress Progress)
{
    public static IncomeBasisEstimate Estimated(uint basis, uint maximum) =>
        new(IncomeBasisEstimateKind.Estimated, new IncomeBasisProgress(basis, maximum));

    public static IncomeBasisEstimate NotBasedOnSelectedIncome =>
        new(IncomeBasisEstimateKind.NotBasedOnSelectedIncome, default);

    public static IncomeBasisEstimate RequiresAdditionalInformation =>
        new(IncomeBasisEstimateKind.RequiresAdditionalInformation, default);
}

public static class IncomeBases
{
    public const uint MinimumPensionableIncome = 25_042;
    public const uint MaximumPensionableIncome = 625_500;
    public const uint GeneralPensionFeeIncomeCeiling = 673_038;
    public const uint MinimumSgiIncome = 14_200;
    public const uint MaximumSgi = 592_000;

    public static IncomeBasisEstimate PublicPensionProgress(
        TaxColumn column,
        uint grossYearlyIncome) => column switch
        {
            TaxColumn.Column1 or TaxColumn.Column3 or TaxColumn.Column5 =>
                PublicPensionProgressForIncome(grossYearlyIncome),
            TaxColumn.Column2 or TaxColumn.Column6 =>
                IncomeBasisEstimate.NotBasedOnSelectedIncome,
            TaxColumn.Column4 => IncomeBasisEstimate.RequiresAdditionalInformation,
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };

    public static IncomeBasisEstimate PublicPensionProgressForIncome(uint grossYearlyIncome)
    {
        var assessedIncome = grossYearlyIncome / 100 * 100;
        var pensionableIncome = assessedIncome < MinimumPensionableIncome
            ? 0
            : Math.Min(
                Arithmetic.SaturatingSubtract(
                    assessedIncome,
                    TaxCalculator.CalculatePensionFee(assessedIncome)),
                MaximumPensionableIncome);
        return IncomeBasisEstimate.Estimated(pensionableIncome, MaximumPensionableIncome);
    }

    public static IncomeBasisEstimate EstimatedSgiProgress(
        TaxColumn column,
        uint grossYearlyIncome) => column is TaxColumn.Column1 or TaxColumn.Column3
            ? EstimatedSgiProgressForIncome(grossYearlyIncome)
            : IncomeBasisEstimate.NotBasedOnSelectedIncome;

    public static IncomeBasisEstimate EstimatedSgiProgressForIncome(uint grossYearlyIncome)
    {
        var estimatedSgi = grossYearlyIncome < MinimumSgiIncome
            ? 0
            : Math.Min(grossYearlyIncome, MaximumSgi);
        return IncomeBasisEstimate.Estimated(estimatedSgi, MaximumSgi);
    }
}
