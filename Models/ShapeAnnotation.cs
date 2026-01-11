// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

namespace OpenJPDF.Models;

public enum ShapeType
{
    Rectangle,
    Ellipse,
    Line
}

public class ShapeAnnotation
{
    public int PageNumber { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 50;
    public ShapeType ShapeType { get; set; } = ShapeType.Rectangle;
    public string FillColor { get; set; } = "Transparent";
    public string StrokeColor { get; set; } = "#000000";
    public float StrokeWidth { get; set; } = 1f;
    
    // For Line shape
    public double X2 { get; set; }
    public double Y2 { get; set; }
}
