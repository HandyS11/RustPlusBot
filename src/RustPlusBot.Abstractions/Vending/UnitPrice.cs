namespace RustPlusBot.Abstractions.Vending;

/// <summary>
/// Exact per-item price comparison. A listing is "<c>Cost</c> currency for <c>Qty</c> items", so the
/// unit price is a ratio; comparing the ratios by cross-multiplication in <see cref="long"/> keeps the
/// comparison exact, whereas dividing would let rounding decide whether someone has undercut you.
/// </summary>
public static class UnitPrice
{
    /// <summary>True when offer A's unit price is at or below offer B's.</summary>
    /// <param name="costA">Currency charged for one order of A.</param>
    /// <param name="qtyA">Items yielded by one order of A; must be at least 1.</param>
    /// <param name="costB">Currency charged for one order of B.</param>
    /// <param name="qtyB">Items yielded by one order of B; must be at least 1.</param>
    /// <returns>True when <c>costA / qtyA &lt;= costB / qtyB</c>.</returns>
    public static bool IsAtOrBelow(int costA, int qtyA, int costB, int qtyB) =>
        (long)costA * qtyB <= (long)costB * qtyA;

    /// <summary>Compares two offers by unit price, for sorting.</summary>
    /// <param name="costA">Currency charged for one order of A.</param>
    /// <param name="qtyA">Items yielded by one order of A; must be at least 1.</param>
    /// <param name="costB">Currency charged for one order of B.</param>
    /// <param name="qtyB">Items yielded by one order of B; must be at least 1.</param>
    /// <returns>Negative when A is cheaper per item, 0 when equal, positive when dearer.</returns>
    public static int Compare(int costA, int qtyA, int costB, int qtyB) =>
        ((long)costA * qtyB).CompareTo((long)costB * qtyA);
}
