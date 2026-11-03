using System.Windows;
using Xunit;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class LineageGraphViewportTests
{
    [Theory]
    [InlineData(-5, 5, 15, 5, true)]
    [InlineData(5, -5, 5, 15, true)]
    [InlineData(-5, 0, 15, 0, true)]
    [InlineData(-5, -5, -1, -1, false)]
    [InlineData(-5, 11, 15, 11, false)]
    [InlineData(5, 5, 5, 5, true)]
    public void ClipsSegmentsIncludingCrossingAndBoundary(double x1, double y1, double x2, double y2, bool expected)
        => Assert.Equal(expected, LineageGraphViewport.IntersectsLine(new Rect(0, 0, 10, 10), new Point(x1, y1), new Point(x2, y2)));
}
