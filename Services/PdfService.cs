// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Runtime.InteropServices;
using iText.Kernel.Pdf;
using PdfRenderOptions = PDFtoImage.RenderOptions;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Utils;
using iText.Kernel.Colors;
using OpenJPDF.Models;
using PDFtoImage;
using SkiaSharp;
using IoPath = System.IO.Path;
using IoFile = System.IO.File;
using IoDirectory = System.IO.Directory;
using ITextRectangle = iText.Kernel.Geom.Rectangle;

namespace OpenJPDF.Services;

public class PdfService : IPdfService, IDisposable
{
    private string? _currentFilePath;
    private int _pageCount;
    private readonly List<TextAnnotation> _textAnnotations = new();
    private readonly List<ImageAnnotation> _imageAnnotations = new();
    private readonly List<ShapeAnnotation> _shapeAnnotations = new();
    private readonly Dictionary<int, int> _pageRotations = new();
    private readonly HashSet<int> _deletedPages = new();
    private readonly List<int> _duplicatedPages = new(); // Pages to duplicate (original index)
    private int[]? _pageOrder; // New page order (original indices in new order)
    private bool _disposed;

    // Redaction and moved content tracking
    private readonly List<(int PageNumber, float X, float Y, float Width, float Height)> _redactions = new();
    private readonly List<ExtractedTextElement> _movedTexts = new();
    private readonly List<ExtractedImageElement> _movedImages = new();

    // Performance optimization: Keep file bytes in memory for faster rendering
    private byte[]? _pdfBytes;
    
    // LRU caches for rendered pages and thumbnails
    private readonly PageCache _pageCache = new(20);      // Full-size page cache
    private readonly PageCache _thumbnailCache = new(100); // Thumbnail cache (smaller images = more can fit)
    
    // Static cache for available system fonts (pre-populated on first use)
    private static readonly Lazy<HashSet<string>> _availableSystemFonts = new(() =>
    {
        var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (IoDirectory.Exists(fontsFolder))
            {
                foreach (var file in IoDirectory.GetFiles(fontsFolder, "*.ttf"))
                    fonts.Add(IoPath.GetFileName(file));
                foreach (var file in IoDirectory.GetFiles(fontsFolder, "*.ttc"))
                    fonts.Add(IoPath.GetFileName(file));
                foreach (var file in IoDirectory.GetFiles(fontsFolder, "*.otf"))
                    fonts.Add(IoPath.GetFileName(file));
            }
            System.Diagnostics.Debug.WriteLine($"[PERF] Cached {fonts.Count} system font files");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PERF] Failed to cache system fonts: {ex.Message}");
        }
        return fonts;
    });

    public int PageCount => _pageCount;

    public async Task<bool> LoadPdfAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                Close();
                
                // Load PDF bytes into memory for faster rendering
                _pdfBytes = IoFile.ReadAllBytes(filePath);
                
                // Get page count using iText
                using var memStream = new MemoryStream(_pdfBytes);
                using var reader = new PdfReader(memStream);
                using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
                _pageCount = pdfDoc.GetNumberOfPages();
                
                _currentFilePath = filePath;
                _textAnnotations.Clear();
                _imageAnnotations.Clear();
                _shapeAnnotations.Clear();
                _pageRotations.Clear();
                _deletedPages.Clear();
                _duplicatedPages.Clear();
                _redactions.Clear();
                _movedTexts.Clear();
                _movedImages.Clear();
                _pageOrder = null;
                
                // Clear caches for new document
                _pageCache.Clear();
                _thumbnailCache.Clear();
                
                System.Diagnostics.Debug.WriteLine($"[PERF] Loaded PDF into memory: {_pdfBytes.Length / 1024}KB, {_pageCount} pages");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading PDF: {ex.Message}");
                return false;
            }
        });
    }

    public BitmapSource? GetPageImage(int pageNumber, float scale = 1.0f, int rotation = 0)
    {
        if (_pdfBytes == null || pageNumber < 0 || pageNumber >= _pageCount)
            return null;

        // Check cache first
        string cacheKey = PageCache.GetCacheKey(pageNumber, scale, rotation);
        if (_pageCache.TryGet(cacheKey, out var cachedImage))
        {
            System.Diagnostics.Debug.WriteLine($"[CACHE HIT] Page {pageNumber} (scale={scale}, rot={rotation})");
            return cachedImage;
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // Calculate DPI based on scale (96 DPI is standard, so 96 * scale)
            int dpi = (int)(96 * scale);
            
            // Use memory stream from cached bytes (much faster than file I/O)
            using var memStream = new MemoryStream(_pdfBytes, writable: false);
            
            // PDFtoImage automatically applies inherent PDF rotation
            // Only apply USER rotation here (not inherent rotation)
            var pdfRotation = rotation switch
            {
                90 => PdfRotation.Rotate90,
                180 => PdfRotation.Rotate180,
                270 => PdfRotation.Rotate270,
                _ => PdfRotation.Rotate0
            };
            
            var options = new PdfRenderOptions
            {
                Dpi = dpi,
                Rotation = pdfRotation
            };
            
            // Render the page (ViewModel already provides the original page index)
            using var skBitmap = Conversion.ToImage(memStream, pageNumber, options: options);
            
            if (skBitmap == null)
            {
                return null;
            }
            
            // Use optimized direct conversion (skip PNG encoding)
            var result = ConvertSkiaBitmapToWpfDirect(skBitmap);
            
            // Cache the result
            if (result != null)
            {
                _pageCache.Set(cacheKey, result);
            }
            
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] Rendered page {pageNumber}: {skBitmap.Width}x{skBitmap.Height} in {sw.ElapsedMilliseconds}ms");
            
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR rendering page {pageNumber}: {ex.GetType().Name}: {ex.Message}");
            
            // Try a fallback approach with default settings
            try
            {
                using var memStream = new MemoryStream(_pdfBytes, writable: false);
                using var skBitmap = Conversion.ToImage(memStream, pageNumber);
                
                if (skBitmap != null)
                {
                    var result = ConvertSkiaBitmapToWpfDirect(skBitmap);
                    if (result != null)
                    {
                        _pageCache.Set(cacheKey, result);
                    }
                    return result;
                }
            }
            catch (Exception fallbackEx)
            {
                System.Diagnostics.Debug.WriteLine($"Fallback also failed for page {pageNumber}: {fallbackEx.Message}");
            }
            
            return null;
        }
    }

    public BitmapSource? GetPageThumbnail(int pageNumber, int rotation = 0)
    {
        if (_pdfBytes == null || pageNumber < 0 || pageNumber >= _pageCount)
            return null;

        // Check cache first
        string cacheKey = PageCache.GetThumbnailKey(pageNumber, rotation);
        if (_thumbnailCache.TryGet(cacheKey, out var cachedImage))
        {
            return cachedImage;
        }

        try
        {
            // Use memory stream from cached bytes (much faster than file I/O)
            using var memStream = new MemoryStream(_pdfBytes, writable: false);
            
            // PDFtoImage automatically applies inherent PDF rotation
            // Only apply USER rotation here (not inherent rotation)
            var pdfRotation = rotation switch
            {
                90 => PdfRotation.Rotate90,
                180 => PdfRotation.Rotate180,
                270 => PdfRotation.Rotate270,
                _ => PdfRotation.Rotate0
            };
            
            // Use low DPI to create small thumbnail while preserving aspect ratio
            var options = new PdfRenderOptions
            {
                Dpi = 24,
                Rotation = pdfRotation
            };
            
            using var skBitmap = Conversion.ToImage(memStream, pageNumber, options: options);
            
            if (skBitmap == null)
            {
                return null;
            }
            
            // Use optimized direct conversion
            var result = ConvertSkiaBitmapToWpfDirect(skBitmap);
            
            // Cache the result
            if (result != null)
            {
                _thumbnailCache.Set(cacheKey, result);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR rendering thumbnail page {pageNumber}: {ex.GetType().Name}: {ex.Message}");
            
            // Try a fallback approach with default settings
            try
            {
                using var memStream = new MemoryStream(_pdfBytes, writable: false);
                using var skBitmap = Conversion.ToImage(memStream, pageNumber);
                
                if (skBitmap != null)
                {
                    var result = ConvertSkiaBitmapToWpfDirect(skBitmap);
                    if (result != null)
                    {
                        _thumbnailCache.Set(cacheKey, result);
                    }
                    return result;
                }
            }
            catch (Exception fallbackEx)
            {
                System.Diagnostics.Debug.WriteLine($"Fallback thumbnail also failed for page {pageNumber}: {fallbackEx.Message}");
            }
            
            return null;
        }
    }

    /// <summary>
    /// Legacy conversion method using PNG encoding (slower but safer fallback).
    /// </summary>
    private static BitmapSource ConvertSkiaBitmapToWpf(SKBitmap skBitmap)
    {
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        
        var bitmap = new BitmapImage();
        using var stream = data.AsStream();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        
        return bitmap;
    }

    /// <summary>
    /// Optimized direct conversion from SkiaSharp to WPF bitmap.
    /// Bypasses PNG encoding for ~3x faster performance.
    /// </summary>
    private static BitmapSource ConvertSkiaBitmapToWpfDirect(SKBitmap skBitmap)
    {
        try
        {
            // Ensure the bitmap is in a format we can work with
            if (skBitmap.ColorType != SKColorType.Bgra8888)
            {
                using var convertedBitmap = new SKBitmap(skBitmap.Width, skBitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                if (!skBitmap.CopyTo(convertedBitmap))
                {
                    // Fallback to PNG method if conversion fails
                    return ConvertSkiaBitmapToWpf(skBitmap);
                }
                return CreateWriteableBitmap(convertedBitmap);
            }
            
            return CreateWriteableBitmap(skBitmap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PERF] Direct conversion failed, using PNG fallback: {ex.Message}");
            return ConvertSkiaBitmapToWpf(skBitmap);
        }
    }

    private static WriteableBitmap CreateWriteableBitmap(SKBitmap skBitmap)
    {
        var writeableBitmap = new WriteableBitmap(
            skBitmap.Width,
            skBitmap.Height,
            96, 96,
            PixelFormats.Bgra32,
            null);

        writeableBitmap.Lock();
        try
        {
            // Get the pixels from SkiaSharp bitmap
            int stride = skBitmap.RowBytes;
            int bufferSize = skBitmap.Height * stride;
            
            // Copy pixels using Marshal (safe code)
            byte[] pixels = new byte[bufferSize];
            Marshal.Copy(skBitmap.GetPixels(), pixels, 0, bufferSize);
            Marshal.Copy(pixels, 0, writeableBitmap.BackBuffer, bufferSize);
            
            writeableBitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, skBitmap.Width, skBitmap.Height));
        }
        finally
        {
            writeableBitmap.Unlock();
        }
        
        writeableBitmap.Freeze(); // Make thread-safe for WPF
        return writeableBitmap;
    }

    public void AddText(TextAnnotation annotation)
    {
        _textAnnotations.Add(annotation);
    }

    public void AddImage(ImageAnnotation annotation)
    {
        _imageAnnotations.Add(annotation);
    }

    public void AddShape(ShapeAnnotation annotation)
    {
        _shapeAnnotations.Add(annotation);
    }

    public void RotatePage(int pageNumber, int degrees)
    {
        if (_pageRotations.TryGetValue(pageNumber, out int current))
        {
            _pageRotations[pageNumber] = (current + degrees) % 360;
        }
        else
        {
            _pageRotations[pageNumber] = degrees;
        }
    }

    public int GetPageRotation(int pageNumber)
    {
        return _pageRotations.TryGetValue(pageNumber, out int rotation) ? rotation : 0;
    }

    /// <summary>
    /// Get the inherent rotation of a PDF page (from PDF metadata)
    /// </summary>
    private int GetPdfPageRotation(int pageNumber)
    {
        if (_pdfBytes == null || pageNumber < 0 || pageNumber >= PageCount)
            return 0;

        try
        {
            using var memStream = new MemoryStream(_pdfBytes, writable: false);
            using var reader = new PdfReader(memStream);
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            
            // iText uses 1-based page numbering
            var page = pdfDoc.GetPage(pageNumber + 1);
            int rotation = page.GetRotation();
            
            // Normalize rotation to 0, 90, 180, or 270
            return rotation % 360;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting page rotation: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Get the dimensions (width, height) of a PDF page in points
    /// </summary>
    public (float width, float height) GetPageDimensions(int pageNumber)
    {
        if (_pdfBytes == null || pageNumber < 0 || pageNumber >= _pageCount)
            return (0, 0);

        try
        {
            using var memStream = new MemoryStream(_pdfBytes, writable: false);
            using var reader = new PdfReader(memStream);
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            
            // iText uses 1-based page numbering
            var page = pdfDoc.GetPage(pageNumber + 1);
            var pageSize = page.GetPageSize();
            
            return (pageSize.GetWidth(), pageSize.GetHeight());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting page dimensions: {ex.Message}");
            return (0, 0);
        }
    }

    public void DeletePage(int pageNumber)
    {
        _deletedPages.Add(pageNumber);
        _pageCache.Clear();
    }

    public void DuplicatePage(int pageNumber)
    {
        _duplicatedPages.Add(pageNumber);
        _pageCache.Clear();
    }

    /// <summary>
    /// Reorder pages in a PDF file
    /// </summary>
    private bool ReorderPages(string sourceFile, string destFile, int[] pageOrder)
    {
        try
        {
            using var reader = new PdfReader(sourceFile);
            using var writer = new PdfWriter(destFile);
            using var srcDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            using var destDoc = new iText.Kernel.Pdf.PdfDocument(writer);

            foreach (int originalIndex in pageOrder)
            {
                // Convert 0-based index to 1-based page number
                int pageNum = originalIndex + 1;
                if (pageNum >= 1 && pageNum <= srcDoc.GetNumberOfPages())
                {
                    srcDoc.CopyPagesTo(pageNum, pageNum, destDoc);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reordering pages: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Duplicate pages in a PDF file (each duplicated page is inserted after its original)
    /// </summary>
    private bool DuplicatePages(string sourceFile, string destFile, List<int> pagesToDuplicate)
    {
        try
        {
            using var reader = new PdfReader(sourceFile);
            using var writer = new PdfWriter(destFile);
            using var srcDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            using var destDoc = new iText.Kernel.Pdf.PdfDocument(writer);

            int totalPages = srcDoc.GetNumberOfPages();
            
            // Sort duplicates so we can process them in order
            var sortedDuplicates = pagesToDuplicate.OrderBy(p => p).ToHashSet();
            
            for (int i = 0; i < totalPages; i++)
            {
                int pageNum = i + 1; // 1-based
                
                // Copy original page
                srcDoc.CopyPagesTo(pageNum, pageNum, destDoc);
                
                // If this page should be duplicated, copy it again
                if (sortedDuplicates.Contains(i))
                {
                    srcDoc.CopyPagesTo(pageNum, pageNum, destDoc);
                    System.Diagnostics.Debug.WriteLine($"Duplicated page {pageNum}");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error duplicating pages: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SaveAsync(string filePath)
    {
        if (_currentFilePath == null)
            return false;

        return await Task.Run(() =>
        {
            try
            {
                string tempFile = IoPath.GetTempFileName();
                string sourceFile = _currentFilePath;

                // If page order has changed, create a reordered PDF first
                if (_pageOrder != null && _pageOrder.Length > 0)
                {
                    string reorderedFile = IoPath.GetTempFileName();
                    if (ReorderPages(sourceFile, reorderedFile, _pageOrder))
                    {
                        sourceFile = reorderedFile;
                    }
                }

                // If pages need to be duplicated, handle that
                if (_duplicatedPages.Count > 0)
                {
                    string duplicatedFile = IoPath.GetTempFileName();
                    if (DuplicatePages(sourceFile, duplicatedFile, _duplicatedPages))
                    {
                        // Clean up previous temp file if it was created by reorder
                        if (sourceFile != _currentFilePath && IoFile.Exists(sourceFile))
                        {
                            try { IoFile.Delete(sourceFile); } catch { }
                        }
                        sourceFile = duplicatedFile;
                    }
                }

                using (var reader = new PdfReader(sourceFile))
                using (var writer = new PdfWriter(tempFile))
                using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer))
                {
                    // Apply deletions (in reverse order to maintain page numbers)
                    var pagesToDelete = _deletedPages.OrderByDescending(p => p).ToList();
                    foreach (var pageNum in pagesToDelete)
                    {
                        if (pageNum < pdfDoc.GetNumberOfPages())
                        {
                            pdfDoc.RemovePage(pageNum + 1); // iText uses 1-based indexing
                        }
                    }

                    // Apply rotations
                    foreach (var rotation in _pageRotations)
                    {
                        int adjustedPage = rotation.Key + 1;
                        // Adjust for deleted pages
                        foreach (var deleted in _deletedPages.Where(d => d < rotation.Key))
                        {
                            adjustedPage--;
                        }
                        if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages())
                        {
                            var page = pdfDoc.GetPage(adjustedPage);
                            int currentRotation = page.GetRotation();
                            page.SetRotation((currentRotation + rotation.Value) % 360);
                        }
                    }

                    // Apply redactions (white rectangles over deleted/modified content)
                    foreach (var redaction in _redactions)
                    {
                        int adjustedPage = redaction.PageNumber + 1;
                        foreach (var deleted in _deletedPages.Where(d => d < redaction.PageNumber))
                        {
                            adjustedPage--;
                        }

                        if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages())
                        {
                            var page = pdfDoc.GetPage(adjustedPage);
                            var canvas = new PdfCanvas(page);

                            // Draw white rectangle to cover content
                            // Coordinates are already in PDF points
                            canvas.SaveState()
                                .SetFillColor(ColorConstants.WHITE)
                                .Rectangle(redaction.X, redaction.Y, redaction.Width, redaction.Height)
                                .Fill()
                                .RestoreState();
                        }
                    }

                    // Apply moved texts (extracted text with new positions)
                    foreach (var movedText in _movedTexts)
                    {
                        int adjustedPage = movedText.PageNumber + 1;
                        foreach (var deleted in _deletedPages.Where(d => d < movedText.PageNumber))
                        {
                            adjustedPage--;
                        }

                        if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages())
                        {
                            var page = pdfDoc.GetPage(adjustedPage);
                            var mediaBox = page.GetMediaBox();
                            var canvas = new PdfCanvas(page);

                            // Get font
                            PdfFont font = GetThaiCompatibleFont(movedText.FontName, false, false, movedText.Text);

                            // Draw text at new position
                            canvas.BeginText()
                                .SetFontAndSize(font, movedText.FontSize > 0 ? movedText.FontSize : 12f)
                                .SetFillColor(ColorConstants.BLACK)
                                .MoveText(movedText.X, movedText.Y)
                                .ShowText(movedText.Text)
                                .EndText();
                        }
                    }

                    // Apply moved images (extracted images with new positions)
                    foreach (var movedImage in _movedImages)
                    {
                        int adjustedPage = movedImage.PageNumber + 1;
                        foreach (var deleted in _deletedPages.Where(d => d < movedImage.PageNumber))
                        {
                            adjustedPage--;
                        }

                        if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages() && movedImage.ImageBytes.Length > 0)
                        {
                            var page = pdfDoc.GetPage(adjustedPage);
                            var canvas = new PdfCanvas(page);

                            try
                            {
                                var imageData = ImageDataFactory.Create(movedImage.ImageBytes);
                                var pdfImage = new iText.Layout.Element.Image(imageData);

                                // Add image using layout document for proper positioning
                                using var layoutDoc = new iText.Layout.Document(pdfDoc);
                                pdfImage.SetFixedPosition(adjustedPage, movedImage.X, movedImage.Y);
                                pdfImage.ScaleToFit(movedImage.Width, movedImage.Height);
                                layoutDoc.Add(pdfImage);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error adding moved image: {ex.Message}");
                            }
                        }
                    }

                    // Apply text annotations
                    // Convert from screen pixels (96 DPI) to PDF points (72 DPI)
                    const float SCREEN_TO_PDF = 72f / 96f; // 0.75

                    foreach (var textAnn in _textAnnotations)
                    {
                        int adjustedPage = textAnn.PageNumber + 1;
                        foreach (var deleted in _deletedPages.Where(d => d < textAnn.PageNumber))
                        {
                            adjustedPage--;
                        }

                        if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages())
                        {
                            var page = pdfDoc.GetPage(adjustedPage);
                            var mediaBox = page.GetMediaBox();

                            // Create canvas directly on page
                            var canvas = new PdfCanvas(page);

                            // Get font - automatically uses Thai-compatible font when Thai text detected
                            PdfFont font = GetThaiCompatibleFont(textAnn.FontFamily, textAnn.IsBold, textAnn.IsItalic, textAnn.Text);

                            // COORDINATE CONVERSION:
                            // WPF renders PDF at 96 DPI, PDF uses 72 DPI (points)
                            // Screen coordinates (X, Y, Width, Height) need to be scaled by 72/96 = 0.75
                            // Font size is stored in POINTS, so no conversion needed

                            // Use MediaBox for accurate coordinate calculation (handles Y offset)
                            float mediaBoxTop = mediaBox.GetY() + mediaBox.GetHeight();
                            float mediaBoxLeft = mediaBox.GetX();

                            float pdfX = (float)textAnn.X * SCREEN_TO_PDF + mediaBoxLeft;
                            float pdfY = (float)textAnn.Y * SCREEN_TO_PDF;
                            float pdfFontSize = textAnn.FontSize; // Already in points, no conversion
                            float padding = 2f * SCREEN_TO_PDF;
                            float borderWidthPdf = textAnn.BorderWidth * SCREEN_TO_PDF;

                            // Calculate text width using PDF font metrics
                            // GetWidth(text, fontSize) returns width in POINTS directly
                            float pdfTextWidth = font.GetWidth(textAnn.Text, pdfFontSize);

                            // Box dimensions based on PDF font
                            float boxWidth = pdfTextWidth + (padding * 2);
                            float boxHeight = pdfFontSize * 1.4f + (padding * 2); // 1.4 for line height with some margin

                            // PDF Y coordinate: 0 is at bottom, use mediaBoxTop for correct positioning
                            float boxX = pdfX;
                            float boxY = mediaBoxTop - pdfY - boxHeight;

                            // Text position: PDF draws from baseline (approximately 80% up from bottom of text)
                            float textX = boxX + padding;
                            float textY = boxY + padding + (pdfFontSize * 0.25f);

                            // Draw background if not transparent
                            if (!string.IsNullOrEmpty(textAnn.BackgroundColor) && textAnn.BackgroundColor != "Transparent")
                            {
                                var bgColor = ParseColor(textAnn.BackgroundColor);
                                canvas.SaveState()
                                    .SetFillColor(bgColor)
                                    .Rectangle(boxX, boxY, boxWidth, boxHeight)
                                    .Fill()
                                    .RestoreState();
                            }

                            // Draw border if specified
                            if (!string.IsNullOrEmpty(textAnn.BorderColor) && textAnn.BorderColor != "Transparent" && textAnn.BorderWidth > 0)
                            {
                                var strokeColor = ParseColor(textAnn.BorderColor);
                                canvas.SaveState()
                                    .SetStrokeColor(strokeColor)
                                    .SetLineWidth(borderWidthPdf)
                                    .Rectangle(boxX, boxY, boxWidth, boxHeight)
                                    .Stroke()
                                    .RestoreState();
                            }

                            // Draw text
                            var textColorParsed = ParseColor(textAnn.Color);
                            canvas.BeginText()
                                .SetFontAndSize(font, pdfFontSize)
                                .SetFillColor(textColorParsed)
                                .MoveText(textX, textY)
                                .ShowText(textAnn.Text)
                                .EndText();
                            
                            // Draw underline if specified
                            if (textAnn.IsUnderline)
                            {
                                canvas.SaveState()
                                    .SetStrokeColor(textColorParsed)
                                    .SetLineWidth(0.5f * SCREEN_TO_PDF)
                                    .MoveTo(textX, textY - 1)
                                    .LineTo(textX + pdfTextWidth, textY - 1)
                                    .Stroke()
                                    .RestoreState();
                            }
                        }
                    }

                    // Apply image annotations
                    foreach (var imgAnn in _imageAnnotations)
                    {
                        int adjustedPage = imgAnn.PageNumber + 1;
                        foreach (var deleted in _deletedPages.Where(d => d < imgAnn.PageNumber))
                        {
                            adjustedPage--;
                        }
                        if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages() && IoFile.Exists(imgAnn.ImagePath))
                        {
                            var page = pdfDoc.GetPage(adjustedPage);
                            var mediaBox = page.GetMediaBox();

                            // Use MediaBox for accurate coordinate calculation (handles Y offset)
                            float mediaBoxTop = mediaBox.GetY() + mediaBox.GetHeight();
                            float mediaBoxLeft = mediaBox.GetX();

                            // Convert screen coordinates to PDF points
                            // Both position and dimensions need conversion from 96 DPI to 72 DPI
                            float pdfX = (float)imgAnn.X * SCREEN_TO_PDF + mediaBoxLeft;
                            float pdfY = (float)imgAnn.Y * SCREEN_TO_PDF;
                            float pdfWidth = (float)imgAnn.Width * SCREEN_TO_PDF;
                            float pdfHeight = (float)imgAnn.Height * SCREEN_TO_PDF;

                            var imageData = ImageDataFactory.Create(imgAnn.ImagePath);
                            var canvas = new PdfCanvas(page);
                            canvas.AddImageFittedIntoRectangle(
                                imageData,
                                new ITextRectangle(
                                    pdfX,
                                    mediaBoxTop - pdfY - pdfHeight,
                                    pdfWidth,
                                    pdfHeight),
                                false);
                        }
                    }

                    // Apply shape annotations
                    foreach (var shapeAnn in _shapeAnnotations)
                    {
                        int adjustedPage = shapeAnn.PageNumber + 1;
                        foreach (var deleted in _deletedPages.Where(d => d < shapeAnn.PageNumber))
                        {
                            adjustedPage--;
                        }
                        if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages())
                        {
                            var page = pdfDoc.GetPage(adjustedPage);
                            var mediaBox = page.GetMediaBox();
                            var canvas = new PdfCanvas(page);

                            // Use MediaBox for accurate coordinate calculation (handles Y offset)
                            float mediaBoxTop = mediaBox.GetY() + mediaBox.GetHeight();
                            float mediaBoxLeft = mediaBox.GetX();

                            // Convert screen coordinates to PDF points
                            // Both position and dimensions need conversion from 96 DPI to 72 DPI
                            float pdfX = (float)shapeAnn.X * SCREEN_TO_PDF + mediaBoxLeft;
                            float pdfY = (float)shapeAnn.Y * SCREEN_TO_PDF;
                            float pdfWidth = (float)shapeAnn.Width * SCREEN_TO_PDF;
                            float pdfHeight = (float)shapeAnn.Height * SCREEN_TO_PDF;
                            float pdfStrokeWidth = shapeAnn.StrokeWidth * SCREEN_TO_PDF;

                            // PDF Y coordinate conversion using mediaBoxTop
                            float y = mediaBoxTop - pdfY - pdfHeight;
                            
                            canvas.SaveState();
                            
                            // Set fill color if not transparent
                            bool hasFill = !string.IsNullOrEmpty(shapeAnn.FillColor) && shapeAnn.FillColor != "Transparent";
                            if (hasFill)
                            {
                                canvas.SetFillColor(ParseColor(shapeAnn.FillColor));
                            }
                            
                            // Set stroke color
                            canvas.SetStrokeColor(ParseColor(shapeAnn.StrokeColor));
                            canvas.SetLineWidth(pdfStrokeWidth);
                            
                            switch (shapeAnn.ShapeType)
                            {
                                case ShapeType.Rectangle:
                                    canvas.Rectangle(pdfX, y, pdfWidth, pdfHeight);
                                    if (hasFill) canvas.FillStroke();
                                    else canvas.Stroke();
                                    break;
                                    
                                case ShapeType.Ellipse:
                                    canvas.Ellipse(pdfX, y, pdfX + pdfWidth, y + pdfHeight);
                                    if (hasFill) canvas.FillStroke();
                                    else canvas.Stroke();
                                    break;
                                    
                                case ShapeType.Line:
                                    float pdfX2 = (float)shapeAnn.X2 * SCREEN_TO_PDF + mediaBoxLeft;
                                    float pdfY2 = (float)shapeAnn.Y2 * SCREEN_TO_PDF;
                                    float y1 = mediaBoxTop - pdfY;
                                    float y2 = mediaBoxTop - pdfY2;
                                    canvas.MoveTo(pdfX, y1);
                                    canvas.LineTo(pdfX2, y2);
                                    canvas.Stroke();
                                    break;
                            }
                            
                            canvas.RestoreState();
                        }
                    }
                    
                    _pageCount = pdfDoc.GetNumberOfPages();
                }

                // Move temp file to target
                if (IoFile.Exists(filePath))
                    IoFile.Delete(filePath);
                IoFile.Move(tempFile, filePath);

                _currentFilePath = filePath;
                
                // Clean up temporary reordered file if created
                if (sourceFile != _currentFilePath && IoFile.Exists(sourceFile))
                {
                    try { IoFile.Delete(sourceFile); } catch { }
                }

                // Clear modifications
                _textAnnotations.Clear();
                _imageAnnotations.Clear();
                _shapeAnnotations.Clear();
                _pageRotations.Clear();
                _deletedPages.Clear();
                _duplicatedPages.Clear();
                _pageOrder = null;

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving PDF: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Get system font file path for a given font family
    /// </summary>
    private static string GetSystemFontPath(string fontFamily, bool isBold, bool isItalic)
    {
        string fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        
        // Map common font names to their file names
        var fontMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Sans-serif
            { "Arial", "arial.ttf" },
            { "Arial Bold", "arialbd.ttf" },
            { "Arial Italic", "ariali.ttf" },
            { "Arial Bold Italic", "arialbi.ttf" },
            { "Helvetica", "arial.ttf" },
            { "Verdana", "verdana.ttf" },
            { "Verdana Bold", "verdanab.ttf" },
            { "Tahoma", "tahoma.ttf" },
            { "Tahoma Bold", "tahomabd.ttf" },
            { "Calibri", "calibri.ttf" },
            { "Calibri Bold", "calibrib.ttf" },
            { "Segoe UI", "segoeui.ttf" },
            { "Segoe UI Bold", "segoeuib.ttf" },
            
            // Serif
            { "Times New Roman", "times.ttf" },
            { "Times New Roman Bold", "timesbd.ttf" },
            { "Georgia", "georgia.ttf" },
            { "Georgia Bold", "georgiab.ttf" },
            { "Cambria", "cambria.ttc" },
            
            // Monospace
            { "Courier New", "cour.ttf" },
            { "Courier New Bold", "courbd.ttf" },
            { "Consolas", "consola.ttf" },
            { "Consolas Bold", "consolab.ttf" },
            
            // Thai fonts - TH Sarabun family
            { "TH Sarabun New", "THSarabunNew.ttf" },
            { "TH Sarabun New Bold", "THSarabunNew Bold.ttf" },
            { "TH SarabunPSK", "THSarabunPSK.ttf" },
            { "TH SarabunPSK Bold", "THSarabunPSK-Bold.ttf" },
            { "Sarabun", "THSarabunNew.ttf" },
            
            // Thai fonts - Angsana family (TTC contains multiple faces: 0=Regular, 1=Bold, 2=Italic, 3=BoldItalic)
            { "Angsana New", "angsana.ttc,0" },
            { "Angsana New Bold", "angsana.ttc,1" },
            { "Angsana New Italic", "angsana.ttc,2" },
            { "Angsana New Bold Italic", "angsana.ttc,3" },
            { "AngsanaUPC", "angsana.ttc,4" },
            
            // Thai fonts - Cordia family (TTC contains multiple faces)
            { "Cordia New", "cordia.ttc,0" },
            { "Cordia New Bold", "cordia.ttc,1" },
            { "Cordia New Italic", "cordia.ttc,2" },
            { "Cordia New Bold Italic", "cordia.ttc,3" },
            { "CordiaUPC", "cordia.ttc,4" },
            
            // Thai fonts - Browallia family (TTC contains multiple faces)
            { "Browallia New", "browalia.ttc,0" },
            { "Browallia New Bold", "browalia.ttc,1" },
            { "Browallia New Italic", "browalia.ttc,2" },
            { "Browallia New Bold Italic", "browalia.ttc,3" },
            { "BrowalliaUPC", "browalia.ttc,4" },
            
            // Thai fonts - Leelawadee family
            { "Leelawadee", "leelawad.ttf" },
            { "Leelawadee Bold", "leelawdb.ttf" },
            { "Leelawadee UI", "LeelawUI.ttf" },
            { "Leelawadee UI Bold", "LeelaUIb.ttf" },
            
            // Thai fonts - Other
            { "DilleniaUPC", "upcil.ttf" },
            { "EucrosiaUPC", "upcel.ttf" },
            { "FreesiaUPC", "upcfl.ttf" },
            { "IrisUPC", "upcil.ttf" },
            { "JasmineUPC", "upcjl.ttf" },
            { "KodchiangUPC", "upckl.ttf" },
            { "LilyUPC", "upcll.ttf" },
            { "Norasi", "Norasi.ttf" },
            { "Garuda", "Garuda.ttf" },
            { "Loma", "Loma.ttf" },
            { "Tlwg Typist", "TlwgTypist.ttf" },
            
            // Microsoft Thai
            { "Microsoft Sans Serif", "micross.ttf" },
            
            // Other
            { "Comic Sans MS", "comic.ttf" },
            { "Impact", "impact.ttf" },
        };

        // Try to find exact match
        string key = fontFamily;
        if (isBold && isItalic) key = $"{fontFamily} Bold Italic";
        else if (isBold) key = $"{fontFamily} Bold";
        else if (isItalic) key = $"{fontFamily} Italic";

        if (fontMap.TryGetValue(key, out string? fileName))
        {
            // Handle TTC index format: "filename.ttc,index"
            if (fileName.Contains(','))
            {
                var parts = fileName.Split(',');
                return IoPath.Combine(fontsFolder, parts[0]) + "," + parts[1];
            }
            return IoPath.Combine(fontsFolder, fileName);
        }

        // Try base font name
        if (fontMap.TryGetValue(fontFamily, out fileName))
        {
            // Handle TTC index format: "filename.ttc,index"
            if (fileName.Contains(','))
            {
                var parts = fileName.Split(',');
                return IoPath.Combine(fontsFolder, parts[0]) + "," + parts[1];
            }
            return IoPath.Combine(fontsFolder, fileName);
        }

        // Try to find any .ttf file with matching name
        string searchPattern = fontFamily.Replace(" ", "").ToLower();
        try
        {
            foreach (var file in IoDirectory.GetFiles(fontsFolder, "*.ttf"))
            {
                if (IoPath.GetFileNameWithoutExtension(file).ToLower().Contains(searchPattern))
                {
                    return file;
                }
            }
        }
        catch { }

        // Default to Arial
        return IoPath.Combine(fontsFolder, "arial.ttf");
    }

    /// <summary>
    /// Check if text contains Thai characters
    /// </summary>
    private static bool ContainsThai(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            // Thai Unicode range: 0x0E00 - 0x0E7F
            if (c >= 0x0E00 && c <= 0x0E7F)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Get the app's bundled fonts folder path
    /// </summary>
    private static string GetAppFontsFolder()
    {
        string appPath = AppDomain.CurrentDomain.BaseDirectory;
        return IoPath.Combine(appPath, "Fonts");
    }

    /// <summary>
    /// Get bundled font path for the given font family and style
    /// Returns null if font not found in app folder
    /// </summary>
    private static string? GetBundledFontPath(string fontFamily, bool isBold, bool isItalic)
    {
        string fontsFolder = GetAppFontsFolder();
        if (!IoDirectory.Exists(fontsFolder)) return null;

        string lower = fontFamily.ToLower();
        
        // Map font family to bundled font file
        // TH Sarabun New - primary Thai font
        if (lower.Contains("sarabun") || lower.Contains("th sarabun"))
        {
            string fileName;
            if (isBold && isItalic) fileName = "THSarabunNew-BoldItalic.ttf";
            else if (isBold) fileName = "THSarabunNew-Bold.ttf";
            else if (isItalic) fileName = "THSarabunNew-Italic.ttf";
            else fileName = "THSarabunNew.ttf";
            
            string path = IoPath.Combine(fontsFolder, fileName);
            if (IoFile.Exists(path)) return path;
        }
        
        // Noto Sans Thai - fallback Thai font
        if (lower.Contains("noto") && lower.Contains("thai"))
        {
            string fileName = isBold ? "NotoSansThai-Bold.ttf" : "NotoSansThai-Regular.ttf";
            string path = IoPath.Combine(fontsFolder, fileName);
            if (IoFile.Exists(path)) return path;
        }
        
        return null;
    }

    /// <summary>
    /// Get the default bundled Thai font path
    /// </summary>
    private static string? GetDefaultBundledThaiFont(bool isBold, bool isItalic)
    {
        string fontsFolder = GetAppFontsFolder();
        if (!IoDirectory.Exists(fontsFolder)) return null;

        // Try TH Sarabun New first (primary bundled Thai font)
        string fileName;
        if (isBold && isItalic) fileName = "THSarabunNew-BoldItalic.ttf";
        else if (isBold) fileName = "THSarabunNew-Bold.ttf";
        else if (isItalic) fileName = "THSarabunNew-Italic.ttf";
        else fileName = "THSarabunNew.ttf";
        
        string path = IoPath.Combine(fontsFolder, fileName);
        if (IoFile.Exists(path)) return path;
        
        // Fallback to Noto Sans Thai
        fileName = isBold ? "NotoSansThai-Bold.ttf" : "NotoSansThai-Regular.ttf";
        path = IoPath.Combine(fontsFolder, fileName);
        if (IoFile.Exists(path)) return path;
        
        return null;
    }

    /// <summary>
    /// Get a font that supports the text content with proper embedding
    /// Automatically uses Thai-compatible font when Thai characters are detected
    /// Priority: 1) Bundled fonts 2) System fonts 3) Fallback
    /// </summary>
    private static PdfFont GetThaiCompatibleFont(string fontFamily, bool isBold, bool isItalic, string? textContent = null)
    {
        string systemFontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        bool needsThai = textContent != null && ContainsThai(textContent);
        
        try
        {
            // ========== PRIORITY 1: Bundled fonts (always available) ==========
            
            // Try to find exact match in bundled fonts
            string? bundledPath = GetBundledFontPath(fontFamily, isBold, isItalic);
            if (bundledPath != null)
            {
                try
                {
                    var font = PdfFontFactory.CreateFont(bundledPath, PdfEncodings.IDENTITY_H,
                        PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
                    System.Diagnostics.Debug.WriteLine($"[FONT] Using bundled font: {bundledPath} for '{fontFamily}' (bold={isBold}, italic={isItalic})");
                    return font;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FONT] Failed to load bundled font {bundledPath}: {ex.Message}");
                }
            }
            
            // If Thai needed and no exact match, use default bundled Thai font
            if (needsThai)
            {
                string? defaultThaiPath = GetDefaultBundledThaiFont(isBold, isItalic);
                if (defaultThaiPath != null)
                {
                    try
                    {
                        var font = PdfFontFactory.CreateFont(defaultThaiPath, PdfEncodings.IDENTITY_H,
                            PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
                        System.Diagnostics.Debug.WriteLine($"[FONT] Using bundled Thai font: {defaultThaiPath} for '{fontFamily}' (bold={isBold}, italic={isItalic})");
                        return font;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FONT] Failed to load bundled Thai font {defaultThaiPath}: {ex.Message}");
                    }
                }
            }
            
            // ========== PRIORITY 2: System fonts ==========
            
            if (needsThai)
            {
                // Map requested font style to Thai-compatible equivalent
                string thaiFontFile = GetThaiFontForStyle(fontFamily, isBold, isItalic);
                
                // Handle TTC index format: "filename.ttc,index"
                string thaiPath;
                if (thaiFontFile.Contains(','))
                {
                    var parts = thaiFontFile.Split(',');
                    thaiPath = IoPath.Combine(systemFontsFolder, parts[0]) + "," + parts[1];
                }
                else
                {
                    thaiPath = IoPath.Combine(systemFontsFolder, thaiFontFile);
                }
                
                // Check if file exists (without the index suffix for TTC)
                string fileToCheck = thaiPath.Contains(',') ? thaiPath.Substring(0, thaiPath.LastIndexOf(',')) : thaiPath;
                if (IoFile.Exists(fileToCheck))
                {
                    try
                    {
                        var font = PdfFontFactory.CreateFont(thaiPath, PdfEncodings.IDENTITY_H,
                            PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
                        System.Diagnostics.Debug.WriteLine($"[FONT] Using system Thai font: {thaiPath} for '{fontFamily}' (bold={isBold}, italic={isItalic})");
                        return font;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FONT] Failed to load system font {thaiPath}: {ex.Message}");
                    }
                }

                // System Thai font fallback list - use cached font check
                var thaiFontFiles = new[]
                {
                    "tahoma.ttf", "tahomabd.ttf", "segoeui.ttf", "segoeuib.ttf",
                    "LeelawUI.ttf", "leelawad.ttf", "cordia.ttc", "angsana.ttc", "browalia.ttc"
                };
                
                foreach (var fontFile in thaiFontFiles)
                {
                    if (_availableSystemFonts.Value.Contains(fontFile))
                    {
                        string fontPath = IoPath.Combine(systemFontsFolder, fontFile);
                        try
                        {
                            var font = PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H,
                                PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
                            System.Diagnostics.Debug.WriteLine($"[FONT] Using system Thai fallback: {fontPath}");
                            return font;
                        }
                        catch { }
                    }
                }
            }
            else
            {
                // No Thai characters - try to use the requested system font
                string requestedPath = GetSystemFontPath(fontFamily, isBold, isItalic);
                
                // Check if file exists (without the index suffix for TTC)
                string fileToCheck = requestedPath.Contains(',') ? requestedPath.Substring(0, requestedPath.LastIndexOf(',')) : requestedPath;
                if (!string.IsNullOrEmpty(requestedPath) && IoFile.Exists(fileToCheck))
                {
                    try
                    {
                        var font = PdfFontFactory.CreateFont(requestedPath, PdfEncodings.IDENTITY_H,
                            PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
                        System.Diagnostics.Debug.WriteLine($"[FONT] Using system font: {requestedPath} (bold={isBold}, italic={isItalic})");
                        return font;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FONT] Failed to load system font {requestedPath}: {ex.Message}");
                    }
                }
            }

            // ========== PRIORITY 3: Final fallbacks ==========
            
            // Try bundled Noto Sans Thai as last Thai option
            string? notoPath = IoPath.Combine(GetAppFontsFolder(), isBold ? "NotoSansThai-Bold.ttf" : "NotoSansThai-Regular.ttf");
            if (IoFile.Exists(notoPath))
            {
                try
                {
                    var font = PdfFontFactory.CreateFont(notoPath, PdfEncodings.IDENTITY_H,
                        PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
                    System.Diagnostics.Debug.WriteLine($"[FONT] Using bundled Noto Sans Thai fallback: {notoPath}");
                    return font;
                }
                catch { }
            }
            
            // Try system Tahoma - use cached font check
            string tahomaFile = isBold ? "tahomabd.ttf" : "tahoma.ttf";
            if (_availableSystemFonts.Value.Contains(tahomaFile))
            {
                string tahomaPath = IoPath.Combine(systemFontsFolder, tahomaFile);
                var font = PdfFontFactory.CreateFont(tahomaPath, PdfEncodings.IDENTITY_H,
                    PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
                System.Diagnostics.Debug.WriteLine($"[FONT] Using Tahoma fallback: {tahomaPath} (requested '{fontFamily}' not found)");
                return font;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FONT] Font error: {ex.Message}");
        }

        // Last resort - Helvetica (no Thai support)
        System.Diagnostics.Debug.WriteLine($"[FONT] WARNING: Using Helvetica - Thai will not display correctly! Requested: '{fontFamily}'");
        return PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
    }

    /// <summary>
    /// Map font style to Thai-compatible equivalent
    /// </summary>
    private static string GetThaiFontForStyle(string fontFamily, bool isBold, bool isItalic)
    {
        string lower = fontFamily.ToLower();
        string fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        
        // Thai specific fonts - use exact match if available (cached check)
        if (lower.Contains("sarabun"))
        {
            // Try TH Sarabun variants
            var sarabunFiles = new[] { 
                isBold ? "THSarabunNew Bold.ttf" : "THSarabunNew.ttf",
                isBold ? "THSarabunPSK-Bold.ttf" : "THSarabunPSK.ttf",
                "THSarabunNew.ttf"
            };
            foreach (var f in sarabunFiles)
            {
                if (_availableSystemFonts.Value.Contains(f)) return f;
            }
        }
        
        if (lower.Contains("angsana"))
        {
            // TTC index: 0=Regular, 1=Bold, 2=Italic, 3=BoldItalic
            if (isBold && isItalic) return "angsana.ttc,3";
            if (isBold) return "angsana.ttc,1";
            if (isItalic) return "angsana.ttc,2";
            return "angsana.ttc,0";
        }
        
        if (lower.Contains("cordia"))
        {
            // TTC index: 0=Regular, 1=Bold, 2=Italic, 3=BoldItalic
            if (isBold && isItalic) return "cordia.ttc,3";
            if (isBold) return "cordia.ttc,1";
            if (isItalic) return "cordia.ttc,2";
            return "cordia.ttc,0";
        }
        
        if (lower.Contains("browallia"))
        {
            // TTC index: 0=Regular, 1=Bold, 2=Italic, 3=BoldItalic
            if (isBold && isItalic) return "browalia.ttc,3";
            if (isBold) return "browalia.ttc,1";
            if (isItalic) return "browalia.ttc,2";
            return "browalia.ttc,0";
        }
        
        if (lower.Contains("leelawadee") || lower.Contains("leelawad"))
        {
            var leelFiles = new[] {
                isBold ? "LeelaUIb.ttf" : "LeelawUI.ttf",
                isBold ? "leelawdb.ttf" : "leelawad.ttf"
            };
            foreach (var f in leelFiles)
            {
                if (_availableSystemFonts.Value.Contains(f)) return f;
            }
            return "LeelawUI.ttf";
        }
        
        if (lower.Contains("norasi"))
        {
            return isBold ? "Norasi-Bold.ttf" : "Norasi.ttf";
        }
        
        if (lower.Contains("garuda"))
        {
            return isBold ? "Garuda-Bold.ttf" : "Garuda.ttf";
        }
        
        if (lower.Contains("loma"))
        {
            return isBold ? "Loma-Bold.ttf" : "Loma.ttf";
        }
        
        // Serif fonts -> Angsana New (Thai serif)
        if (lower.Contains("times") || lower.Contains("georgia") || lower.Contains("garamond") || 
            lower.Contains("palatino") || lower.Contains("cambria") || lower.Contains("serif"))
        {
            // TTC index: 0=Regular, 1=Bold, 2=Italic, 3=BoldItalic
            if (isBold && isItalic) return "angsana.ttc,3";
            if (isBold) return "angsana.ttc,1";
            if (isItalic) return "angsana.ttc,2";
            return "angsana.ttc,0";
        }
        
        // Monospace fonts -> TH Sarabun (good for code) or Tahoma
        if (lower.Contains("courier") || lower.Contains("consolas") || lower.Contains("mono") || 
            lower.Contains("lucida console"))
        {
            // Try Sarabun first (more readable Thai) - cached check
            string sarabun = isBold ? "THSarabunNew Bold.ttf" : "THSarabunNew.ttf";
            if (_availableSystemFonts.Value.Contains(sarabun)) return sarabun;
            return isBold ? "tahomabd.ttf" : "tahoma.ttf";
        }
        
        // Modern UI fonts -> Leelawadee UI or Segoe UI - cached check
        if (lower.Contains("segoe") || lower.Contains("calibri") || lower.Contains("roboto") || 
            lower.Contains("open sans") || lower.Contains("arial"))
        {
            // Try Leelawadee UI first (designed for UI)
            string leela = isBold ? "LeelaUIb.ttf" : "LeelawUI.ttf";
            if (_availableSystemFonts.Value.Contains(leela)) return leela;
            return isBold ? "segoeuib.ttf" : "segoeui.ttf";
        }
        
        // Default -> TH Sarabun (most popular Thai font) or Tahoma - cached check
        string defaultSarabun = isBold ? "THSarabunNew Bold.ttf" : "THSarabunNew.ttf";
        if (_availableSystemFonts.Value.Contains(defaultSarabun)) return defaultSarabun;
        
        return isBold ? "tahomabd.ttf" : "tahoma.ttf";
    }

    private static iText.Kernel.Colors.Color ParseColor(string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor == "Transparent")
        {
            return ColorConstants.BLACK;
        }
        
        try
        {
            // Remove # if present
            hexColor = hexColor.TrimStart('#');
            
            if (hexColor.Length == 6)
            {
                int r = Convert.ToInt32(hexColor.Substring(0, 2), 16);
                int g = Convert.ToInt32(hexColor.Substring(2, 2), 16);
                int b = Convert.ToInt32(hexColor.Substring(4, 2), 16);
                return new DeviceRgb(r, g, b);
            }
        }
        catch { }
        
        return ColorConstants.BLACK;
    }

    public async Task<bool> MergePdfsAsync(string[] inputFiles, string outputFile)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var writer = new PdfWriter(outputFile);
                using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(writer);
                var merger = new PdfMerger(pdfDoc);

                foreach (var file in inputFiles)
                {
                    using var reader = new PdfReader(file);
                    using var srcDoc = new iText.Kernel.Pdf.PdfDocument(reader);
                    merger.Merge(srcDoc, 1, srcDoc.GetNumberOfPages());
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error merging PDFs: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> SplitPdfAsync(string inputFile, string outputFolder)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!IoDirectory.Exists(outputFolder))
                    IoDirectory.CreateDirectory(outputFolder);

                using var reader = new PdfReader(inputFile);
                using var srcDoc = new iText.Kernel.Pdf.PdfDocument(reader);

                string baseName = IoPath.GetFileNameWithoutExtension(inputFile);

                for (int i = 1; i <= srcDoc.GetNumberOfPages(); i++)
                {
                    string outputFilePath = IoPath.Combine(outputFolder, $"{baseName}_page_{i}.pdf");
                    using var writer = new PdfWriter(outputFilePath);
                    using var destDoc = new iText.Kernel.Pdf.PdfDocument(writer);
                    srcDoc.CopyPagesTo(i, i, destDoc);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error splitting PDF: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> ExtractPagesAsync(string inputFile, int[] pageNumbers, string outputFile)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var reader = new PdfReader(inputFile);
                using var srcDoc = new iText.Kernel.Pdf.PdfDocument(reader);
                using var writer = new PdfWriter(outputFile);
                using var destDoc = new iText.Kernel.Pdf.PdfDocument(writer);

                // Use the order specified by user (no sorting)
                foreach (int pageNum in pageNumbers)
                {
                    if (pageNum >= 1 && pageNum <= srcDoc.GetNumberOfPages())
                    {
                        srcDoc.CopyPagesTo(pageNum, pageNum, destDoc);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extracting pages: {ex.Message}");
                return false;
            }
        });
    }

    public void ClearAnnotations()
    {
        _textAnnotations.Clear();
        _imageAnnotations.Clear();
        _shapeAnnotations.Clear();
    }

    public void SetPageOrder(int[] pageOrder)
    {
        _pageOrder = pageOrder;
        _pageCache.Clear();
    }

    /// <summary>
    /// Add a redaction area to cover existing content with white rectangle
    /// </summary>
    public void AddRedaction(int pageNumber, float x, float y, float width, float height)
    {
        _redactions.Add((pageNumber, x, y, width, height));
    }

    /// <summary>
    /// Add moved text element (extracted text with new position)
    /// </summary>
    public void AddMovedText(ExtractedTextElement element)
    {
        _movedTexts.Add(element);
    }

    /// <summary>
    /// Add moved image element (extracted image with new position)
    /// </summary>
    public void AddMovedImage(ExtractedImageElement element)
    {
        _movedImages.Add(element);
    }

    /// <summary>
    /// Apply header and footer to the PDF according to the configuration
    /// </summary>
    public async Task<bool> ApplyHeaderFooterAsync(string inputFile, string outputFile, HeaderFooterConfig config, string? fileName = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ApplyHeaderFooterAsync: input={inputFile}, output={outputFile}");
                System.Diagnostics.Debug.WriteLine($"Config: HeaderEnabled={config.HeaderEnabled}, FooterEnabled={config.FooterEnabled}");
                
                string tempFile = IoPath.GetTempFileName();
                
                using (var reader = new PdfReader(inputFile))
                using (var writer = new PdfWriter(tempFile))
                using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer))
                {
                    int totalPages = pdfDoc.GetNumberOfPages();
                    string fileNameToUse = fileName ?? IoPath.GetFileName(inputFile);
                    DateTime now = DateTime.Now;
                    
                    System.Diagnostics.Debug.WriteLine($"Processing {totalPages} pages");
                    
                    for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                    {
                        // Check if header/footer should be applied to this page
                        if (!config.ShouldApplyToPage(pageNum, totalPages))
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipping page {pageNum}");
                            continue;
                        }
                        
                        var page = pdfDoc.GetPage(pageNum);
                        var pageSize = page.GetPageSize();
                        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);
                        
                        float leftMargin = 50f; // Left margin in points
                        float rightMargin = pageSize.GetWidth() - 50f; // Right margin
                        float centerX = pageSize.GetWidth() / 2f;
                        
                        System.Diagnostics.Debug.WriteLine($"Page {pageNum}: size={pageSize.GetWidth()}x{pageSize.GetHeight()}");
                        
                        // Apply header
                        if (config.HeaderEnabled)
                        {
                            float headerY = pageSize.GetHeight() - config.HeaderMargin;
                            System.Diagnostics.Debug.WriteLine($"Drawing header at Y={headerY}");
                            
                            DrawHeaderFooterElement(canvas, config.HeaderLeft, pageNum, totalPages, fileNameToUse, now,
                                leftMargin, headerY, HorizontalPosition.Left, pdfDoc);
                            DrawHeaderFooterElement(canvas, config.HeaderCenter, pageNum, totalPages, fileNameToUse, now,
                                centerX, headerY, HorizontalPosition.Center, pdfDoc);
                            DrawHeaderFooterElement(canvas, config.HeaderRight, pageNum, totalPages, fileNameToUse, now,
                                rightMargin, headerY, HorizontalPosition.Right, pdfDoc);
                        }
                        
                        // Apply footer
                        if (config.FooterEnabled)
                        {
                            float footerY = config.FooterMargin;
                            System.Diagnostics.Debug.WriteLine($"Drawing footer at Y={footerY}");
                            
                            DrawHeaderFooterElement(canvas, config.FooterLeft, pageNum, totalPages, fileNameToUse, now,
                                leftMargin, footerY, HorizontalPosition.Left, pdfDoc);
                            DrawHeaderFooterElement(canvas, config.FooterCenter, pageNum, totalPages, fileNameToUse, now,
                                centerX, footerY, HorizontalPosition.Center, pdfDoc);
                            DrawHeaderFooterElement(canvas, config.FooterRight, pageNum, totalPages, fileNameToUse, now,
                                rightMargin, footerY, HorizontalPosition.Right, pdfDoc);
                        }
                        
                        // Apply custom text boxes
                        foreach (var customBox in config.CustomTextBoxes)
                        {
                            DrawCustomTextBox(canvas, customBox, pageNum, totalPages, fileNameToUse, now, pdfDoc);
                        }
                    }
                }
                
                // Move temp file to output
                if (IoFile.Exists(outputFile))
                    IoFile.Delete(outputFile);
                IoFile.Move(tempFile, outputFile);
                
                System.Diagnostics.Debug.WriteLine($"Header/Footer applied successfully to {outputFile}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying header/footer: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        });
    }
    
    /// <summary>
    /// Draw a single header/footer element (text or image)
    /// </summary>
    private void DrawHeaderFooterElement(PdfCanvas canvas, HeaderFooterElement element, int currentPage, int totalPages,
        string fileName, DateTime date, float x, float y, HorizontalPosition position, iText.Kernel.Pdf.PdfDocument pdfDoc)
    {
        System.Diagnostics.Debug.WriteLine($"DrawElement: IsEnabled={element.IsEnabled}, IsImage={element.IsImage}, Text='{element.Text}', ImagePath='{element.ImagePath}'");
        
        if (!element.IsEnabled)
        {
            System.Diagnostics.Debug.WriteLine("Element is disabled, skipping");
            return;
        }
        
        bool drewImage = false;
        
        // Try to draw image if IsImage is true
        if (element.IsImage && !string.IsNullOrEmpty(element.ImagePath))
        {
            if (IoFile.Exists(element.ImagePath))
            {
                try
                {
                    var imageData = ImageDataFactory.Create(element.ImagePath);
                    float imgWidth = (float)element.ImageWidth;
                    float imgHeight = (float)element.ImageHeight;
                    
                    // Adjust x position based on alignment
                    float drawX = position switch
                    {
                        HorizontalPosition.Center => x - (imgWidth / 2f),
                        HorizontalPosition.Right => x - imgWidth,
                        _ => x
                    };
                    
                    // Center image vertically on the baseline
                    float drawY = y - (imgHeight / 2f);
                    
                    canvas.AddImageFittedIntoRectangle(imageData, new ITextRectangle(drawX, drawY, imgWidth, imgHeight), false);
                    drewImage = true;
                    System.Diagnostics.Debug.WriteLine($"Drew image at ({drawX}, {drawY})");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error drawing header/footer image: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Image file not found: {element.ImagePath}");
            }
        }
        
        // Draw text if we didn't draw an image (either IsImage=false, or image failed)
        if (!drewImage && !string.IsNullOrEmpty(element.Text))
        {
            string text = element.GetFormattedText(currentPage, totalPages, fileName, date);
            System.Diagnostics.Debug.WriteLine($"Formatted text: '{text}'");
            
            if (string.IsNullOrEmpty(text))
            {
                System.Diagnostics.Debug.WriteLine("Text is empty after formatting, skipping");
                return;
            }
            
            try
            {
                PdfFont font = GetThaiCompatibleFont(element.FontFamily, element.IsBold, element.IsItalic, text);
                float fontSize = element.FontSize;
                
                // Ensure minimum font size
                if (fontSize < 6f) fontSize = 10f;
                
                // Calculate text width for alignment
                float textWidth = font.GetWidth(text, fontSize);
                
                // Adjust x position based on alignment
                float drawX = position switch
                {
                    HorizontalPosition.Center => x - (textWidth / 2f),
                    HorizontalPosition.Right => x - textWidth,
                    _ => x
                };
                
                var textColor = ParseColor(element.Color);
                
                canvas.BeginText()
                    .SetFontAndSize(font, fontSize)
                    .SetFillColor(textColor)
                    .MoveText(drawX, y)
                    .ShowText(text)
                    .EndText();
                
                System.Diagnostics.Debug.WriteLine($"Drew text '{text}' at ({drawX}, {y}) with fontSize={fontSize}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error drawing header/footer text: {ex.Message}");
            }
        }
        else if (!drewImage)
        {
            System.Diagnostics.Debug.WriteLine("Nothing to draw (no image and no text)");
        }
    }

    /// <summary>
    /// Draw a custom text box at specified position (supports multiline)
    /// </summary>
    private void DrawCustomTextBox(PdfCanvas canvas, CustomTextBox textBox, int currentPage, int totalPages,
        string fileName, DateTime date, iText.Kernel.Pdf.PdfDocument pdfDoc)
    {
        string text = textBox.GetFormattedText(currentPage, totalPages, fileName, date);
        
        System.Diagnostics.Debug.WriteLine($"DrawCustomTextBox: Label={textBox.Label}, Text='{text}', Offset=({textBox.OffsetX}, {textBox.OffsetY})");
        
        // Get current page for size reference
        var page = pdfDoc.GetPage(currentPage);
        var pageSize = page.GetPageSize();
        
        // Position: OffsetX is from left, OffsetY is from bottom (PDF coordinate system)
        float x = textBox.OffsetX;
        float y = textBox.OffsetY;
        
        try
        {
            PdfFont font = GetThaiCompatibleFont(textBox.FontFamily, textBox.IsBold, textBox.IsItalic, text);
            float fontSize = textBox.FontSize;
            if (fontSize < 6f) fontSize = 10f;
            
            var textColor = ParseColor(textBox.Color);
            
            // Draw border if enabled
            if (textBox.ShowBorder)
            {
                canvas.SaveState()
                    .SetStrokeColor(textColor)
                    .SetLineWidth(0.5f)
                    .Rectangle(x, y, textBox.BoxWidth, textBox.BoxHeight)
                    .Stroke()
                    .RestoreState();
            }
            
            // Draw multiline text if present
            if (!string.IsNullOrEmpty(text))
            {
                float padding = 3f;
                float lineHeight = fontSize * 1.2f; // Line spacing
                float textX = x + padding;
                
                // Start from top of box and go down (PDF Y is from bottom, so we start high)
                float topY = y + textBox.BoxHeight - padding - fontSize;
                
                // Split text by newlines
                string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                
                canvas.BeginText()
                    .SetFontAndSize(font, fontSize)
                    .SetFillColor(textColor);
                
                for (int i = 0; i < lines.Length; i++)
                {
                    float lineY = topY - (i * lineHeight);
                    
                    // Stop if we're below the box
                    if (lineY < y + padding) break;
                    
                    canvas.MoveText(textX, lineY)
                        .ShowText(lines[i]);
                    
                    // Reset position for next line (MoveText is relative after first call)
                    if (i < lines.Length - 1)
                    {
                        canvas.MoveText(-textX, -lineY);
                    }
                }
                
                canvas.EndText();
            }
            
            System.Diagnostics.Debug.WriteLine($"Drew custom text box at ({x}, {y}) with size {textBox.BoxWidth}x{textBox.BoxHeight}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error drawing custom text box: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a new PDF from an image file
    /// </summary>
    public async Task<bool> CreatePdfFromImageAsync(string imagePath, string outputPdfPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!IoFile.Exists(imagePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Image file not found: {imagePath}");
                    return false;
                }

                using var writer = new PdfWriter(outputPdfPath);
                using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(writer);
                
                // Load the image
                var imageData = ImageDataFactory.Create(imagePath);
                
                // Calculate page size based on image dimensions
                // Use A4 as default, but scale image to fit
                float imageWidth = imageData.GetWidth();
                float imageHeight = imageData.GetHeight();
                
                // Create page size that matches image aspect ratio
                // Use points (72 per inch), assume 150 DPI for scanned images
                float dpi = 150f;
                float pageWidth = imageWidth / dpi * 72f;
                float pageHeight = imageHeight / dpi * 72f;
                
                // Ensure minimum page size (A4-ish minimum)
                float minWidth = 400f;
                float minHeight = 500f;
                if (pageWidth < minWidth || pageHeight < minHeight)
                {
                    float scale = Math.Max(minWidth / pageWidth, minHeight / pageHeight);
                    pageWidth *= scale;
                    pageHeight *= scale;
                }
                
                // Cap at reasonable maximum (A3-ish maximum)
                float maxWidth = 1200f;
                float maxHeight = 1700f;
                if (pageWidth > maxWidth || pageHeight > maxHeight)
                {
                    float scale = Math.Min(maxWidth / pageWidth, maxHeight / pageHeight);
                    pageWidth *= scale;
                    pageHeight *= scale;
                }
                
                var pageSize = new iText.Kernel.Geom.PageSize(pageWidth, pageHeight);
                var page = pdfDoc.AddNewPage(pageSize);
                var canvas = new PdfCanvas(page);
                
                // Draw image to fill the page
                canvas.AddImageFittedIntoRectangle(
                    imageData,
                    new ITextRectangle(0, 0, pageWidth, pageHeight),
                    false);
                
                System.Diagnostics.Debug.WriteLine($"Created PDF from image: {outputPdfPath} ({pageWidth}x{pageHeight} points)");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating PDF from image: {ex.Message}");
                return false;
            }
        });
    }

    public void Close()
    {
        _currentFilePath = null;
        _pageCount = 0;
        _pdfBytes = null;
        _pageCache.Clear();
        _thumbnailCache.Clear();
    }

    /// <summary>
    /// Invalidate cache for a specific page (e.g., after rotation)
    /// </summary>
    public void InvalidatePageCache(int pageNumber)
    {
        _pageCache.InvalidatePage(pageNumber);
        _thumbnailCache.InvalidatePage(pageNumber);
    }

    /// <summary>
    /// Clear all caches (e.g., after zoom change)
    /// </summary>
    public void ClearPageCache()
    {
        _pageCache.Clear();
        // Keep thumbnail cache since thumbnails don't change with zoom
    }

    /// <summary>
    /// Get cache statistics for debugging
    /// </summary>
    public string GetCacheStats()
    {
        var pageStats = _pageCache.GetStats();
        var thumbStats = _thumbnailCache.GetStats();
        return $"PageCache: {pageStats.Count}/{pageStats.MaxSize}, ThumbnailCache: {thumbStats.Count}/{thumbStats.MaxSize}";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _pageCache.Dispose();
            _thumbnailCache.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
