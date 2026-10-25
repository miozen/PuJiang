using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.Services;

public class PdfExportService
{
    public bool ExportToPdf(LayoutResult layoutResult, LayoutSettings settings, string filePath)
    {
        try
        {
            var document = new PdfDocument();
            document.Info.Title = settings.Title;
            document.Info.Creator = "谱匠 PuJiang";

            var pages = layoutResult.Pages.Count > 0 ? layoutResult.Pages : new List<PageLayout>
            {
                new PageLayout
                {
                    PageNumber = 1,
                    Nodes = layoutResult.Nodes,
                    Lines = layoutResult.Lines,
                    ContentHeight = layoutResult.TotalHeight,
                    StartGeneration = 1,
                    EndGeneration = 1
                }
            };

            foreach (var pageLayout in pages)
            {
                var page = document.AddPage();

                // 设置页面尺寸 (A3 = 297x420mm, A4 = 210x297mm)
                double pageWidthMm = settings.PageSize switch
                {
                    PageSizeOption.A2 => 420,
                    PageSizeOption.A3 => 297,
                    PageSizeOption.A4 => 210,
                    _ => 297
                };
                double pageHeightMm = settings.PageSize switch
                {
                    PageSizeOption.A2 => 594,
                    PageSizeOption.A3 => 420,
                    PageSizeOption.A4 => 297,
                    _ => 420
                };

                if (settings.Orientation == PageOrientation.Portrait)
                {
                    (pageWidthMm, pageHeightMm) = (pageHeightMm, pageWidthMm);
                }

                page.Width = XUnit.FromMillimeter(pageWidthMm);
                page.Height = XUnit.FromMillimeter(pageHeightMm);

                using var gfx = XGraphics.FromPdfPage(page);

                // 绘制标题
                var titleFont = MakeFont(settings, settings.TitleFontSize, XFontStyle.Bold);
                var titleSize = gfx.MeasureString(settings.Title, titleFont);
                gfx.DrawString(
                    settings.Title,
                    titleFont,
                    XBrushes.DarkOrange,
                    new XPoint((page.Width.Millimeter - titleSize.Width) / 2, 15),
                    XStringFormats.Default);

                // 计算缩放
                double availableWidth = page.Width.Millimeter - 20;
                double availableHeight = page.Height.Millimeter - 40;
                double contentWidth = pageLayout.Nodes.Count > 0
                    ? pageLayout.Nodes.Max(n => n.X + n.Width + (n.SpouseNode?.Width ?? 0) + 20) / 3.78 // px to mm
                    : pageWidthMm;
                double contentHeight = pageLayout.Nodes.Count > 0
                    ? pageLayout.Nodes.Max(n => n.Y + n.Height) / 3.78
                    : pageHeightMm;

                double scaleX = availableWidth / Math.Max(contentWidth, 1);
                double scaleY = availableHeight / Math.Max(contentHeight - 15, 1);
                double scale = Math.Min(Math.Min(scaleX, scaleY), 1.0);

                gfx.Save();
                gfx.TranslateTransform(10, 25);
                gfx.ScaleTransform(scale);

                // 绘制连线
                foreach (var line in pageLayout.Lines)
                {
                    var pen = new XPen(line.IsSpouseLine
                        ? XColor.FromArgb(180, 50, 50)
                        : XColor.FromArgb(100, 70, 40),
                        line.IsSpouseLine ? 1.5 : 1.2);

                    if (line.IsSpouseLine)
                    {
                        gfx.DrawLine(pen, line.ParentX / 3.78, line.ParentY / 3.78 - 0.5, line.ChildX / 3.78, line.ChildY / 3.78 - 0.5);
                        gfx.DrawLine(pen, line.ParentX / 3.78, line.ParentY / 3.78 + 0.5, line.ChildX / 3.78, line.ChildY / 3.78 + 0.5);
                    }
                    else
                    {
                        double midY = (line.ParentY + line.ChildY) / 2 / 3.78;
                        gfx.DrawLine(pen, line.ParentX / 3.78, line.ParentY / 3.78, line.ParentX / 3.78, midY);
                        gfx.DrawLine(pen, line.ParentX / 3.78, midY, line.ChildX / 3.78, midY);
                        gfx.DrawLine(pen, line.ChildX / 3.78, midY, line.ChildX / 3.78, line.ChildY / 3.78);
                    }
                }

                // 绘制节点
                foreach (var node in pageLayout.Nodes)
                {
                    DrawNodeCard(gfx, node, settings);
                }

                gfx.Restore();

                // 绘制页脚
                var footerFont = MakeFont(settings, 8);
                var footerText = $"第 {pageLayout.StartGeneration} 代 - 第 {pageLayout.PageNumber}/{pages.Count} 页";
                var footerSize = gfx.MeasureString(footerText, footerFont);
                gfx.DrawString(
                    footerText,
                    footerFont,
                    XBrushes.Gray,
                    new XPoint((page.Width.Millimeter - footerSize.Width) / 2, page.Height.Millimeter - 10),
                    XStringFormats.Default);
            }

            document.Save(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DrawNodeCard(XGraphics gfx, LayoutNode node, LayoutSettings settings)
    {
        double x = node.X / 3.78;
        double y = node.Y / 3.78;
        double width = node.Width / 3.78;
        double height = node.Height / 3.78;

        // 背景
        var bgColor = node.Member.Gender == Gender.Male
            ? XColor.FromArgb(245, 245, 250)
            : XColor.FromArgb(255, 240, 245);
        gfx.DrawRoundedRectangle(new XSolidBrush(bgColor), x, y, width, height, 1, 1);

        // 边框
        var borderPen = new XPen(XColor.FromArgb(120, 80, 40), 0.3);
        gfx.DrawRoundedRectangle(borderPen, x, y, width, height, 1, 1);

        // 姓名
        var nameFont = MakeFont(settings, settings.FontSize + 1, XFontStyle.Bold);
        var nameSize = gfx.MeasureString(node.Member.Name, nameFont);
        gfx.DrawString(
            node.Member.Name,
            nameFont,
            XBrushes.Black,
            new XPoint(x + (width - nameSize.Width) / 2, y + 3 + nameSize.Height),
            XStringFormats.Default);

        // 生卒年
        string lifeText = node.Member.BirthDate;
        if (!string.IsNullOrEmpty(node.Member.DeathDate))
        {
            lifeText += $" - {node.Member.DeathDate}";
        }
        if (!string.IsNullOrEmpty(lifeText))
        {
            var lifeFont = MakeFont(settings, settings.FontSize - 1);
            var lifeSize = gfx.MeasureString(lifeText, lifeFont);
            gfx.DrawString(
                lifeText,
                lifeFont,
                XBrushes.DimGray,
                new XPoint(x + (width - lifeSize.Width) / 2, y + 3 + nameSize.Height + lifeSize.Height + 1),
                XStringFormats.Default);
        }
    }

    /// <summary>根据设置创建 PDF 字体（中文名称映射 + 回退）</summary>
    private static XFont MakeFont(LayoutSettings settings, double size, XFontStyle style = XFontStyle.Regular)
    {
        string fam = ResolvePdfFont(settings.FontFamily);
        try { return new XFont(fam, size, style); }
        catch { return new XFont("SimSun", size, style); }
    }

    private static string ResolvePdfFont(string? name)
    {
        name = (name ?? "").Trim();
        return name switch
        {
            "" => "SimSun",
            "宋体" => "SimSun",
            "黑体" => "SimHei",
            "微软雅黑" => "Microsoft YaHei",
            "楷体" => "KaiTi",
            "仿宋" => "FangSong",
            "隶书" => "LiSu",
            "幼圆" => "YouYuan",
            _ => name
        };
    }
}
