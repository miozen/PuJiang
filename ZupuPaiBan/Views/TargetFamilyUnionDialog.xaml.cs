using System.Windows;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Views;

/// <summary>A bounded union editor; partner choices are supplied only from the selected genealogy.</summary>
public partial class TargetFamilyUnionDialog : Window
{
    public FamilyUnionKind Kind => (FamilyUnionKind)KindBox.SelectedValue;
    public Guid? PartnerTwoId => (PartnerTwoBox.SelectedItem as Person)?.Id;

    public TargetFamilyUnionDialog(Person partnerOne, IReadOnlyCollection<Person> currentGenealogyPeople)
    {
        ArgumentNullException.ThrowIfNull(partnerOne);
        ArgumentNullException.ThrowIfNull(currentGenealogyPeople);
        InitializeComponent();
        PartnerOneLabel.Text = $"{partnerOne.Name}（ID: {partnerOne.Id}）";
        PartnerTwoBox.ItemsSource = currentGenealogyPeople
            .Where(person => person.GenealogyId == partnerOne.GenealogyId && person.Id != partnerOne.Id)
            .OrderBy(person => person.Name, StringComparer.Ordinal).ThenBy(person => person.Id).ToList();
        KindBox.SelectedValue = FamilyUnionKind.Marriage;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Enum.IsDefined(Kind))
        {
            MessageBox.Show("请选择有效的婚配类型。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (PartnerTwoId is null)
        {
            MessageBox.Show("请选择当前族谱中的婚配第二方。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
