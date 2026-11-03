using System.Windows;

namespace ZupuPaiBan.Services;

public static class LineageGraphViewport
{
    // Liang–Barsky clipping also detects segments whose endpoints are both outside.
    public static bool IntersectsLine(Rect rect, Point start, Point end)
    {
        double first = 0, last = 1;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            var t = q / p;
            if (p < 0) { if (t > last) return false; first = Math.Max(first, t); }
            else { if (t < first) return false; last = Math.Min(last, t); }
            return true;
        }
        return Clip(-dx, start.X - rect.Left) && Clip(dx, rect.Right - start.X)
            && Clip(-dy, start.Y - rect.Top) && Clip(dy, rect.Bottom - start.Y);
    }
}
