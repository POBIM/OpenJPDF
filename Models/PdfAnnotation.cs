// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

namespace OpenJPDF.Models;

/// <summary>
/// Base class for PDF annotations that can be previewed before saving
/// </summary>
public abstract class PdfAnnotation
{
    public int PageNumber { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsSaved { get; set; } = false;
}
