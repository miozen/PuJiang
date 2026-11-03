using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;
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
