using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Xunit;
using ZupuPaiBan.Services;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class HorizontalLineageSampleUiTests
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    [Fact]
    [Trait("Category", "UI")]
    public void RealWindowRendersAndKeepsFocusSeparateFromExpansion()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            HorizontalLineageSampleWindow? window = null;
            try
            {
                window = new HorizontalLineageSampleWindow(); window.Show(); window.UpdateLayout();
                Assert.Contains("虚构示例", window.Title);
                Assert.Equal(3, window.CurrentLayout.Nodes.Count);
                Assert.Equal(2, window.CurrentLayout.Edges.Count);
                Assert.Single(Descendants<Canvas>(window));
                Assert.Equal(2, Descendants<System.Windows.Shapes.Polyline>(window).Count());
                AssertSampleGeometry(window, (1, 2));
                window.SelectPerson(Id(2), true);
                Assert.Equal(Id(2), window.SelectedPerson);
                Assert.True(window.IsExpanded(Id(2)));
                Assert.Equal(5, window.CurrentLayout.Nodes.Count);
                window.SelectPerson(Id(3), false);
                Assert.Equal(Id(3), window.SelectedPerson);
                Assert.True(window.IsExpanded(Id(2)));
                Assert.False(window.IsExpanded(Id(3)));
                window.SelectPerson(Id(3), true);
                Assert.True(window.IsExpanded(Id(2)) && window.IsExpanded(Id(3)));
                Assert.Equal(6, window.CurrentLayout.Nodes.Count);
                window.SelectPerson(Id(4), true);
                AssertSampleGeometry(window, (1, 2), (2, 4), (3, 6), (4, 8));
                var expanded = window.CurrentLayout.Nodes.ToArray();
                var nodes = expanded.ToDictionary(n => n.PersonId);
                Assert.True(nodes[Id(8)].Y + nodes[Id(8)].Height < nodes[Id(7)].Y);
                Assert.True(nodes[Id(7)].Y + nodes[Id(7)].Height < nodes[Id(5)].Y);
                Assert.True(nodes[Id(5)].Y + nodes[Id(5)].Height < nodes[Id(3)].Y);
                window.ToggleBranch(Id(2));
                Assert.DoesNotContain(window.CurrentLayout.Nodes, n => n.PersonId == Id(4));
                Assert.True(window.IsExpanded(Id(4)));
                AssertSampleGeometry(window, (1, 2), (3, 6));
                window.ToggleBranch(Id(2));
                Assert.Equal(expanded, window.CurrentLayout.Nodes);
                AssertSampleGeometry(window, (1, 2), (2, 4), (3, 6), (4, 8));
                var toggle = Descendants<Button>(window).First(b => (string?)b.Content == "−");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(window.CurrentLayout.Nodes);
                Assert.Null(window.SelectedPerson);
                Assert.True(window.FindPerson("后代甲"));
                Assert.Equal(Id(8), window.SelectedPerson);
                Assert.Contains(window.CurrentLayout.Nodes, n => n.PersonId == Id(8));
                Assert.False(window.FindPerson("不存在"));
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF test timed out");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void AssertSampleGeometry(HorizontalLineageSampleWindow window, params (int Parent, int FirstChild)[] pairs)
    {
        var layout = window.CurrentLayout;
        foreach (var (parent, child) in pairs)
        {
            var p = layout.Nodes.Single(n => n.PersonId == Id(parent));
            var c = layout.Nodes.Single(n => n.PersonId == Id(child));
            Assert.Equal(p.Y + p.Height / 2, c.Y + c.Height / 2);
        }
        var lines = Descendants<Polyline>(window).ToArray();
        Assert.Equal(layout.Edges.Count, lines.Length);
        foreach (var edge in layout.Edges)
        {
            var p = layout.Nodes.Single(n => n.PersonId == edge.ParentId);
            var c = layout.Nodes.Single(n => n.PersonId == edge.ChildId);
            Assert.Equal(new HorizontalLayoutPoint(p.X + p.Width, p.Y + p.Height / 2), edge.Points[0]);
            Assert.Equal(new HorizontalLayoutPoint(c.X, c.Y + c.Height / 2), edge.Points[^1]);
            Assert.Contains(lines, line => line.Points.Select(point => new HorizontalLayoutPoint(point.X - 40, point.Y - 40)).SequenceEqual(edge.Points));
            for (var i = 1; i < edge.Points.Count; i++)
                Assert.True(edge.Points[i - 1].X == edge.Points[i].X || edge.Points[i - 1].Y == edge.Points[i].Y);
        }
        foreach (var node in layout.Nodes)
        {
            var card = Descendants<Border>(window).Single(b => b.Tag is Guid id && id == node.PersonId);
            Assert.Equal(node.X + 40, Canvas.GetLeft(card));
            Assert.Equal(node.Y + 40, Canvas.GetTop(card));
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
