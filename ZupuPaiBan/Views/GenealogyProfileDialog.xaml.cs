using System.Windows;
using ZupuPaiBan.Data;

namespace ZupuPaiBan.Views;

public partial class GenealogyProfileDialog : Window
{
    private readonly GenealogyGraphInfo _snapshot;
    public string? Surname => SurnameBox.Text.Length == 0 && _snapshot.Surname != string.Empty ? null : SurnameBox.Text;
    public string? Description => DescriptionBox.Text.Length == 0 && _snapshot.Description != string.Empty ? null : DescriptionBox.Text;

    public GenealogyProfileDialog(GenealogyGraphInfo snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        InitializeComponent();
        GenealogyLabel.Text = $"族谱：{snapshot.Name}（ID: {snapshot.Id}）";
        SurnameBox.Text = snapshot.Surname ?? string.Empty;
        DescriptionBox.Text = snapshot.Description ?? string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
