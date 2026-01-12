// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenJPDF.Models;

/// <summary>
/// Base class for annotations that can be displayed in the sidebar and selected
/// </summary>
public abstract partial class AnnotationItem : ObservableObject
{
    [ObservableProperty]
    private int pageNumber;

    [ObservableProperty]
    private double x;

    [ObservableProperty]
    private double y;

    [ObservableProperty]
    private bool isSelected;

    /// <summary>
    /// Rotation angle in degrees (0-360)
    /// </summary>
    [ObservableProperty]
    private double rotation;

    public abstract string DisplayName { get; }
    public abstract string IconEmoji { get; }
}

/// <summary>
/// Text annotation item for sidebar display
/// </summary>
public partial class TextAnnotationItem : AnnotationItem
{
    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private string fontFamily = "Arial";

    [ObservableProperty]
    private float fontSize = 12f;

    [ObservableProperty]
    private string color = "#000000";

    [ObservableProperty]
    private string backgroundColor = "Transparent";

    [ObservableProperty]
    private string borderColor = "Transparent";

    [ObservableProperty]
    private float borderWidth = 0f;

    [ObservableProperty]
    private bool isBold = false;

    [ObservableProperty]
    private bool isItalic = false;

    [ObservableProperty]
    private bool isUnderline = false;

    [ObservableProperty]
    private TextAlignment textAlignment = TextAlignment.Left;

    // Measured dimensions (screen pixels, includes padding)
    [ObservableProperty]
    private double width;

    [ObservableProperty]
    private double height;

    public override string DisplayName => Text.Length > 20 ? Text[..20] + "..." : Text;
    public override string IconEmoji => "📝";

    public TextAnnotation ToAnnotation() => new()
    {
        PageNumber = PageNumber,
        X = X,
        Y = Y,
        Text = Text,
        FontFamily = FontFamily,
        FontSize = FontSize,
        Color = Color,
        BackgroundColor = BackgroundColor,
        BorderColor = BorderColor,
        BorderWidth = BorderWidth,
        IsBold = IsBold,
        IsItalic = IsItalic,
        IsUnderline = IsUnderline,
        TextAlignment = TextAlignment,
        Width = Width,
        Height = Height,
        Rotation = Rotation
    };

    public static TextAnnotationItem FromAnnotation(TextAnnotation ann) => new()
    {
        PageNumber = ann.PageNumber,
        X = ann.X,
        Y = ann.Y,
        Text = ann.Text,
        FontFamily = ann.FontFamily,
        FontSize = ann.FontSize,
        Color = ann.Color,
        BackgroundColor = ann.BackgroundColor,
        BorderColor = ann.BorderColor,
        BorderWidth = ann.BorderWidth,
        IsBold = ann.IsBold,
        IsItalic = ann.IsItalic,
        IsUnderline = ann.IsUnderline,
        TextAlignment = ann.TextAlignment,
        Width = ann.Width,
        Height = ann.Height,
        Rotation = ann.Rotation
    };
}

/// <summary>
/// Image annotation item for sidebar display
/// </summary>
public partial class ImageAnnotationItem : AnnotationItem
{
    [ObservableProperty]
    private double width = 200;

    [ObservableProperty]
    private double height = 200;

    [ObservableProperty]
    private string imagePath = string.Empty;

    public override string DisplayName => System.IO.Path.GetFileName(ImagePath);
    public override string IconEmoji => "🖼️";

    public ImageAnnotation ToAnnotation() => new()
    {
        PageNumber = PageNumber,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        ImagePath = ImagePath,
        Rotation = Rotation
    };

    public static ImageAnnotationItem FromAnnotation(ImageAnnotation ann) => new()
    {
        PageNumber = ann.PageNumber,
        X = ann.X,
        Y = ann.Y,
        Width = ann.Width,
        Height = ann.Height,
        ImagePath = ann.ImagePath,
        Rotation = ann.Rotation
    };
}

/// <summary>
/// Shape annotation item for sidebar display
/// </summary>
public partial class ShapeAnnotationItem : AnnotationItem
{
    [ObservableProperty]
    private double width = 100;

    [ObservableProperty]
    private double height = 50;

    [ObservableProperty]
    private ShapeType shapeType = ShapeType.Rectangle;

    [ObservableProperty]
    private string fillColor = "Transparent";

    [ObservableProperty]
    private string strokeColor = "#000000";

    [ObservableProperty]
    private float strokeWidth = 1f;

    // For Line shape
    [ObservableProperty]
    private double x2;

    [ObservableProperty]
    private double y2;

    public override string DisplayName => ShapeType.ToString();
    public override string IconEmoji => ShapeType switch
    {
        ShapeType.Rectangle => "▭",
        ShapeType.Ellipse => "⬭",
        ShapeType.Line => "╱",
        _ => "▭"
    };

    public ShapeAnnotation ToAnnotation() => new()
    {
        PageNumber = PageNumber,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        ShapeType = ShapeType,
        FillColor = FillColor,
        StrokeColor = StrokeColor,
        StrokeWidth = StrokeWidth,
        X2 = X2,
        Y2 = Y2
    };

    public static ShapeAnnotationItem FromAnnotation(ShapeAnnotation ann) => new()
    {
        PageNumber = ann.PageNumber,
        X = ann.X,
        Y = ann.Y,
        Width = ann.Width,
        Height = ann.Height,
        ShapeType = ann.ShapeType,
        FillColor = ann.FillColor,
        StrokeColor = ann.StrokeColor,
        StrokeWidth = ann.StrokeWidth,
        X2 = ann.X2,
        Y2 = ann.Y2
    };
}

/// <summary>
/// Extracted text item from existing PDF content - for UI display
/// </summary>
public partial class ExtractedTextItem : AnnotationItem
{
    [ObservableProperty]
    private Guid elementId;

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private string fontName = string.Empty;

    [ObservableProperty]
    private float fontSize = 12f;

    [ObservableProperty]
    private string? color;

    [ObservableProperty]
    private double width;

    [ObservableProperty]
    private double height;

    [ObservableProperty]
    private bool isDeleted = false;

    [ObservableProperty]
    private bool isModified = false;

    // Flag to identify this is from original PDF
    public bool IsFromOriginalPdf => true;

    public override string DisplayName => Text.Length > 20 ? Text[..20] + "..." : Text;
    public override string IconEmoji => "📄";

    public static ExtractedTextItem FromElement(ExtractedTextElement elem) => new()
    {
        ElementId = elem.Id,
        PageNumber = elem.PageNumber,
        X = elem.X,
        Y = elem.Y,
        Width = elem.Width,
        Height = elem.Height,
        Text = elem.Text,
        FontName = elem.FontName,
        FontSize = elem.FontSize,
        Color = elem.Color,
        IsDeleted = elem.IsDeleted,
        IsModified = elem.IsModified
    };

    public ExtractedTextElement ToElement() => new()
    {
        Id = ElementId,
        PageNumber = PageNumber,
        X = (float)X,
        Y = (float)Y,
        Width = (float)Width,
        Height = (float)Height,
        Text = Text,
        FontName = FontName,
        FontSize = FontSize,
        Color = Color,
        IsDeleted = IsDeleted,
        IsModified = IsModified
    };
}

/// <summary>
/// Extracted image item from existing PDF content - for UI display
/// </summary>
public partial class ExtractedImageItem : AnnotationItem
{
    [ObservableProperty]
    private Guid elementId;

    [ObservableProperty]
    private double width = 200;

    [ObservableProperty]
    private double height = 200;

    [ObservableProperty]
    private byte[] imageBytes = Array.Empty<byte>();

    [ObservableProperty]
    private string format = "png";

    [ObservableProperty]
    private bool isDeleted = false;

    [ObservableProperty]
    private bool isModified = false;

    // Flag to identify this is from original PDF
    public bool IsFromOriginalPdf => true;

    public override string DisplayName => $"Image ({Width:F0}x{Height:F0})";
    public override string IconEmoji => "🖼️";

    public static ExtractedImageItem FromElement(ExtractedImageElement elem) => new()
    {
        ElementId = elem.Id,
        PageNumber = elem.PageNumber,
        X = elem.X,
        Y = elem.Y,
        Width = elem.Width,
        Height = elem.Height,
        ImageBytes = elem.ImageBytes,
        Format = elem.Format,
        IsDeleted = elem.IsDeleted,
        IsModified = elem.IsModified
    };

    public ExtractedImageElement ToElement() => new()
    {
        Id = ElementId,
        PageNumber = PageNumber,
        X = (float)X,
        Y = (float)Y,
        Width = (float)Width,
        Height = (float)Height,
        ImageBytes = ImageBytes,
        Format = Format,
        IsDeleted = IsDeleted,
        IsModified = IsModified
    };
}
