using System.Windows;
using System.Windows.Controls;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Views;

/// <summary>A bounded editor: it receives only members from the already selected genealogy.</summary>
public partial class TargetRelationshipDialog : Window
{
    public PersonRelationshipKind Kind => (PersonRelationshipKind)KindBox.SelectedValue;
    public Guid? RelatedPersonId => Kind == PersonRelationshipKind.LostAncestry
        ? null
        : (RelatedPersonBox.SelectedItem as Person)?.Id;

    public TargetRelationshipDialog(Person subject, IReadOnlyCollection<Person> currentGenealogyPeople)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(currentGenealogyPeople);
        InitializeComponent();
        SubjectLabel.Text = $"{subject.Name}（ID: {subject.Id}）";
        RelatedPersonBox.ItemsSource = currentGenealogyPeople
            .Where(person => person.GenealogyId == subject.GenealogyId && person.Id != subject.Id)
            .OrderBy(person => person.Name, StringComparer.Ordinal).ThenBy(person => person.Id).ToList();
        KindBox.SelectedValue = PersonRelationshipKind.BiologicalParent;
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var lost = KindBox.SelectedValue is PersonRelationshipKind.LostAncestry;
        RelatedLabel.IsEnabled = !lost;
        RelatedPersonBox.IsEnabled = !lost;
        if (lost) RelatedPersonBox.SelectedItem = null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Enum.IsDefined(Kind))
        {
            MessageBox.Show("请选择有效的关系类型。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (Kind != PersonRelationshipKind.LostAncestry && RelatedPersonId is null)
        {
            MessageBox.Show("请选择当前族谱中的关系对方。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
