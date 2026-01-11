// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

namespace OpenJPDF.Models;

/// <summary>
/// Represents text content extracted from an existing PDF page.
/// Used for the internal extraction process.
/// </summary>
public class ExtractedTextElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;

    // Position in PDF coordinates (points)
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    // Font information (if available)
    public string FontName { get; set; } = string.Empty;
    public float FontSize { get; set; }
    public string? Color { get; set; }

    // Tracking state
    public bool IsDeleted { get; set; } = false;
    public bool IsModified { get; set; } = false;

    // Original position for undo
    public float OriginalX { get; set; }
    public float OriginalY { get; set; }
    public string OriginalText { get; set; } = string.Empty;
}
