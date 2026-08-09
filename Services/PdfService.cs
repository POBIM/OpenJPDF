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
    private const float ScreenToPdf = 72f / 96f;
    private const double BaseWpfDpi = 96.0;
    private const double PreviewRenderQuality = 2.0;
    private const int MaxPreviewRenderDpi = 600;

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

    // Base PDF bytes without user annotations - used for re-saving without double-baking
    // When saving, structural changes are applied to base, then annotations are overlaid
    private byte[]? _basePdfBytes;
    
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

    // PERFORMANCE FIX: Cache for PdfFont objects to avoid repeated font loading
    // Key: (fontFamily, isBold, isItalic, needsThai) -> PdfFont
    private static readonly Dictionary<(string, bool, bool, bool), PdfFont> _fontCache = new();

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
                _basePdfBytes = (byte[])_pdfBytes.Clone();
                
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
                
                System.Diagnostics.Debug.WriteLine($"[PERF] Loaded PDF into memory: {_pdfBytes.Length / 1024}KB, {_pageCount} pages (base bytes stored)");
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
            
            // Render above the displayed DPI and store matching bitmap DPI metadata.
            // WPF keeps the same DIP size, while zoomed text/vector edges stay crisp.
            double qualityScale = Math.Min(PreviewRenderQuality, MaxPreviewRenderDpi / (BaseWpfDpi * Math.Max(scale, 0.01f)));
            qualityScale = Math.Max(1.0, qualityScale);
            int dpi = Math.Max(24, (int)Math.Round(BaseWpfDpi * scale * qualityScale));
            double bitmapDpi = BaseWpfDpi * qualityScale;
            
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
            var result = ConvertSkiaBitmapToWpfDirect(skBitmap, bitmapDpi);
            
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
        
        // PERFORMANCE FIX: Don't use 'using' with stream assigned to StreamSource
        var bitmap = new BitmapImage();
        var stream = data.AsStream();
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
    private static BitmapSource ConvertSkiaBitmapToWpfDirect(SKBitmap skBitmap, double dpi = BaseWpfDpi)
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
                return CreateWriteableBitmap(convertedBitmap, dpi);
            }
            
            return CreateWriteableBitmap(skBitmap, dpi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PERF] Direct conversion failed, using PNG fallback: {ex.Message}");
            return ConvertSkiaBitmapToWpf(skBitmap);
        }
    }

    private static WriteableBitmap CreateWriteableBitmap(SKBitmap skBitmap, double dpi = BaseWpfDpi)
    {
        var writeableBitmap = new WriteableBitmap(
            skBitmap.Width,
            skBitmap.Height,
            dpi, dpi,
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

    public void SetPageRotations(IReadOnlyDictionary<int, int> pageRotations)
    {
        _pageRotations.Clear();

        foreach (var rotation in pageRotations)
        {
            int normalized = NormalizePdfRotation(rotation.Value);
            if (normalized != 0)
            {
                _pageRotations[rotation.Key] = normalized;
            }
        }

        _pageCache.Clear();
        _thumbnailCache.Clear();
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
    private bool ReorderPages(string sourceFile, string destFile, int[] pageOrder, byte[]? sourceBytes = null)
    {
        try
        {
            // Use in-memory bytes when possible to avoid file locking issues
            bool useMemoryStream = sourceBytes != null;
            
            using var memStream = useMemoryStream ? new MemoryStream(sourceBytes!, writable: false) : null;
            using var reader = useMemoryStream ? new PdfReader(memStream!) : new PdfReader(sourceFile);
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
    private bool DuplicatePages(string sourceFile, string destFile, List<int> pagesToDuplicate, byte[]? sourceBytes = null)
    {
        try
        {
            // Use in-memory bytes when possible to avoid file locking issues
            bool useMemoryStream = sourceBytes != null;
            
            using var memStream = useMemoryStream ? new MemoryStream(sourceBytes!, writable: false) : null;
            using var reader = useMemoryStream ? new PdfReader(memStream!) : new PdfReader(sourceFile);
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

    private readonly record struct PdfAnnotationBox(float Left, float Bottom, float Width, float Height)
    {
        public float Right => Left + Width;
        public float Top => Bottom + Height;
        public float CenterX => Left + Width / 2;
        public float CenterY => Bottom + Height / 2;
    }

    private static float DipsToPdfPoints(double value) => (float)value * ScreenToPdf;

    private static int NormalizePdfRotation(int rotation)
    {
        rotation %= 360;
        if (rotation < 0) rotation += 360;
        return rotation;
    }

    private static (float Width, float Height) GetDisplayPageSize(ITextRectangle mediaBox, int pageRotation)
    {
        int rotation = NormalizePdfRotation(pageRotation);
        return rotation is 90 or 270
            ? (mediaBox.GetHeight(), mediaBox.GetWidth())
            : (mediaBox.GetWidth(), mediaBox.GetHeight());
    }

    private static void ApplyDisplayToPdfTransform(PdfCanvas canvas, ITextRectangle mediaBox, int pageRotation)
    {
        float x = mediaBox.GetX();
        float y = mediaBox.GetY();
        float width = mediaBox.GetWidth();
        float height = mediaBox.GetHeight();

        switch (NormalizePdfRotation(pageRotation))
        {
            case 90:
                canvas.ConcatMatrix(0, 1, -1, 0, x + width, y);
                break;
            case 180:
                canvas.ConcatMatrix(-1, 0, 0, -1, x + width, y + height);
                break;
            case 270:
                canvas.ConcatMatrix(0, -1, 1, 0, x, y + height);
                break;
            default:
                canvas.ConcatMatrix(1, 0, 0, 1, x, y);
                break;
        }
    }

    private static PdfAnnotationBox ToDisplayBoxFromTopLeft(
        ITextRectangle mediaBox,
        int pageRotation,
        double xFromLeftDips,
        double yFromTopDips,
        float widthPoints,
        float heightPoints)
    {
        var (_, displayHeight) = GetDisplayPageSize(mediaBox, pageRotation);
        float left = DipsToPdfPoints(xFromLeftDips);
        float bottom = displayHeight - DipsToPdfPoints(yFromTopDips) - heightPoints;
        return new PdfAnnotationBox(left, bottom, widthPoints, heightPoints);
    }

    private static float ToDisplayX(double xFromLeftDips) => DipsToPdfPoints(xFromLeftDips);

    private static float ToDisplayYFromTop(ITextRectangle mediaBox, int pageRotation, double yFromTopDips)
    {
        var (_, displayHeight) = GetDisplayPageSize(mediaBox, pageRotation);
        return displayHeight - DipsToPdfPoints(yFromTopDips);
    }

    private static float GetAlignedTextX(
        PdfAnnotationBox box,
        float lineWidth,
        float padding,
        TextAlignment textAlignment)
    {
        return textAlignment switch
        {
            TextAlignment.Center => box.Left + (box.Width - lineWidth) / 2,
            TextAlignment.Right => box.Right - padding - lineWidth,
            _ => box.Left + padding
        };
    }

    public async Task<bool> SaveAsync(string filePath)
    {
        if (_currentFilePath == null && _basePdfBytes == null)
            return false;

        return await Task.Run(() =>
        {
            try
            {
                bool hasStructuralChanges = _pageOrder != null || _duplicatedPages.Count > 0 ||
                    _deletedPages.Count > 0 || _pageRotations.Count > 0 ||
                    _redactions.Count > 0 || _movedTexts.Count > 0 || _movedImages.Count > 0;

                // ============================================================
                // PHASE 1: Apply structural changes to base PDF
                // (reorder, duplicate, delete, rotate, redactions, moved content)
                // Result: intermediate PDF bytes WITHOUT user annotations
                // ============================================================
                byte[] intermediateBytes;

                if (hasStructuralChanges)
                {
                    string structuralTempFile = IoPath.GetTempFileName();
                    string sourceFile = _currentFilePath ?? string.Empty;

                    // If page order has changed, create a reordered PDF first
                    if (_pageOrder != null && _pageOrder.Length > 0)
                    {
                        string reorderedFile = IoPath.GetTempFileName();
                        byte[]? reorderSourceBytes = sourceFile == _currentFilePath ? _basePdfBytes : null;
                        if (ReorderPages(sourceFile, reorderedFile, _pageOrder, reorderSourceBytes))
                        {
                            sourceFile = reorderedFile;
                        }
                    }

                    // If pages need to be duplicated, handle that
                    if (_duplicatedPages.Count > 0)
                    {
                        string duplicatedFile = IoPath.GetTempFileName();
                        byte[]? duplicateSourceBytes = sourceFile == _currentFilePath ? _basePdfBytes : null;
                        if (DuplicatePages(sourceFile, duplicatedFile, _duplicatedPages, duplicateSourceBytes))
                        {
                            if (sourceFile != _currentFilePath && IoFile.Exists(sourceFile))
                            {
                                try { IoFile.Delete(sourceFile); } catch { }
                            }
                            sourceFile = duplicatedFile;
                        }
                    }

                    // Apply deletions, rotations, redactions, moved content
                    bool useMemoryStream = sourceFile == _currentFilePath && _basePdfBytes != null;
                    using var memStream = useMemoryStream ? new MemoryStream(_basePdfBytes!, writable: false) : null;
                    using (var reader = useMemoryStream ? new PdfReader(memStream!) : new PdfReader(sourceFile))
                    using (var writer = new PdfWriter(structuralTempFile))
                    using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer))
                    {
                        // Apply deletions (in reverse order to maintain page numbers)
                        var pagesToDelete = _deletedPages.OrderByDescending(p => p).ToList();
                        foreach (var pageNum in pagesToDelete)
                        {
                            if (pageNum < pdfDoc.GetNumberOfPages())
                            {
                                pdfDoc.RemovePage(pageNum + 1);
                            }
                        }

                        // Apply rotations
                        foreach (var rotation in _pageRotations)
                        {
                            int adjustedPage = rotation.Key + 1;
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
                                var canvas = new PdfCanvas(page);
                                PdfFont font = GetThaiCompatibleFont(movedText.FontName, false, false, movedText.Text);
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
                                    canvas.AddImageWithTransformationMatrix(
                                        imageData,
                                        movedImage.Width,
                                        0,
                                        0,
                                        movedImage.Height,
                                        movedImage.X,
                                        movedImage.Y,
                                        false);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error adding moved image: {ex.Message}");
                                }
                            }
                        }

                        _pageCount = pdfDoc.GetNumberOfPages();
                    }

                    // Read intermediate bytes (structural changes applied, no annotations)
                    intermediateBytes = IoFile.ReadAllBytes(structuralTempFile);

                    // Clean up temp files
                    try { IoFile.Delete(structuralTempFile); } catch { }
                    if (sourceFile != _currentFilePath && IoFile.Exists(sourceFile))
                    {
                        try { IoFile.Delete(sourceFile); } catch { }
                    }
                }
                else
                {
                    // No structural changes - use base bytes as-is
                    intermediateBytes = _basePdfBytes ?? _pdfBytes ?? throw new InvalidOperationException("No PDF bytes available");
                }

                // ============================================================
                // PHASE 2: Apply user annotations on top of intermediate PDF
                // (text, image, shape annotations)
                // Result: final PDF with everything baked in for external viewers
                // ============================================================
                bool hasAnnotations = _textAnnotations.Count > 0 || _imageAnnotations.Count > 0 || _shapeAnnotations.Count > 0;

                if (hasAnnotations)
                {
                    string annotatedTempFile = IoPath.GetTempFileName();
                    using (var intermediateStream = new MemoryStream(intermediateBytes, writable: false))
                    using (var reader = new PdfReader(intermediateStream))
                    using (var writer = new PdfWriter(annotatedTempFile))
                    using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer))
                    {
                        // Apply text annotations
                        foreach (var textAnn in _textAnnotations)
                        {
                            int adjustedPage = textAnn.PageNumber + 1;
                            if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages())
                            {
                                var page = pdfDoc.GetPage(adjustedPage);
                                var mediaBox = page.GetMediaBox();
                                int pageRotation = page.GetRotation();
                                var canvas = new PdfCanvas(page);
                                PdfFont font = GetThaiCompatibleFont(textAnn.FontFamily, textAnn.IsBold, textAnn.IsItalic, textAnn.Text);

                                float pdfFontSize = textAnn.FontSize;
                                float padding = DipsToPdfPoints(2);
                                float borderWidthPdf = DipsToPdfPoints(textAnn.BorderWidth);

                                float pdfTextWidth = font.GetWidth(textAnn.Text, pdfFontSize);

                                float boxWidth = (textAnn.Width > 0)
                                    ? DipsToPdfPoints(textAnn.Width)
                                    : pdfTextWidth + (padding * 2);
                                float boxHeight = (textAnn.Height > 0)
                                    ? DipsToPdfPoints(textAnn.Height)
                                    : pdfFontSize * 1.2f + (padding * 2);

                                var box = ToDisplayBoxFromTopLeft(mediaBox, pageRotation, textAnn.X, textAnn.Y, boxWidth, boxHeight);

                                // Text baseline positioning:
                                // Use WPF-measured BaselineOffset for pixel-perfect match
                                float baselineFromTop;
                                if (textAnn.BaselineOffset > 0)
                                {
                                    baselineFromTop = DipsToPdfPoints(textAnn.BaselineOffset);
                                }
                                else
                                {
                                    try
                                    {
                                        var fontProgram = font.GetFontProgram();
                                        var fontMetrics = fontProgram.GetFontMetrics();
                                        float ascenderUnits = fontMetrics.GetTypoAscender();
                                        float unitsPerEm = fontMetrics.GetUnitsPerEm();
                                        if (unitsPerEm <= 0) unitsPerEm = 1000f;
                                        baselineFromTop = ascenderUnits / unitsPerEm * pdfFontSize;
                                        if (baselineFromTop <= 0 || baselineFromTop > pdfFontSize * 1.5f)
                                            baselineFromTop = pdfFontSize * 0.8f;
                                    }
                                    catch
                                    {
                                        baselineFromTop = pdfFontSize * 0.8f;
                                    }
                                }
                                float textY = box.Top - padding - baselineFromTop;

                                bool hasRotation = Math.Abs(textAnn.Rotation) > 0.1;
                                canvas.SaveState();
                                ApplyDisplayToPdfTransform(canvas, mediaBox, pageRotation);
                                if (hasRotation)
                                {
                                    float centerX = box.CenterX;
                                    float centerY = box.CenterY;
                                    double angleRad = -textAnn.Rotation * Math.PI / 180;
                                    float cos = (float)Math.Cos(angleRad);
                                    float sin = (float)Math.Sin(angleRad);
                                    canvas.SaveState();
                                    canvas.ConcatMatrix(1, 0, 0, 1, centerX, centerY);
                                    canvas.ConcatMatrix(cos, sin, -sin, cos, 0, 0);
                                    canvas.ConcatMatrix(1, 0, 0, 1, -centerX, -centerY);
                                }

                                if (!string.IsNullOrEmpty(textAnn.BackgroundColor) && textAnn.BackgroundColor != "Transparent")
                                {
                                    var bgColor = ParseColor(textAnn.BackgroundColor);
                                    canvas.SaveState()
                                        .SetFillColor(bgColor)
                                        .Rectangle(box.Left, box.Bottom, box.Width, box.Height)
                                        .Fill()
                                        .RestoreState();
                                }

                                if (!string.IsNullOrEmpty(textAnn.BorderColor) && textAnn.BorderColor != "Transparent" && textAnn.BorderWidth > 0)
                                {
                                    var strokeColor = ParseColor(textAnn.BorderColor);
                                    canvas.SaveState()
                                        .SetStrokeColor(strokeColor)
                                        .SetLineWidth(borderWidthPdf)
                                        .Rectangle(box.Left, box.Bottom, box.Width, box.Height)
                                        .Stroke()
                                        .RestoreState();
                                }

                                var textColorParsed = ParseColor(textAnn.Color);
                                float lineHeight = pdfFontSize * 1.2f;
                                string[] textLines = textAnn.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                                for (int lineIdx = 0; lineIdx < textLines.Length; lineIdx++)
                                {
                                    float lineWidth = font.GetWidth(textLines[lineIdx], pdfFontSize);
                                    float textX = GetAlignedTextX(box, lineWidth, padding, textAnn.TextAlignment);
                                    float lineY = textY - (lineIdx * lineHeight);
                                    canvas.BeginText()
                                        .SetFontAndSize(font, pdfFontSize)
                                        .SetFillColor(textColorParsed)
                                        .MoveText(textX, lineY)
                                        .ShowText(textLines[lineIdx])
                                        .EndText();
                                }

                                if (textAnn.IsUnderline)
                                {
                                    for (int lineIdx = 0; lineIdx < textLines.Length; lineIdx++)
                                    {
                                        float lineWidth = font.GetWidth(textLines[lineIdx], pdfFontSize);
                                        float textX = GetAlignedTextX(box, lineWidth, padding, textAnn.TextAlignment);
                                        float lineY = textY - (lineIdx * lineHeight);
                                        canvas.SaveState()
                                            .SetStrokeColor(textColorParsed)
                                            .SetLineWidth(DipsToPdfPoints(0.5))
                                            .MoveTo(textX, lineY - 1)
                                            .LineTo(textX + lineWidth, lineY - 1)
                                            .Stroke()
                                            .RestoreState();
                                    }
                                }

                                if (hasRotation)
                                {
                                    canvas.RestoreState();
                                }
                                canvas.RestoreState();
                            }
                        }

                        // Apply image annotations
                        foreach (var imgAnn in _imageAnnotations)
                        {
                            int adjustedPage = imgAnn.PageNumber + 1;
                            if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages() && IoFile.Exists(imgAnn.ImagePath))
                            {
                                var page = pdfDoc.GetPage(adjustedPage);
                                var mediaBox = page.GetMediaBox();
                                int pageRotation = page.GetRotation();
                                var box = ToDisplayBoxFromTopLeft(
                                    mediaBox,
                                    pageRotation,
                                    imgAnn.X,
                                    imgAnn.Y,
                                    DipsToPdfPoints(imgAnn.Width),
                                    DipsToPdfPoints(imgAnn.Height));

                                var imageData = ImageDataFactory.Create(imgAnn.ImagePath);
                                var canvas = new PdfCanvas(page);
                                canvas.SaveState();
                                ApplyDisplayToPdfTransform(canvas, mediaBox, pageRotation);

                                if (Math.Abs(imgAnn.Rotation) > 0.1)
                                {
                                    float centerX = box.CenterX;
                                    float centerY = box.CenterY;
                                    double angleRad = -imgAnn.Rotation * Math.PI / 180;
                                    float cos = (float)Math.Cos(angleRad);
                                    float sin = (float)Math.Sin(angleRad);
                                    canvas.SaveState();
                                    canvas.ConcatMatrix(1, 0, 0, 1, centerX, centerY);
                                    canvas.ConcatMatrix(cos, sin, -sin, cos, 0, 0);
                                    canvas.ConcatMatrix(1, 0, 0, 1, -centerX, -centerY);
                                    canvas.AddImageWithTransformationMatrix(imageData,
                                        box.Width, 0, 0, box.Height, box.Left, box.Bottom, false);
                                    canvas.RestoreState();
                                }
                                else
                                {
                                    canvas.AddImageWithTransformationMatrix(imageData,
                                        box.Width, 0, 0, box.Height, box.Left, box.Bottom, false);
                                }
                                canvas.RestoreState();
                            }
                        }

                        // Apply shape annotations
                        foreach (var shapeAnn in _shapeAnnotations)
                        {
                            int adjustedPage = shapeAnn.PageNumber + 1;
                            if (adjustedPage > 0 && adjustedPage <= pdfDoc.GetNumberOfPages())
                            {
                                var page = pdfDoc.GetPage(adjustedPage);
                                var mediaBox = page.GetMediaBox();
                                var canvas = new PdfCanvas(page);
                                int pageRotation = page.GetRotation();
                                var box = ToDisplayBoxFromTopLeft(
                                    mediaBox,
                                    pageRotation,
                                    shapeAnn.X,
                                    shapeAnn.Y,
                                    DipsToPdfPoints(shapeAnn.Width),
                                    DipsToPdfPoints(shapeAnn.Height));
                                float pdfStrokeWidth = DipsToPdfPoints(shapeAnn.StrokeWidth);
                                bool hasRotation = Math.Abs(shapeAnn.Rotation) > 0.1;
                                float rotationCenterX = box.CenterX;
                                float rotationCenterY = box.CenterY;

                                canvas.SaveState();
                                ApplyDisplayToPdfTransform(canvas, mediaBox, pageRotation);
                                bool hasFill = !string.IsNullOrEmpty(shapeAnn.FillColor) && shapeAnn.FillColor != "Transparent";
                                if (hasFill) canvas.SetFillColor(ParseColor(shapeAnn.FillColor));
                                canvas.SetStrokeColor(ParseColor(shapeAnn.StrokeColor));
                                canvas.SetLineWidth(pdfStrokeWidth);

                                if (shapeAnn.ShapeType == ShapeType.Line)
                                {
                                    float lineX1 = ToDisplayX(shapeAnn.X);
                                    float lineY1 = ToDisplayYFromTop(mediaBox, pageRotation, shapeAnn.Y);
                                    float lineX2 = ToDisplayX(shapeAnn.X2);
                                    float lineY2 = ToDisplayYFromTop(mediaBox, pageRotation, shapeAnn.Y2);
                                    rotationCenterX = (lineX1 + lineX2) / 2;
                                    rotationCenterY = (lineY1 + lineY2) / 2;
                                }

                                if (hasRotation)
                                {
                                    double angleRad = -shapeAnn.Rotation * Math.PI / 180;
                                    float cos = (float)Math.Cos(angleRad);
                                    float sin = (float)Math.Sin(angleRad);
                                    canvas.ConcatMatrix(1, 0, 0, 1, rotationCenterX, rotationCenterY);
                                    canvas.ConcatMatrix(cos, sin, -sin, cos, 0, 0);
                                    canvas.ConcatMatrix(1, 0, 0, 1, -rotationCenterX, -rotationCenterY);
                                }

                                switch (shapeAnn.ShapeType)
                                {
                                    case ShapeType.Rectangle:
                                        canvas.Rectangle(box.Left, box.Bottom, box.Width, box.Height);
                                        if (hasFill) canvas.FillStroke(); else canvas.Stroke();
                                        break;
                                    case ShapeType.Ellipse:
                                        canvas.Ellipse(box.Left, box.Bottom, box.Right, box.Top);
                                        if (hasFill) canvas.FillStroke(); else canvas.Stroke();
                                        break;
                                    case ShapeType.Line:
                                        float pdfX2 = ToDisplayX(shapeAnn.X2);
                                        float y1 = ToDisplayYFromTop(mediaBox, pageRotation, shapeAnn.Y);
                                        float y2 = ToDisplayYFromTop(mediaBox, pageRotation, shapeAnn.Y2);
                                        canvas.MoveTo(box.Left, y1);
                                        canvas.LineTo(pdfX2, y2);
                                        canvas.Stroke();
                                        break;
                                }
                                canvas.RestoreState();
                            }
                        }

                        _pageCount = pdfDoc.GetNumberOfPages();
                    }

                    // Write annotated file to target
                    if (IoFile.Exists(filePath))
                        IoFile.Delete(filePath);
                    IoFile.Move(annotatedTempFile, filePath);
                }
                else
                {
                    // No annotations - write intermediate directly
                    if (IoFile.Exists(filePath))
                        IoFile.Delete(filePath);
                    IoFile.WriteAllBytes(filePath, intermediateBytes);

                    // Update page count from intermediate bytes
                    using var countStream = new MemoryStream(intermediateBytes, writable: false);
                    using var countReader = new PdfReader(countStream);
                    using var countDoc = new iText.Kernel.Pdf.PdfDocument(countReader);
                    _pageCount = countDoc.GetNumberOfPages();
                }

                // ============================================================
                // PHASE 3: Update internal state to the actual saved file.
                // Annotations are now baked into filePath, so stale overlay
                // lists must be cleared to prevent accidental double-save.
                // ============================================================
                _pdfBytes = IoFile.ReadAllBytes(filePath);
                _basePdfBytes = (byte[])_pdfBytes.Clone();
                _currentFilePath = filePath;

                // Clear structural modifications (consumed by Phase 1)
                _pageRotations.Clear();
                _deletedPages.Clear();
                _duplicatedPages.Clear();
                _pageOrder = null;
                _redactions.Clear();
                _movedTexts.Clear();
                _movedImages.Clear();

                _textAnnotations.Clear();
                _imageAnnotations.Clear();
                _shapeAnnotations.Clear();

                // Clear render caches (page images need re-rendering from new base)
                _pageCache.Clear();
                _thumbnailCache.Clear();

                System.Diagnostics.Debug.WriteLine($"[SAVE] Saved successfully. Bytes={_pdfBytes.Length / 1024}KB, annotations cleared after baking.");

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
    /// Reload PDF bytes from the saved file and clear transient annotation lists.
    /// This ensures rendered pages match the saved file (including baked annotations).
    /// </summary>
    public async Task<bool> ReloadBytesFromFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!IoFile.Exists(filePath)) return false;

                _pdfBytes = IoFile.ReadAllBytes(filePath);
                _basePdfBytes = (byte[])_pdfBytes.Clone();
                _currentFilePath = filePath;

                // Update page count
                using var memStream = new MemoryStream(_pdfBytes, writable: false);
                using var reader = new PdfReader(memStream);
                using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
                _pageCount = pdfDoc.GetNumberOfPages();

                // Clear caches so pages re-render from new bytes
                _pageCache.Clear();
                _thumbnailCache.Clear();

                // Clear structural state (already applied and saved)
                _pageRotations.Clear();
                _deletedPages.Clear();
                _duplicatedPages.Clear();
                _redactions.Clear();
                _movedTexts.Clear();
                _movedImages.Clear();
                _pageOrder = null;

                _textAnnotations.Clear();
                _imageAnnotations.Clear();
                _shapeAnnotations.Clear();

                System.Diagnostics.Debug.WriteLine($"[RELOAD] Reloaded from file: {_pdfBytes.Length / 1024}KB, {_pageCount} pages, annotations cleared");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reloading PDF bytes: {ex.Message}");
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
    /// PERFORMANCE FIX: Uses cache to avoid repeated font loading
    /// </summary>
    private static PdfFont GetThaiCompatibleFont(string fontFamily, bool isBold, bool isItalic, string? textContent = null)
    {
        string systemFontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        bool needsThai = textContent != null && ContainsThai(textContent);
        
        // PERFORMANCE FIX: Check cache first
        var cacheKey = (fontFamily.ToLower(), isBold, isItalic, needsThai);
        lock (_fontCache)
        {
            if (_fontCache.TryGetValue(cacheKey, out var cachedFont))
            {
                System.Diagnostics.Debug.WriteLine($"[FONT CACHE] Hit: '{fontFamily}' (bold={isBold}, italic={isItalic}, thai={needsThai})");
                return cachedFont;
            }
        }
        
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
                    CacheFont(cacheKey, font);
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
                        CacheFont(cacheKey, font);
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
                        CacheFont(cacheKey, font);
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
                            CacheFont(cacheKey, font);
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
                        CacheFont(cacheKey, font);
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
                    CacheFont(cacheKey, font);
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
                CacheFont(cacheKey, font);
                return font;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FONT] Font error: {ex.Message}");
        }

        // Last resort - Helvetica (no Thai support)
        System.Diagnostics.Debug.WriteLine($"[FONT] WARNING: Using Helvetica - Thai will not display correctly! Requested: '{fontFamily}'");
        var helvetica = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        CacheFont(cacheKey, helvetica);
        return helvetica;
    }

    /// <summary>
    /// PERFORMANCE FIX: Helper method to cache a font
    /// </summary>
    private static void CacheFont((string, bool, bool, bool) key, PdfFont font)
    {
        lock (_fontCache)
        {
            _fontCache[key] = font;
            System.Diagnostics.Debug.WriteLine($"[FONT CACHE] Added: key=({key.Item1}, bold={key.Item2}, italic={key.Item3}, thai={key.Item4}), cache size={_fontCache.Count}");
        }
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

    public async Task<bool> ImportPdfPagesAsync(string[] inputFiles, int insertIndex)
    {
        if (_pdfBytes == null && _basePdfBytes == null)
            return false;

        return await Task.Run(() =>
        {
            try
            {
                var sourceBytes = _basePdfBytes ?? _pdfBytes!;
                using var sourceStream = new MemoryStream(sourceBytes, writable: false);
                using var reader = new PdfReader(sourceStream);
                using var outputStream = new MemoryStream();
                using (var writer = new PdfWriter(outputStream))
                using (var destDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer))
                {
                    int currentPageCount = destDoc.GetNumberOfPages();
                    int insertionPoint = Math.Clamp(insertIndex, 0, currentPageCount);

                    foreach (var file in inputFiles.Where(IoFile.Exists))
                    {
                        using var importReader = new PdfReader(file);
                        using var importDoc = new iText.Kernel.Pdf.PdfDocument(importReader);
                        int importPageCount = importDoc.GetNumberOfPages();
                        if (importPageCount == 0)
                            continue;

                        if (insertionPoint >= destDoc.GetNumberOfPages())
                        {
                            importDoc.CopyPagesTo(1, importPageCount, destDoc);
                        }
                        else
                        {
                            importDoc.CopyPagesTo(1, importPageCount, destDoc, insertionPoint + 1);
                        }

                        insertionPoint += importPageCount;
                    }

                    _pageCount = destDoc.GetNumberOfPages();
                }

                _pdfBytes = outputStream.ToArray();
                _basePdfBytes = (byte[])_pdfBytes.Clone();

                _pageRotations.Clear();
                _deletedPages.Clear();
                _duplicatedPages.Clear();
                _pageOrder = null;
                _redactions.Clear();
                _movedTexts.Clear();
                _movedImages.Clear();
                _textAnnotations.Clear();
                _imageAnnotations.Clear();
                _shapeAnnotations.Clear();
                _pageCache.Clear();
                _thumbnailCache.Clear();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error importing PDF pages: {ex.Message}");
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
                        
                        // Apply custom text boxes (with page scope check)
                        foreach (var customBox in config.CustomTextBoxes)
                        {
                            if (customBox.ShouldApplyToPage(pageNum, totalPages))
                            {
                                DrawCustomTextBox(canvas, customBox, pageNum, totalPages, fileNameToUse, now, pdfDoc);
                            }
                        }
                        
                        // Apply custom image boxes (with page scope check)
                        foreach (var imageBox in config.CustomImageBoxes)
                        {
                            if (imageBox.ShouldApplyToPage(pageNum, totalPages))
                            {
                                DrawCustomImageBox(canvas, imageBox, pdfDoc, pageNum);
                            }
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
                    
                    canvas.AddImageWithTransformationMatrix(imageData, imgWidth, 0, 0, imgHeight, drawX, drawY, false);
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
    /// Draw a custom text box at specified position (supports multiline and rotation)
    /// </summary>
    private void DrawCustomTextBox(PdfCanvas canvas, CustomTextBox textBox, int currentPage, int totalPages,
        string fileName, DateTime date, iText.Kernel.Pdf.PdfDocument pdfDoc)
    {
        string text = textBox.GetFormattedText(currentPage, totalPages, fileName, date);
        
        System.Diagnostics.Debug.WriteLine($"DrawCustomTextBox: Label={textBox.Label}, Text='{text}', Offset=({textBox.OffsetX}, {textBox.OffsetY}), Rotation={textBox.Rotation}");
        
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
            
            // Apply rotation if needed
            bool hasRotation = Math.Abs(textBox.Rotation) > 0.001f;
            if (hasRotation)
            {
                canvas.SaveState();
                // Calculate center of the text box for rotation pivot
                float centerX = x + textBox.BoxWidth / 2f;
                float centerY = y + textBox.BoxHeight / 2f;
                
                // Convert degrees to radians (negative because PDF rotation is counter-clockwise)
                double radians = -textBox.Rotation * Math.PI / 180.0;
                float cos = (float)Math.Cos(radians);
                float sin = (float)Math.Sin(radians);
                
                // Apply rotation transform around center point
                // Matrix: [cos, sin, -sin, cos, cx - cx*cos + cy*sin, cy - cx*sin - cy*cos]
                float tx = centerX - centerX * cos + centerY * sin;
                float ty = centerY - centerX * sin - centerY * cos;
                canvas.ConcatMatrix(cos, sin, -sin, cos, tx, ty);
            }
            
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
                
                for (int i = 0; i < lines.Length; i++)
                {
                    float lineY = topY - (i * lineHeight);
                    
                    // Stop if we're below the box
                    if (lineY < y + padding) break;
                    
                    canvas.BeginText()
                        .SetFontAndSize(font, fontSize)
                        .SetFillColor(textColor)
                        .MoveText(textX, lineY)
                        .ShowText(lines[i])
                        .EndText();
                }
            }
            
            // Restore state if rotation was applied
            if (hasRotation)
            {
                canvas.RestoreState();
            }
            
            System.Diagnostics.Debug.WriteLine($"Drew custom text box at ({x}, {y}) with size {textBox.BoxWidth}x{textBox.BoxHeight}, rotation={textBox.Rotation}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error drawing custom text box: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Draw a custom image box at specified position (supports rotation and opacity)
    /// </summary>
    private void DrawCustomImageBox(PdfCanvas canvas, CustomImageBox imageBox, iText.Kernel.Pdf.PdfDocument pdfDoc, int currentPage)
    {
        System.Diagnostics.Debug.WriteLine($"DrawCustomImageBox: Label={imageBox.Label}, Path='{imageBox.ImagePath}', Offset=({imageBox.OffsetX}, {imageBox.OffsetY}), Rotation={imageBox.Rotation}, Opacity={imageBox.Opacity}");
        
        if (string.IsNullOrEmpty(imageBox.ImagePath) || !IoFile.Exists(imageBox.ImagePath))
        {
            System.Diagnostics.Debug.WriteLine($"Image file not found or empty: {imageBox.ImagePath}");
            return;
        }
        
        try
        {
            var imageData = ImageDataFactory.Create(imageBox.ImagePath);
            
            float x = imageBox.OffsetX;
            float y = imageBox.OffsetY;
            float width = imageBox.Width;
            float height = imageBox.Height;
            
            canvas.SaveState();
            
            // Apply opacity if not fully opaque
            if (imageBox.Opacity < 1.0f)
            {
                var gState = new iText.Kernel.Pdf.Extgstate.PdfExtGState();
                gState.SetFillOpacity(imageBox.Opacity);
                canvas.SetExtGState(gState);
            }
            
            // Apply rotation if needed
            bool hasRotation = Math.Abs(imageBox.Rotation) > 0.001f;
            if (hasRotation)
            {
                // Calculate center of the image box for rotation pivot
                float centerX = x + width / 2f;
                float centerY = y + height / 2f;
                
                // Convert degrees to radians (negative because PDF rotation is counter-clockwise)
                double radians = -imageBox.Rotation * Math.PI / 180.0;
                float cos = (float)Math.Cos(radians);
                float sin = (float)Math.Sin(radians);
                
                // Apply rotation transform around center point
                float tx = centerX - centerX * cos + centerY * sin;
                float ty = centerY - centerX * sin - centerY * cos;
                canvas.ConcatMatrix(cos, sin, -sin, cos, tx, ty);
            }
            
            // Draw the image using exact fill semantics to match WPF Stretch.Fill preview.
            canvas.AddImageWithTransformationMatrix(imageData, width, 0, 0, height, x, y, false);
            
            canvas.RestoreState();
            
            System.Diagnostics.Debug.WriteLine($"Drew custom image box at ({x}, {y}) with size {width}x{height}, rotation={imageBox.Rotation}, opacity={imageBox.Opacity}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error drawing custom image box: {ex.Message}");
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
        _basePdfBytes = null;
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
