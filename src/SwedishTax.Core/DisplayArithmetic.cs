namespace SwedishTax.Core;

/// <summary>Saturating arithmetic for presentation of Rust calculation results.</summary>
public static class DisplayArithmetic
{
    public static uint Add(uint a, uint b) => (uint)Math.Min((ulong)a + b, uint.MaxValue);
    public static uint Sub(uint a, uint b) => a > b ? a - b : 0;
    public static uint Mul(uint a, uint b) => (uint)Math.Min((ulong)a * b, uint.MaxValue);
    public static double Share(uint amount, uint basis) => basis == 0 ? 0 : (double)amount * 100 / basis;
}
