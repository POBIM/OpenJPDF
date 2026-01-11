// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Windows;
using System.Windows.Controls;
using OpenJPDF.Models;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using ModelTextAlignment = OpenJPDF.Models.TextAlignment;

namespace OpenJPDF.Views;

public partial class TextInputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;
    public new float FontSize { get; private set; } = 12f;
    public new string FontFamily { get; private set; } = "Arial";
    public string TextColor { get; private set; } = "#000000";
    public string BackgroundColor { get; private set; } = "Transparent";
    public string BorderColor { get; private set; } = "Transparent";
    public float BorderWidth { get; private set; } = 0f;
    public bool IsBold { get; private set; } = false;
    public bool IsItalic { get; private set; } = false;
    public bool IsUnderline { get; private set; } = false;
    public ModelTextAlignment TextAlignment { get; private set; } = ModelTextAlignment.Left;

    public TextInputDialog()
    {
        InitializeComponent();
        TextInputBox.Focus();

        // Subscribe to change events for live preview
        TextInputBox.TextChanged += (s, e) => UpdatePreview();
        FontFamilyCombo.SelectionChanged += (s, e) => UpdatePreview();
        FontSizeCombo.SelectionChanged += (s, e) => UpdatePreview();
        TextColorCombo.SelectionChanged += (s, e) => UpdatePreview();
        BackgroundColorCombo.SelectionChanged += (s, e) => UpdatePreview();
        BorderColorCombo.SelectionChanged += (s, e) => UpdatePreview();
        BorderWidthCombo.SelectionChanged += (s, e) => UpdatePreview();
        BoldCheck.Checked += (s, e) => UpdatePreview();
        BoldCheck.Unchecked += (s, e) => UpdatePreview();
        ItalicCheck.Checked += (s, e) => UpdatePreview();
        ItalicCheck.Unchecked += (s, e) => UpdatePreview();
        UnderlineCheck.Checked += (s, e) => UpdatePreview();
        UnderlineCheck.Unchecked += (s, e) => UpdatePreview();
        AlignLeftRadio.Checked += (s, e) => UpdatePreview();
        AlignCenterRadio.Checked += (s, e) => UpdatePreview();
        AlignRightRadio.Checked += (s, e) => UpdatePreview();

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewText == null || PreviewBorder == null) return;

        // Text
        PreviewText.Text = string.IsNullOrEmpty(TextInputBox.Text) ? "Preview text" : TextInputBox.Text;

        // Font Family
        if (FontFamilyCombo.SelectedItem is ComboBoxItem fontItem)
        {
            PreviewText.FontFamily = new WpfFontFamily(fontItem.Content.ToString() ?? "Arial");
        }

        // Font Size
        if (FontSizeCombo.SelectedItem is ComboBoxItem sizeItem && 
            float.TryParse(sizeItem.Content.ToString(), out float size))
        {
            PreviewText.FontSize = size;
        }

        // Text Color
        if (TextColorCombo.SelectedItem is ComboBoxItem colorItem)
        {
            try
            {
                var color = (WpfColor)WpfColorConverter.ConvertFromString(colorItem.Tag?.ToString() ?? "Black");
                PreviewText.Foreground = new WpfSolidColorBrush(color);
            }
            catch { }
        }

        // Background Color
        if (BackgroundColorCombo.SelectedItem is ComboBoxItem bgItem)
        {
            try
            {
                var bgColor = bgItem.Tag?.ToString() ?? "Transparent";
                if (bgColor == "Transparent")
                {
                    PreviewBorder.Background = WpfBrushes.White;
                }
                else
                {
                    var color = (WpfColor)WpfColorConverter.ConvertFromString(bgColor);
                    PreviewBorder.Background = new WpfSolidColorBrush(color);
                }
            }
            catch { }
        }

        // Border Color & Width
        if (BorderColorCombo.SelectedItem is ComboBoxItem borderItem &&
            BorderWidthCombo.SelectedItem is ComboBoxItem widthItem)
        {
            try
            {
                var borderColor = borderItem.Tag?.ToString() ?? "Transparent";
                if (borderColor == "Transparent" || !float.TryParse(widthItem.Content.ToString(), out float bw) || bw == 0)
                {
                    PreviewBorder.BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(224, 224, 224));
                    PreviewBorder.BorderThickness = new Thickness(1);
                }
                else
                {
                    var color = (WpfColor)WpfColorConverter.ConvertFromString(borderColor);
                    PreviewBorder.BorderBrush = new WpfSolidColorBrush(color);
                    PreviewBorder.BorderThickness = new Thickness(bw);
                }
            }
            catch { }
        }

        // Bold
        PreviewText.FontWeight = BoldCheck.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;

        // Italic
        PreviewText.FontStyle = ItalicCheck.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;

        // Underline
        PreviewText.TextDecorations = UnderlineCheck.IsChecked == true ? TextDecorations.Underline : null;

        // Alignment
        if (AlignCenterRadio.IsChecked == true)
            PreviewText.TextAlignment = System.Windows.TextAlignment.Center;
        else if (AlignRightRadio.IsChecked == true)
            PreviewText.TextAlignment = System.Windows.TextAlignment.Right;
        else
            PreviewText.TextAlignment = System.Windows.TextAlignment.Left;
    }

    private static readonly Dictionary<string, string> ColorNameToHex = new()
    {
        { "Black", "#000000" },
        { "Red", "#FF0000" },
        { "Blue", "#0000FF" },
        { "Green", "#008000" },
        { "Orange", "#FFA500" },
        { "Purple", "#800080" },
        { "Gray", "#808080" },
        { "White", "#FFFFFF" }
    };

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        InputText = TextInputBox.Text;

        // Font Size
        if (FontSizeCombo.SelectedItem is ComboBoxItem sizeItem &&
            float.TryParse(sizeItem.Content.ToString(), out float size))
        {
            FontSize = size;
        }

        // Font Family
        if (FontFamilyCombo.SelectedItem is ComboBoxItem fontItem)
        {
            FontFamily = fontItem.Content.ToString() ?? "Arial";
        }

        // Text Color
        if (TextColorCombo.SelectedItem is ComboBoxItem colorItem)
        {
            var colorName = colorItem.Content.ToString() ?? "Black";
            TextColor = ColorNameToHex.GetValueOrDefault(colorName, "#000000");
        }

        // Background Color
        if (BackgroundColorCombo.SelectedItem is ComboBoxItem bgItem)
        {
            var bgTag = bgItem.Tag?.ToString() ?? "Transparent";
            BackgroundColor = bgTag;
        }

        // Border Color
        if (BorderColorCombo.SelectedItem is ComboBoxItem borderItem)
        {
            var borderTag = borderItem.Tag?.ToString() ?? "Transparent";
            BorderColor = borderTag;
        }

        // Border Width
        if (BorderWidthCombo.SelectedItem is ComboBoxItem widthItem &&
            float.TryParse(widthItem.Content.ToString(), out float bw))
        {
            BorderWidth = bw;
        }

        // Text Style
        IsBold = BoldCheck.IsChecked == true;
        IsItalic = ItalicCheck.IsChecked == true;
        IsUnderline = UnderlineCheck.IsChecked == true;

        // Text Alignment
        if (AlignCenterRadio.IsChecked == true)
            TextAlignment = ModelTextAlignment.Center;
        else if (AlignRightRadio.IsChecked == true)
            TextAlignment = ModelTextAlignment.Right;
        else
            TextAlignment = ModelTextAlignment.Left;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
