using System;

namespace LiveSplit.UI.Components;

// Digit-width stability for the value cells. Each cell reserves room for N
// digits (the widest N-digit run in the row font) so a rollover inside the
// reserve — 9->10, 99->100 — does not shift every counter to its right. The
// cell grows only once the value outruns the reserve, and the value sits
// left-aligned inside it so the icon-to-number gap never moves.
internal static class ValueWidth
{
    public const int MinDigits = 1;
    public const int MaxDigits = 6;
    public const int DefaultDigits = 2;

    public static int ClampDigits(int digits)
        => Math.Max(MinDigits, Math.Min(MaxDigits, digits));

    // Digits the cell is sized for: the floor, or the value's own digit count
    // once it outgrows the floor. Deaths at 1234 get four digits' room while
    // Exits at 12 sit at the floor, and neither jitters within its decade.
    public static int DigitsFor(int value, int floorDigits)
        => Math.Max(ClampDigits(floorDigits), value.ToString("0").Length);

    // Widest run of `digits` identical digits under `measure`. Digits are not
    // all the same width in proportional fonts ('1' is usually narrow), so the
    // reserve is the max over 0-9 rather than any one glyph.
    public static float Reserve(Func<string, float> measure, int digits)
    {
        int n = ClampDigits(digits);
        float widest = 0f;
        for (char d = '0'; d <= '9'; d++)
        {
            widest = Math.Max(widest, measure(new string(d, n)));
        }
        return widest;
    }

    public static float Cell(float measured, float reserve)
        => Math.Max(measured, reserve);
}
