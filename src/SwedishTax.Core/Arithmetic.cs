namespace SwedishTax.Core;

internal static class Arithmetic
{
    internal static uint SaturatingAdd(uint left, uint right) =>
        (uint)Math.Min((ulong)left + right, uint.MaxValue);

    internal static uint SaturatingSubtract(uint value, uint subtract) =>
        value > subtract ? value - subtract : 0;

    internal static uint SaturatingMultiply(uint value, uint multiplier) =>
        (uint)Math.Min((ulong)value * multiplier, uint.MaxValue);

    internal static uint Percentage(uint amount, uint percent) =>
        (uint)Math.Min((ulong)amount * percent / 100, uint.MaxValue);

    internal static uint BasisPointsRounded(uint amount, uint basisPoints) =>
        (uint)Math.Min(((ulong)amount * basisPoints + 5_000) / 10_000, uint.MaxValue);
}
