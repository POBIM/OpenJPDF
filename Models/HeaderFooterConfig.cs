// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenJPDF.Models;

/// <summary>
/// Position alignment for header/footer elements
/// </summary>
public enum HorizontalPosition
{
    Left,
    Center,
    Right
}

/// <summary>
/// Represents a single element in header or footer (text or image)
/// </summary>
public partial class HeaderFooterElement : ObservableObject
{
    [ObservableProperty]
    private bool isEnabled = true;

    [ObservableProperty]
    private string text = "";

    [ObservableProperty]
    private string? imagePath;

    [ObservableProperty]
    private bool isImage;

    [ObservableProperty]
    private HorizontalPosition position = HorizontalPosition.Center;

    [ObservableProperty]
    private string fontFamily = "Arial";

    [ObservableProperty]
    private float fontSize = 10f;

    [ObservableProperty]
    private string color = "#000000";

    [ObservableProperty]
    private bool isBold;

    [ObservableProperty]
    private bool isItalic;

    [ObservableProperty]
    private double imageWidth = 100;

    [ObservableProperty]
    private double imageHeight = 30;

    /// <summary>
    /// Special placeholders that can be used in text:
    /// {page} - Current page number
    /// {total} - Total page count
    /// {date} - Current date
    /// {filename} - File name
    /// </summary>
    public string GetFormattedText(int currentPage, int totalPages, string fileName, DateTime date)
    {
        if (IsImage || string.IsNullOrEmpty(Text))
            return Text;

        return Text
            .Replace("{page}", currentPage.ToString())
            .Replace("{total}", totalPages.ToString())
            .Replace("{date}", date.ToString("yyyy-MM-dd"))
            .Replace("{filename}", System.IO.Path.GetFileName(fileName));
    }
}

/// <summary>
/// Custom text box that can be positioned anywhere on the page
/// Useful for signature boxes, custom labels, etc.
/// </summary>
public partial class CustomTextBox : ObservableObject
{
    [ObservableProperty]
    private string text = "";

    [ObservableProperty]
    private string label = "Text Box";

    /// <summary>
    /// X offset from left edge of page (in points, 72 points = 1 inch)
    /// </summary>
    [ObservableProperty]
    private float offsetX = 50f;

    /// <summary>
    /// Y offset from bottom of page (in points, 72 points = 1 inch)
    /// </summary>
    [ObservableProperty]
    private float offsetY = 100f;

    [ObservableProperty]
    private string fontFamily = "Arial";

    [ObservableProperty]
    private float fontSize = 10f;

    [ObservableProperty]
    private string color = "#000000";

    [ObservableProperty]
    private bool isBold;

    [ObservableProperty]
    private bool isItalic;

    /// <summary>
    /// Optional: draw a border around the text box
    /// </summary>
    [ObservableProperty]
    private bool showBorder;

    /// <summary>
    /// Width of the text box (for border, 0 = auto)
    /// </summary>
    [ObservableProperty]
    private float boxWidth = 150f;

    /// <summary>
    /// Height of the text box (for border, 0 = auto)
    /// </summary>
    [ObservableProperty]
    private float boxHeight = 20f;

    /// <summary>
    /// Get formatted text with placeholder replacement
    /// </summary>
    public string GetFormattedText(int currentPage, int totalPages, string fileName, DateTime date)
    {
        if (string.IsNullOrEmpty(Text))
            return Text;

        return Text
            .Replace("{page}", currentPage.ToString())
            .Replace("{total}", totalPages.ToString())
            .Replace("{date}", date.ToString("yyyy-MM-dd"))
            .Replace("{filename}", System.IO.Path.GetFileName(fileName));
    }
}

/// <summary>
/// Configuration for document header and footer
/// </summary>
public partial class HeaderFooterConfig : ObservableObject
{
    #region Header Configuration

    [ObservableProperty]
    private bool headerEnabled;

    [ObservableProperty]
    private HeaderFooterElement headerLeft = new() { Position = HorizontalPosition.Left };

    [ObservableProperty]
    private HeaderFooterElement headerCenter = new() { Position = HorizontalPosition.Center };

    [ObservableProperty]
    private HeaderFooterElement headerRight = new() { Position = HorizontalPosition.Right };

    /// <summary>
    /// Margin from top of page (in points)
    /// </summary>
    [ObservableProperty]
    private float headerMargin = 30f;

    #endregion

    #region Footer Configuration

    [ObservableProperty]
    private bool footerEnabled;

    [ObservableProperty]
    private HeaderFooterElement footerLeft = new() { Position = HorizontalPosition.Left };

    [ObservableProperty]
    private HeaderFooterElement footerCenter = new() { Position = HorizontalPosition.Center, Text = "Page {page} of {total}" };

    [ObservableProperty]
    private HeaderFooterElement footerRight = new() { Position = HorizontalPosition.Right };

    /// <summary>
    /// Margin from bottom of page (in points)
    /// </summary>
    [ObservableProperty]
    private float footerMargin = 30f;

    #endregion

    #region Page Range

    [ObservableProperty]
    private bool applyToAllPages = true;

    [ObservableProperty]
    private int startPage = 1;

    [ObservableProperty]
    private int endPage = 1;

    /// <summary>
    /// Skip first page (useful for cover pages)
    /// </summary>
    [ObservableProperty]
    private bool skipFirstPage;

    #endregion

    #region Custom Text Boxes

    /// <summary>
    /// Custom text boxes with free positioning (for signatures, labels, etc.)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<CustomTextBox> customTextBoxes = new();

    #endregion

    /// <summary>
    /// Check if header/footer should be applied to a specific page
    /// </summary>
    public bool ShouldApplyToPage(int pageNumber, int totalPages)
    {
        if (SkipFirstPage && pageNumber == 1)
            return false;

        if (ApplyToAllPages)
            return true;

        return pageNumber >= StartPage && pageNumber <= Math.Min(EndPage, totalPages);
    }

    /// <summary>
    /// Create a default configuration with page numbers in footer
    /// </summary>
    public static HeaderFooterConfig CreateDefault()
    {
        return new HeaderFooterConfig
        {
            HeaderEnabled = false,
            FooterEnabled = true,
            FooterCenter = new HeaderFooterElement
            {
                IsEnabled = true,
                Text = "Page {page} of {total}",
                Position = HorizontalPosition.Center,
                FontSize = 10f
            }
        };
    }
}
