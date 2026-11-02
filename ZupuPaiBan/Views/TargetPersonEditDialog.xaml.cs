using System.Windows;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Views;

public partial class TargetPersonEditDialog : Window
{
    private readonly string? _originalBirthDate;
    private readonly string? _originalDeathDate;
    private readonly string? _originalBiography;
    private readonly string? _originalRecordedGeneration;
    private readonly string? _originalRecordedBirthOrder;
    public string PersonName => NameBox.Text;
    public PersonSex Sex => (PersonSex)SexBox.SelectedValue;
    public string? BirthDate => BirthBox.Text.Length == 0 && _originalBirthDate is null ? null : BirthBox.Text;
    public string? DeathDate => DeathBox.Text.Length == 0 && _originalDeathDate is null ? null : DeathBox.Text;
    public string? Biography => BiographyBox.Text.Length == 0 && _originalBiography is null ? null : BiographyBox.Text;
    public string? RecordedGeneration => RecordedGenerationBox.Text.Length == 0 && _originalRecordedGeneration != string.Empty ? null : RecordedGenerationBox.Text;
    public string? RecordedBirthOrder => RecordedBirthOrderBox.Text.Length == 0 && _originalRecordedBirthOrder != string.Empty ? null : RecordedBirthOrderBox.Text;

    public TargetPersonEditDialog(Person person)
    {
        InitializeComponent();
        _originalBirthDate = person.BirthDateText;
        _originalDeathDate = person.DeathDateText;
        _originalBiography = person.Biography;
        _originalRecordedGeneration = person.RecordedGeneration;
        _originalRecordedBirthOrder = person.RecordedBirthOrder;
        PersonIdLabel.Text = $"人物 ID：{person.Id}";
        NameBox.Text = person.Name;
        SexBox.SelectedValue = person.Sex;
        BirthBox.Text = person.BirthDateText;
        DeathBox.Text = person.DeathDateText;
        BiographyBox.Text = person.Biography;
        RecordedGenerationBox.Text = person.RecordedGeneration;
        RecordedBirthOrderBox.Text = person.RecordedBirthOrder;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PersonName))
        {
            MessageBox.Show("请输入人物姓名。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
