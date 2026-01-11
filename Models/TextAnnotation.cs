// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

namespace OpenJPDF.Models;

public enum TextAlignment
{
    Left,
    Center,
    Right
}

public class TextAnnotation
{
    public int PageNumber { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Arial";
    public float FontSize { get; set; } = 12f;
    public string Color { get; set; } = "#000000";
    
    // Measured dimensions from WPF (screen pixels, includes padding)
    public double Width { get; set; }
    public double Height { get; set; }
    
    // New text formatting properties
    public string BackgroundColor { get; set; } = "Transparent";
    public string BorderColor { get; set; } = "Transparent";
    public float BorderWidth { get; set; } = 0f;
    public bool IsBold { get; set; } = false;
    public bool IsItalic { get; set; } = false;
    public bool IsUnderline { get; set; } = false;
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
}
