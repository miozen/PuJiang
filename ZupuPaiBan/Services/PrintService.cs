using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Xps.Packaging;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Services;

public class PrintService
{
    public bool Print(List<FixedPage> pages, string description)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
        {
            var writer = PrintQueue.CreateXpsDocumentWriter(dialog.PrintQueue);
            var doc = new FixedDocument();
            foreach (var page in pages)
            {
                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(page);
                doc.Pages.Add(pageContent);
            }
            writer.Write(doc);
            return true;
        }
        return false;
    }

    public bool ExportToXps(List<FixedPage> pages, string filePath)
    {
        string? temporaryPath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            var targetPath = System.IO.Path.GetFullPath(filePath);
            var directory = System.IO.Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory)) return false;

            temporaryPath = System.IO.Path.Combine(
                directory,
                $".{System.IO.Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
            var doc = new FixedDocument();
            foreach (var page in pages)
            {
                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(page);
                doc.Pages.Add(pageContent);
            }

            using (var xpsDocument = new XpsDocument(temporaryPath, System.IO.FileAccess.Write))
            {
                var writer = XpsDocument.CreateXpsDocumentWriter(xpsDocument);
                writer.Write(doc);
            }

            System.IO.File.Move(temporaryPath, targetPath, overwrite: true);
            temporaryPath = null;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null && System.IO.File.Exists(temporaryPath))
                System.IO.File.Delete(temporaryPath);
        }
    }

    public List<FixedPage> CreatePrintPages(LayoutResult layoutResult, LayoutSettings settings, Dictionary<int, (double dx, double dy)>? positionOffsets = null)
    {
        var pages = new List<FixedPage>();

        if (layoutResult.Pages.Count == 0)
        {
            pages.Add(CreateSinglePage(layoutResult.Nodes, layoutResult.Lines, settings, 1, 1, 1, positionOffsets));
            return pages;
        }

        foreach (var pageLayout in layoutResult.Pages)
        {
            var page = CreateSinglePage(
                pageLayout.Nodes,
                pageLayout.Lines,
                settings,
                pageLayout.PageNumber,
                layoutResult.TotalPages,
                pageLayout.StartGeneration,
                positionOffsets);
            pages.Add(page);
        }

        return pages;
    }

    private FixedPage CreateSinglePage(
        List<LayoutNode> nodes,
        List<ConnectionLine> lines,
        LayoutSettings settings,
        int pageNumber,
        int totalPages,
        int startGeneration,
        Dictionary<int, (double dx, double dy)>? positionOffsets)
    {
        var page = new FixedPage
        {
            Width = settings.PageWidth,
            Height = settings.PageHeight
        };

        var canvas = new Canvas
        {
            Width = settings.PageWidth,
            Height = settings.PageHeight,
            ClipToBounds = true
        };

        // 将选中字体作为继承属性应用到整页（标题、节点、页脚均生效）
        try
        {
            TextElement.SetFontFamily(canvas, new FontFamily(settings.FontFamily));
        }
        catch
        {
            TextElement.SetFontFamily(canvas, new FontFamily("宋体"));
        }

        // 绘制标题
        var titleBlock = new TextBlock
        {
            Text = settings.Title,
            FontSize = settings.TitleFontSize,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 40, 10))
        };
        titleBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(titleBlock, (settings.PageWidth - titleBlock.DesiredSize.Width) / 2);
        Canvas.SetTop(titleBlock, 15);
        canvas.Children.Add(titleBlock);

        // 计算缩放比例
        double availableWidth = settings.PageWidth - 40;
        double availableHeight = settings.PageHeight - 100; // 留出页脚空间
        double contentWidth = nodes.Count > 0 ? nodes.Max(n => n.X + n.Width + (n.SpouseNode?.Width ?? 0) + 20) : settings.PageWidth;
        double contentHeight = nodes.Count > 0 ? nodes.Max(n => n.Y + n.Height) : settings.PageHeight;

        double scaleX = availableWidth / Math.Max(contentWidth, 1);
        double scaleY = availableHeight / Math.Max(contentHeight - 60, 1);
        double scale = Math.Min(Math.Min(scaleX, scaleY), 1.0);

        var contentCanvas = new Canvas();
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new ScaleTransform(scale, scale));
        double offsetX = (settings.PageWidth - contentWidth * scale) / 2;
        double offsetY = 60;
        transformGroup.Children.Add(new TranslateTransform(offsetX / scale, offsetY / scale));
        contentCanvas.RenderTransform = transformGroup;

        // 绘制连线（应用节点拖动偏移）
        foreach (var line in lines)
        {
            double px = line.ParentX, py = line.ParentY, cx = line.ChildX, cy = line.ChildY;
            if (positionOffsets != null)
            {
                if (positionOffsets.TryGetValue(line.ParentMemberId, out var po)) { px += po.dx; py += po.dy; }
                if (positionOffsets.TryGetValue(line.ChildMemberId, out var co)) { cx += co.dx; cy += co.dy; }
            }

            if (line.IsSpouseLine)
            {
                var lineShape = new Line
                {
                    X1 = px,
                    Y1 = py - 2,
                    X2 = cx,
                    Y2 = cy - 2,
                    Stroke = new SolidColorBrush(Color.FromRgb(180, 50, 50)),
                    StrokeThickness = 1.5
                };
                contentCanvas.Children.Add(lineShape);

                var lineShape2 = new Line
                {
                    X1 = px,
                    Y1 = py + 2,
                    X2 = cx,
                    Y2 = cy + 2,
                    Stroke = new SolidColorBrush(Color.FromRgb(180, 50, 50)),
                    StrokeThickness = 1.5
                };
                contentCanvas.Children.Add(lineShape2);
            }
            else
            {
                var pathFigure = new PathFigure();
                double midY = (py + cy) / 2;

                var segments = new PathSegmentCollection
                {
                    new LineSegment(new Point(px, midY), true),
                    new LineSegment(new Point(cx, midY), true),
                    new LineSegment(new Point(cx, cy), true)
                };

                pathFigure.StartPoint = new Point(px, py);
                pathFigure.Segments = segments;

                var pathGeometry = new PathGeometry(new[] { pathFigure });
                var pathShape = new Path
                {
                    Data = pathGeometry,
                    Stroke = new SolidColorBrush(Color.FromRgb(100, 70, 40)),
                    StrokeThickness = 1.2
                };
                contentCanvas.Children.Add(pathShape);
            }
        }

        // 绘制节点卡片
        foreach (var node in nodes)
        {
            double nodeX = node.X;
            double nodeY = node.Y;
            if (positionOffsets != null && positionOffsets.TryGetValue(node.Member.Id, out var offset))
            {
                nodeX += offset.dx;
                nodeY += offset.dy;
            }
            var card = CreateNodeCard(node, settings);
            Canvas.SetLeft(card, nodeX);
            Canvas.SetTop(card, nodeY);
            contentCanvas.Children.Add(card);
        }

        canvas.Children.Add(contentCanvas);

        // 绘制页脚
        var footerBlock = new TextBlock
        {
            Text = $"第 {startGeneration} 代 - 第 {pageNumber}/{totalPages} 页",
            FontSize = 10,
            Foreground = new SolidColorBrush(Colors.Gray),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        footerBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(footerBlock, (settings.PageWidth - footerBlock.DesiredSize.Width) / 2);
        Canvas.SetTop(footerBlock, settings.PageHeight - 25);
        canvas.Children.Add(footerBlock);

        page.Children.Add(canvas);
        return page;
    }

    private Border CreateNodeCard(LayoutNode node, LayoutSettings settings)
    {
        var border = new Border
        {
            Width = node.Width,
            Height = node.Height,
            CornerRadius = new CornerRadius(4),
            Background = settings.ShowNodeBorder
                ? (node.Member.Gender == Gender.Male
                    ? new SolidColorBrush(Color.FromRgb(245, 245, 250))
                    : new SolidColorBrush(Color.FromRgb(255, 240, 245)))
                : Brushes.Transparent
        };

        if (settings.ShowNodeBorder)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(120, 80, 40));
            border.BorderThickness = new Thickness(1.2);
        }

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (settings.VerticalText)
        {
            // 竖排文字：每字一行
            var nameBlock = new TextBlock
            {
                Text = string.Join("\n", node.Member.Name.ToCharArray()),
                FontSize = settings.FontSize + 2,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(50, 30, 10)),
                Margin = new Thickness(2, 2, 2, 0)
            };
            Grid.SetRow(nameBlock, 0);
            grid.Children.Add(nameBlock);
        }
        else
        {
            var nameBlock = new TextBlock
            {
                Text = node.Member.Name,
                FontSize = settings.FontSize + 2,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(50, 30, 10)),
                Margin = new Thickness(2, 2, 2, 0)
            };
            Grid.SetRow(nameBlock, 0);
            grid.Children.Add(nameBlock);
        }

        var infoStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 2)
        };

        string lifeText = node.Member.BirthDate;
        if (!string.IsNullOrEmpty(node.Member.DeathDate))
        {
            lifeText += $" - {node.Member.DeathDate}";
        }
        if (!string.IsNullOrEmpty(lifeText))
        {
            var lifeBlock = new TextBlock
            {
                Text = lifeText,
                FontSize = settings.FontSize - 1,
                Foreground = new SolidColorBrush(Colors.DimGray),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };
            infoStack.Children.Add(lifeBlock);
        }

        if (settings.ShowBiography && !string.IsNullOrEmpty(node.Member.Biography))
        {
            var bioText = node.Member.Biography.Length > 30
                ? node.Member.Biography[..30] + "..."
                : node.Member.Biography;
            var bioBlock = new TextBlock
            {
                Text = bioText,
                FontSize = settings.FontSize - 2,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = node.Width - 8
            };
            infoStack.Children.Add(bioBlock);
        }

        Grid.SetRow(infoStack, 1);
        grid.Children.Add(infoStack);
        border.Child = grid;

        return border;
    }
}
