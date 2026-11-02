using System.Windows;
using ZupuPaiBan.Models;
using ZupuPaiBan.Data;

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
    public Guid? BranchId => ((BranchOption)BranchBox.SelectedItem).Id;

    private sealed record BranchOption(Guid? Id, string Label);

    public TargetPersonEditDialog(Person person, IReadOnlyList<GenealogyBranchData>? branches = null)
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
        var byId = (branches ?? []).ToDictionary(branch => branch.Id);
        var options = new List<BranchOption> { new(null, "未指定房支") };
        foreach (var branch in byId.Values.OrderBy(b => b.Name, StringComparer.Ordinal).ThenBy(b => b.Id))
        {
            var names = new List<string>();
            var seen = new HashSet<Guid>();
            var current = branch;
            while (true)
            {
                if (!seen.Add(current.Id)) throw new InvalidOperationException("房支层级存在循环，无法编辑。");
                names.Add(current.Name);
                if (current.ParentBranchId is not Guid parent) break;
                if (!byId.TryGetValue(parent, out current!)) throw new InvalidOperationException("房支上级不存在，无法编辑。");
            }
            names.Reverse();
            options.Add(new BranchOption(branch.Id, $"{string.Join(" / ", names)}（ID: {branch.Id}）"));
        }
        BranchBox.ItemsSource = options;
        BranchBox.SelectedItem = options.SingleOrDefault(option => option.Id == person.BranchId)
            ?? throw new InvalidOperationException("人物房支已失效，请重新加载族谱。");
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
