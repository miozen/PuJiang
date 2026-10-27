using System.Windows;

namespace ZupuPaiBan.Views;

public partial class TargetChildDialog : Window
{
    public string ChildName => ChildNameBox.Text;

    public TargetChildDialog(string parentLabel)
    {
        InitializeComponent();
        ParentLabel.Text = parentLabel;
        Loaded += (_, _) => ChildNameBox.Focus();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ChildName))
        {
            MessageBox.Show("请输入子女姓名。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
