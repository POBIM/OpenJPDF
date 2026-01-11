// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

namespace OpenJPDF.Models;

/// <summary>
/// Represents an image extracted from an existing PDF page.
/// Used for the internal extraction process.
/// </summary>
public class ExtractedImageElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int PageNumber { get; set; }

    // Position in PDF coordinates (points)
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    // Image data
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
    public string Format { get; set; } = "png";

    // Tracking state
    public bool IsDeleted { get; set; } = false;
    public bool IsModified { get; set; } = false;

    // Original position for undo
    public float OriginalX { get; set; }
    public float OriginalY { get; set; }
    public float OriginalWidth { get; set; }
    public float OriginalHeight { get; set; }
}
