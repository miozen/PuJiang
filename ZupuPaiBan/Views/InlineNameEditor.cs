using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Views;

/// <summary>Temporary name input; its host owns placement, focus policy and draft changes.</summary>
internal sealed class InlineNameEditor : Border
{
    private readonly TextBox _input = new() { MinWidth = 100 };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    internal event Action<string>? Confirmed;
    internal event Action? Cancelled;
    internal TextBox Input => _input;

    internal InlineNameEditor()
    {
        Background = System.Windows.Media.Brushes.White;
        BorderBrush = System.Windows.Media.Brushes.SteelBlue;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(4);
        var panel = new StackPanel();
        panel.Children.Add(_input);
        panel.Children.Add(_error);
        Child = panel;
        _input.PreviewKeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirm();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancelled?.Invoke();
        }
    }

    internal void Confirm()
    {
        string name;
        try
        {
            name = PersonProfileRules.Validate(_input.Text, PersonSex.Unknown, null, null, null);
            _error.Text = string.Empty;
        }
        catch (InvalidOperationException error) { _error.Text = error.Message; _input.Focus(); return; }
        Confirmed?.Invoke(name);
    }
}
