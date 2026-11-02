using System.Windows;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class PersonDeletePreviewDialog : Window
{
    public PersonDeletePreviewDialog(PersonDeletePreview preview)
    {
        InitializeComponent();
        Subject.Text = $"人物：{preview.PersonName}（ID: {preview.PersonId}）\n关联记录：{preview.Links.Count}";
        LinkList.ItemsSource = preview.Links.Select(link => $"{link.Description}（记录 ID: {link.Id}）").ToList();
        LinkList.DisplayMemberPath = "";
        DeleteButton.IsEnabled = preview.Links.Count == 0;
        Guidance.Text = preview.Links.Count == 0 ? "没有关联记录。删除人物无法撤销。" : "存在关联，不能删除。请关闭此窗口，通过“解除关系/婚配”逐条处理后重新预览。";
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
