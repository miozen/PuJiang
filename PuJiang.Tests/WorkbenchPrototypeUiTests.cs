using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Xunit;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class WorkbenchPrototypeUiTests
{
    [Fact]
    [Trait("Category", "UI")]
    public void FictionalWorkbenchShowsGenerationsAdoptionAndReflowsOnCollapse()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            WorkbenchPrototypeWindow? window = null;
            try
            {
                window = new WorkbenchPrototypeWindow(); window.Show(); window.UpdateLayout();
                var canvas = Descendants<Canvas>(window).Single();
                var cards = canvas.Children.OfType<Border>().ToArray();
                Border Card(string name) => cards.Single(c => Descendants<TextBlock>(c).Any(t => t.Text == name));
                Assert.Equal(Canvas.GetTop(Card("示例·始祖")), Canvas.GetTop(Card("示例·长房")));
                Assert.Equal(Canvas.GetTop(Card("示例·胜邦")), Canvas.GetTop(Card("示例·振凤")));
                Assert.True(Canvas.GetLeft(Card("示例·振凤")) > Canvas.GetLeft(Card("示例·胜邦")) + 190);
                Assert.Equal(Canvas.GetLeft(Card("示例·振凤")), Canvas.GetLeft(Card("振凤\n（出继胜邦）")));
                Assert.NotNull(Card("示例·未关联"));
                foreach (var line in canvas.Children.OfType<Polyline>())
                    for (int i = 1; i < line.Points.Count; i++)
                        Assert.True(line.Points[i - 1].X == line.Points[i].X || line.Points[i - 1].Y == line.Points[i].Y);
                var toggle = Descendants<Button>(Card("示例·长房")).Single();
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.DoesNotContain(canvas.Children.OfType<Border>(), c => Descendants<TextBlock>(c).Any(t => t.Text == "示例·胜邦"));
                Assert.Contains(canvas.Children.OfType<Border>(), c => Descendants<TextBlock>(c).Any(t => t.Text == "示例·次房"));
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF test timed out");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
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
