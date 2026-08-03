using System.Globalization;

namespace SwedishTax.Web.Client;

internal static class PercentageText
{
    internal static string FormatBasisPoints(uint basisPoints)
    {
        var wholePercent = basisPoints / 100;
        var fractionalPercent = basisPoints % 100;
        return fractionalPercent switch
        {
            0 => wholePercent.ToString(CultureInfo.InvariantCulture),
            _ when fractionalPercent % 10 == 0 => FormattableString.Invariant($"{wholePercent}.{fractionalPercent / 10}"),
            _ => FormattableString.Invariant($"{wholePercent}.{fractionalPercent:00}"),
        };
    }

    internal static bool TryParseBasisPoints(string? text, uint maximumBasisPoints, out uint basisPoints)
    {
        basisPoints = 0;
        var normalized = (text ?? string.Empty).Replace(',', '.');
        var parts = normalized.Split('.');
        if (parts.Length > 2 || parts.Any(part => part.Any(character => !char.IsAsciiDigit(character))))
            return false;

        var wholeText = parts[0];
        if (!ulong.TryParse(wholeText.Length == 0 ? "0" : wholeText, NumberStyles.None, CultureInfo.InvariantCulture, out var wholePercent))
            return false;

        var fractionalText = parts.Length == 2 ? parts[1] : string.Empty;
        if (fractionalText.Length > 2)
            return false;
        if (!ulong.TryParse(fractionalText.Length == 0 ? "0" : fractionalText, NumberStyles.None, CultureInfo.InvariantCulture, out var fractionalValue))
            return false;

        var fractionalBasisPoints = fractionalText.Length == 1 ? fractionalValue * 10 : fractionalValue;
        if (wholePercent > uint.MaxValue / 100UL)
            return false;
        var total = wholePercent * 100 + fractionalBasisPoints;
        if (total > uint.MaxValue)
            return false;

        basisPoints = Math.Min((uint)total, maximumBasisPoints);
        return true;
    }
}
