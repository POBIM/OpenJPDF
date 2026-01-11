// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Windows.Media.Imaging;
using OpenJPDF.Models;

namespace OpenJPDF.Services;

public interface IPdfService
{
    /// <summary>
    /// Load a PDF file
    /// </summary>
    Task<bool> LoadPdfAsync(string filePath);

    /// <summary>
    /// Get the total number of pages
    /// </summary>
    int PageCount { get; }

    /// <summary>
    /// Get a page as an image with optional rotation
    /// </summary>
    BitmapSource? GetPageImage(int pageNumber, float scale = 1.0f, int rotation = 0);

    /// <summary>
    /// Get a thumbnail for a page with optional rotation
    /// </summary>
    BitmapSource? GetPageThumbnail(int pageNumber, int rotation = 0);

    /// <summary>
    /// Get the dimensions (width, height) of a PDF page in points
    /// </summary>
    (float width, float height) GetPageDimensions(int pageNumber);

    /// <summary>
    /// Add text to a page
    /// </summary>
    void AddText(TextAnnotation annotation);

    /// <summary>
    /// Add image to a page
    /// </summary>
    void AddImage(ImageAnnotation annotation);

    /// <summary>
    /// Add shape to a page
    /// </summary>
    void AddShape(ShapeAnnotation annotation);

    /// <summary>
    /// Rotate a page
    /// </summary>
    void RotatePage(int pageNumber, int degrees);

    /// <summary>
    /// Get the pending rotation for a page (before save)
    /// </summary>
    int GetPageRotation(int pageNumber);

    /// <summary>
    /// Delete a page
    /// </summary>
    void DeletePage(int pageNumber);

    /// <summary>
    /// Mark a page to be duplicated (inserted after current position)
    /// </summary>
    void DuplicatePage(int pageNumber);

    /// <summary>
    /// Save the PDF
    /// </summary>
    Task<bool> SaveAsync(string filePath);

    /// <summary>
    /// Merge multiple PDFs
    /// </summary>
    Task<bool> MergePdfsAsync(string[] inputFiles, string outputFile);

    /// <summary>
    /// Split PDF into individual pages
    /// </summary>
    Task<bool> SplitPdfAsync(string inputFile, string outputFolder);

    /// <summary>
    /// Extract specific pages from PDF
    /// </summary>
    Task<bool> ExtractPagesAsync(string inputFile, int[] pageNumbers, string outputFile);

    /// <summary>
    /// Clear all annotations (text and image)
    /// </summary>
    void ClearAnnotations();

    /// <summary>
    /// Set the page order for reordering (array of original page indices in new order)
    /// </summary>
    void SetPageOrder(int[] pageOrder);

    /// <summary>
    /// Apply header and footer to PDF pages
    /// </summary>
    Task<bool> ApplyHeaderFooterAsync(string inputFile, string outputFile, HeaderFooterConfig config, string? fileName = null);

    /// <summary>
    /// Create a PDF from an image file
    /// </summary>
    Task<bool> CreatePdfFromImageAsync(string imagePath, string outputPdfPath);

    /// <summary>
    /// Add a redaction area to cover/remove existing content
    /// </summary>
    void AddRedaction(int pageNumber, float x, float y, float width, float height);

    /// <summary>
    /// Add moved text (extracted text that was moved to new position)
    /// </summary>
    void AddMovedText(ExtractedTextElement element);

    /// <summary>
    /// Add moved image (extracted image that was moved to new position)
    /// </summary>
    void AddMovedImage(ExtractedImageElement element);

    /// <summary>
    /// Close and dispose resources
    /// </summary>
    void Close();
}
