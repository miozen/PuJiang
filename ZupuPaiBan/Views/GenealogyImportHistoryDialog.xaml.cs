using System.Windows;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class GenealogyImportHistoryDialog : Window
{
    public GenealogyImportHistoryDialog(GenealogyImportHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        InitializeComponent();
        GenealogyLabel.Text = $"族谱：{history.Genealogy.Name}（ID: {history.Genealogy.Id}）";
        HistoryText.Text = history.Records.Count == 0 ? "当前族谱没有导入来源记录。" : string.Join("\n\n", history.Records.Select(record =>
        {
            var sourceIdLabel = record.SourceKind == "PuJiang.InternalJson" ? "来源族谱 ID" :
                record.SourceKind is "PuJiang.ExcelLegacyV1" or "PuJiang.GEDCOM-5.5.x" ? "来源标识（由指纹派生）" : "来源标识";
            return $"来源类型：{record.SourceKind}\n导入模式：{record.ImportMode}\n格式版本：{record.SourceFormatVersion}\n" +
                   $"{sourceIdLabel}：{record.SourceGenealogyId}\n文档指纹：{record.SourceDocumentFingerprint}\n导入时间：{record.ImportedAt:O}";
        }));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
