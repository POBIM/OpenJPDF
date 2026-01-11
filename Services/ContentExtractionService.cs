// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Xobject;
using iText.Kernel.Geom;
using OpenJPDF.Models;

namespace OpenJPDF.Services;

/// <summary>
/// Service for extracting existing content (text and images) from PDF pages.
/// Uses iText7's canvas parser to identify and locate content.
/// </summary>
public class ContentExtractionService
{
    private readonly string _filePath;

    public ContentExtractionService(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Extract all text elements from a specific page with their positions.
    /// </summary>
    public List<ExtractedTextElement> ExtractTextFromPage(int pageNumber)
    {
        var results = new List<ExtractedTextElement>();

        try
        {
            using var reader = new PdfReader(_filePath);
            using var pdfDoc = new PdfDocument(reader);

            if (pageNumber < 0 || pageNumber >= pdfDoc.GetNumberOfPages())
                return results;

            var page = pdfDoc.GetPage(pageNumber + 1); // iText uses 1-based indexing
            var strategy = new TextChunkExtractionStrategy();

            var processor = new PdfCanvasProcessor(strategy);
            processor.ProcessPageContent(page);

            var chunks = strategy.GetTextChunks();

            // Merge nearby chunks with same font into blocks
            var mergedBlocks = MergeTextChunks(chunks, pageNumber);
            results.AddRange(mergedBlocks);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting text from page {pageNumber}: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Extract all images from a specific page with their positions.
    /// </summary>
    public List<ExtractedImageElement> ExtractImagesFromPage(int pageNumber)
    {
        var results = new List<ExtractedImageElement>();

        try
        {
            using var reader = new PdfReader(_filePath);
            using var pdfDoc = new PdfDocument(reader);

            if (pageNumber < 0 || pageNumber >= pdfDoc.GetNumberOfPages())
                return results;

            var page = pdfDoc.GetPage(pageNumber + 1); // iText uses 1-based indexing
            var strategy = new ImageExtractionStrategy();

            var processor = new PdfCanvasProcessor(strategy);
            processor.ProcessPageContent(page);

            var images = strategy.GetImages();

            foreach (var img in images)
            {
                results.Add(new ExtractedImageElement
                {
                    PageNumber = pageNumber,
                    X = img.X,
                    Y = img.Y,
                    Width = img.Width,
                    Height = img.Height,
                    ImageBytes = img.ImageBytes,
                    Format = img.Format,
                    OriginalX = img.X,
                    OriginalY = img.Y,
                    OriginalWidth = img.Width,
                    OriginalHeight = img.Height
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting images from page {pageNumber}: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Merge nearby text chunks that likely belong together into blocks.
    /// </summary>
    private List<ExtractedTextElement> MergeTextChunks(List<TextChunkInfo> chunks, int pageNumber)
    {
        var results = new List<ExtractedTextElement>();

        if (chunks.Count == 0) return results;

        // Sort chunks by Y (top to bottom), then X (left to right)
        var sortedChunks = chunks.OrderByDescending(c => c.Y).ThenBy(c => c.X).ToList();

        // Group chunks into lines (same Y position within tolerance)
        var lines = new List<List<TextChunkInfo>>();
        var currentLine = new List<TextChunkInfo> { sortedChunks[0] };
        float lineY = sortedChunks[0].Y;
        float lineTolerance = sortedChunks[0].FontSize * 0.5f;

        for (int i = 1; i < sortedChunks.Count; i++)
        {
            var chunk = sortedChunks[i];

            // Check if same line (Y within tolerance)
            if (Math.Abs(chunk.Y - lineY) <= lineTolerance)
            {
                currentLine.Add(chunk);
            }
            else
            {
                // Start new line
                lines.Add(currentLine);
                currentLine = new List<TextChunkInfo> { chunk };
                lineY = chunk.Y;
                lineTolerance = chunk.FontSize * 0.5f;
            }
        }
        lines.Add(currentLine);

        // Convert lines to ExtractedTextElement
        foreach (var line in lines)
        {
            // Sort chunks in line by X position
            var sortedLine = line.OrderBy(c => c.X).ToList();

            // Merge chunks that are close together
            var mergedText = new System.Text.StringBuilder();
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            string fontName = sortedLine[0].FontName;
            float fontSize = sortedLine[0].FontSize;
            string? color = sortedLine[0].Color;

            float lastEndX = float.MinValue;
            float spaceThreshold = fontSize * 0.3f;

            foreach (var chunk in sortedLine)
            {
                // Add space if there's a gap
                if (lastEndX != float.MinValue && chunk.X - lastEndX > spaceThreshold)
                {
                    mergedText.Append(' ');
                }

                mergedText.Append(chunk.Text);

                minX = Math.Min(minX, chunk.X);
                maxX = Math.Max(maxX, chunk.X + chunk.Width);
                minY = Math.Min(minY, chunk.Y);
                maxY = Math.Max(maxY, chunk.Y + chunk.Height);

                lastEndX = chunk.X + chunk.Width;
            }

            var text = mergedText.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                results.Add(new ExtractedTextElement
                {
                    PageNumber = pageNumber,
                    Text = text,
                    X = minX,
                    Y = minY,
                    Width = maxX - minX,
                    Height = maxY - minY,
                    FontName = fontName,
                    FontSize = fontSize,
                    Color = color,
                    OriginalX = minX,
                    OriginalY = minY,
                    OriginalText = text
                });
            }
        }

        return results;
    }
}

/// <summary>
/// Information about a text chunk extracted from PDF.
/// </summary>
internal class TextChunkInfo
{
    public string Text { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public string FontName { get; set; } = string.Empty;
    public float FontSize { get; set; }
    public string? Color { get; set; }
}

/// <summary>
/// Information about an image extracted from PDF.
/// </summary>
internal class ImageInfo
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
    public string Format { get; set; } = "png";
}

/// <summary>
/// Custom IEventListener for extracting text chunks with position information.
/// </summary>
internal class TextChunkExtractionStrategy : IEventListener
{
    private readonly List<TextChunkInfo> _chunks = new();

    public ICollection<EventType> GetSupportedEvents()
    {
        return new List<EventType> { EventType.RENDER_TEXT };
    }

    public void EventOccurred(IEventData data, EventType type)
    {
        if (type != EventType.RENDER_TEXT) return;

        var renderInfo = (TextRenderInfo)data;
        var text = renderInfo.GetText();

        if (string.IsNullOrEmpty(text)) return;

        try
        {
            var baseline = renderInfo.GetBaseline();
            var ascentLine = renderInfo.GetAscentLine();

            // Get position from baseline
            var startPoint = baseline.GetStartPoint();
            var endPoint = baseline.GetEndPoint();

            float x = startPoint.Get(0);
            float y = startPoint.Get(1);
            float width = endPoint.Get(0) - startPoint.Get(0);

            // Calculate height from baseline to ascent
            var ascentStart = ascentLine.GetStartPoint();
            float height = Math.Abs(ascentStart.Get(1) - y);

            // Get font info
            var font = renderInfo.GetFont();
            string fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "Unknown";
            float fontSize = 0;

            // Try to get font size from the text rendering matrix
            try
            {
                var gs = renderInfo.GetGraphicsState();
                if (gs != null)
                {
                    fontSize = gs.GetFontSize();
                }
            }
            catch
            {
                // Fallback: estimate from height
                fontSize = height > 0 ? height : 12f;
            }

            if (fontSize <= 0) fontSize = 12f;
            if (height <= 0) height = fontSize;
            if (width <= 0) width = text.Length * fontSize * 0.5f;

            // Try to get color
            string? color = null;
            try
            {
                var gs = renderInfo.GetGraphicsState();
                var fillColor = gs?.GetFillColor();
                if (fillColor != null)
                {
                    var colorValue = fillColor.GetColorValue();
                    if (colorValue != null && colorValue.Length >= 3)
                    {
                        int r = (int)(colorValue[0] * 255);
                        int g = (int)(colorValue[1] * 255);
                        int b = (int)(colorValue[2] * 255);
                        color = $"#{r:X2}{g:X2}{b:X2}";
                    }
                }
            }
            catch
            {
                // Ignore color extraction errors
            }

            _chunks.Add(new TextChunkInfo
            {
                Text = text,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                FontName = fontName,
                FontSize = fontSize,
                Color = color
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing text chunk: {ex.Message}");
        }
    }

    public List<TextChunkInfo> GetTextChunks() => _chunks;
}

/// <summary>
/// Custom IEventListener for extracting images with position information.
/// </summary>
internal class ImageExtractionStrategy : IEventListener
{
    private readonly List<ImageInfo> _images = new();

    public ICollection<EventType> GetSupportedEvents()
    {
        return new List<EventType> { EventType.RENDER_IMAGE };
    }

    public void EventOccurred(IEventData data, EventType type)
    {
        if (type != EventType.RENDER_IMAGE) return;

        var renderInfo = (ImageRenderInfo)data;

        try
        {
            var image = renderInfo.GetImage();
            if (image == null) return;

            // Get transformation matrix to determine position and size
            var ctm = renderInfo.GetImageCtm();
            float x = ctm.Get(Matrix.I31);
            float y = ctm.Get(Matrix.I32);
            float width = Math.Abs(ctm.Get(Matrix.I11));
            float height = Math.Abs(ctm.Get(Matrix.I22));

            // Handle rotation in transformation matrix
            if (width == 0 && height == 0)
            {
                width = Math.Abs(ctm.Get(Matrix.I21));
                height = Math.Abs(ctm.Get(Matrix.I12));
            }

            // Get image bytes
            byte[] imageBytes;
            string format = "png";

            try
            {
                imageBytes = image.GetImageBytes();

                // Determine format from image type
                var imageType = image.IdentifyImageType();
                format = imageType.ToString().ToLower();
                if (format == "jpeg2000") format = "jp2";
                if (string.IsNullOrEmpty(format)) format = "png";
            }
            catch
            {
                // If direct bytes fail, try to get raw data
                try
                {
                    var pdfStream = image.GetPdfObject();
                    imageBytes = pdfStream?.GetBytes() ?? Array.Empty<byte>();
                }
                catch
                {
                    imageBytes = Array.Empty<byte>();
                }
            }

            if (imageBytes.Length > 0)
            {
                _images.Add(new ImageInfo
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    ImageBytes = imageBytes,
                    Format = format
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting image: {ex.Message}");
        }
    }

    public List<ImageInfo> GetImages() => _images;
}
