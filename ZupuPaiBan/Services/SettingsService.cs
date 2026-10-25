using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZupuPaiBan.Models;
using ZupuPaiBan.Infrastructure;

namespace ZupuPaiBan.Services;

/// <summary>
/// 保存/加载最后一次排版设置到 JSON 文件
/// </summary>
public class SettingsService
{
    private static readonly string _settingsPath = Path.Combine(AppDataPaths.RootDirectory, "layout-settings.json");

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private class SettingsSnapshot
    {
        public PageSizeOption PageSize { get; set; } = PageSizeOption.A3;
        public PageOrientation Orientation { get; set; } = PageOrientation.Landscape;
        public LayoutMode LayoutMode { get; set; } = LayoutMode.Horizontal;
        public double NodeWidth { get; set; } = 140;
        public double NodeHeight { get; set; } = 100;
        public double HorizontalSpacing { get; set; } = 30;
        public double VerticalSpacing { get; set; } = 80;
        public double FontSize { get; set; } = 12;
        public string FontFamily { get; set; } = "宋体";
        public bool ShowPhoto { get; set; } = true;
        public bool ShowSpouse { get; set; } = true;
        public bool ShowBiography { get; set; }
        public bool ShowNodeBorder { get; set; } = true;
        public bool VerticalText { get; set; }
        public string Title { get; set; } = "族谱";
        public double TitleFontSize { get; set; } = 28;
    }

    public LayoutSettings Load()
    {
        var settings = new LayoutSettings();
        try
        {
            if (!File.Exists(_settingsPath)) return settings;
            var json = File.ReadAllText(_settingsPath);
            var snap = JsonSerializer.Deserialize<SettingsSnapshot>(json);
            if (snap == null) return settings;

            settings.PageSize = snap.PageSize;
            settings.Orientation = snap.Orientation;
            settings.LayoutMode = snap.LayoutMode;
            settings.NodeWidth = snap.NodeWidth;
            settings.NodeHeight = snap.NodeHeight;
            settings.HorizontalSpacing = snap.HorizontalSpacing;
            settings.VerticalSpacing = snap.VerticalSpacing;
            settings.FontSize = snap.FontSize;
            settings.FontFamily = snap.FontFamily;
            settings.ShowPhoto = snap.ShowPhoto;
            settings.ShowSpouse = snap.ShowSpouse;
            settings.ShowBiography = snap.ShowBiography;
            settings.ShowNodeBorder = snap.ShowNodeBorder;
            settings.VerticalText = snap.VerticalText;
            settings.Title = snap.Title;
            settings.TitleFontSize = snap.TitleFontSize;
        }
        catch
        {
            // 读取失败时使用默认设置
        }
        return settings;
    }

    public void Save(LayoutSettings settings)
    {
        try
        {
            var snap = new SettingsSnapshot
            {
                PageSize = settings.PageSize,
                Orientation = settings.Orientation,
                LayoutMode = settings.LayoutMode,
                NodeWidth = settings.NodeWidth,
                NodeHeight = settings.NodeHeight,
                HorizontalSpacing = settings.HorizontalSpacing,
                VerticalSpacing = settings.VerticalSpacing,
                FontSize = settings.FontSize,
                FontFamily = settings.FontFamily,
                ShowPhoto = settings.ShowPhoto,
                ShowSpouse = settings.ShowSpouse,
                ShowBiography = settings.ShowBiography,
                ShowNodeBorder = settings.ShowNodeBorder,
                VerticalText = settings.VerticalText,
                Title = settings.Title,
                TitleFontSize = settings.TitleFontSize
            };

            var dir = Path.GetDirectoryName(_settingsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(snap, _options);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // 保存失败时忽略
        }
    }
}
