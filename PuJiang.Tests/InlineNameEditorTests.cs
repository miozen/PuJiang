using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class InlineNameEditorTests
{
    [Fact]
    public void CanvasShortcutsAreBlockedWhileTextEditingKeysRemainAvailable()
    {
        Assert.True(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.N, ModifierKeys.Control));
        Assert.True(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.S, ModifierKeys.Control));
        Assert.True(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.M, ModifierKeys.Alt));
        Assert.True(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.F2, ModifierKeys.None));
        Assert.True(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.Tab, ModifierKeys.None));
        Assert.True(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.Tab, ModifierKeys.Shift));
        Assert.False(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.A, ModifierKeys.Control));
        Assert.False(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.Enter, ModifierKeys.None));
        Assert.False(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.Escape, ModifierKeys.None));
        Assert.False(WorkbenchProfileWindow.IsCanvasShortcutBlocked(Key.Delete, ModifierKeys.None));
    }
    [Fact]
    [Trait("Category", "UI")]
    public void NameInputValidatesAndCancelsWithoutMutatingTheHost()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var canvas = new Canvas { LayoutTransform = new ScaleTransform(1.5, 1.5) };
                var editor = new InlineNameEditor();
                Canvas.SetLeft(editor, 234); Canvas.SetTop(editor, 80);
                canvas.Children.Add(editor);
                window = new Window { Content = canvas, Width = 500, Height = 300 };
                window.Show(); window.UpdateLayout();
                var source = PresentationSource.FromVisual(editor.Input)!;
                string? confirmed = null; var cancelled = 0;
                editor.Confirmed += value => confirmed = value;
                editor.Cancelled += () => cancelled++;
                void Key(Key key) => editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                editor.Input.Text = "  "; Key(System.Windows.Input.Key.Enter);
                Assert.Null(confirmed);
                editor.Input.Text = new string('甲', 201); Key(System.Windows.Input.Key.Enter);
                Assert.Null(confirmed);
                editor.Input.Text = "  谱匠  "; Key(System.Windows.Input.Key.Enter);
                Assert.Equal("谱匠", confirmed);
                Key(System.Windows.Input.Key.Escape);
                Assert.Equal(1, cancelled);
                Assert.Equal(234, Canvas.GetLeft(editor)); Assert.Equal(80, Canvas.GetTop(editor));
                Assert.Single(canvas.Children);
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF test timed out");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
