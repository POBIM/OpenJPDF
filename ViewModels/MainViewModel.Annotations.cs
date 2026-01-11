// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using CommunityToolkit.Mvvm.Input;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenJPDF.Models;
using OpenJPDF.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;


namespace OpenJPDF.ViewModels;

/// <summary>
/// MainViewModel - Annotations (Text, Image, Shape, OCR)
/// </summary>
public partial class MainViewModel
{
    #region Add Annotation Commands

    [RelayCommand]
    private void AddText()
    {
        if (!IsFileLoaded) return;

        if (CurrentEditMode == EditMode.AddText)
        {
            CurrentEditMode = EditMode.None;
            StatusMessage = "Ready";
        }
        else
        {
            CurrentEditMode = EditMode.AddText;
            StatusMessage = "Click on the page to add text";
        }
    }

    [RelayCommand]
    private void AddImage()
    {
        if (!IsFileLoaded) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            Title = "Select Image"
        };

        if (dialog.ShowDialog() == true)
        {
            CurrentEditMode = EditMode.AddImage;
            // Fire event to show image preview that follows mouse
            ImageSelectedForPlacement?.Invoke(dialog.FileName);
        }
    }
    
    /// <summary>
    /// Place an image annotation at the specified position (called from MainWindow after preview placement)
    /// </summary>
    public void PlaceImageAnnotation(string imagePath, double pdfX, double pdfY, double pdfWidth, double pdfHeight)
    {
        var annotation = new ImageAnnotation
        {
            PageNumber = CurrentPageIndex,
            X = pdfX,
            Y = pdfY,
            Width = pdfWidth,
            Height = pdfHeight,
            ImagePath = imagePath
        };

        var annotationItem = ImageAnnotationItem.FromAnnotation(annotation);
        Annotations.Add(annotationItem);
        SelectedAnnotation = annotationItem;

        // Record undo action
        _undoRedoManager.RecordAction(new AddAnnotationAction(Annotations, annotationItem));

        RefreshAnnotationsRequested?.Invoke();
        CurrentEditMode = EditMode.None;
        StatusMessage = "Image added. Click 'Save' to apply changes.";
    }
    
    /// <summary>
    /// Place an image annotation from bytes (for clipboard/screen capture)
    /// </summary>
    public void PlaceImageAnnotationFromBytes(byte[] imageBytes, double pdfX, double pdfY, double pdfWidth, double pdfHeight)
    {
        // Save to temp file
        string tempPath = Path.Combine(Path.GetTempPath(), $"pasted_image_{Guid.NewGuid()}.png");
        File.WriteAllBytes(tempPath, imageBytes);
        
        PlaceImageAnnotation(tempPath, pdfX, pdfY, pdfWidth, pdfHeight);
    }

    [RelayCommand]
    private void AddRectangle()
    {
        if (!IsFileLoaded) return;

        if (CurrentEditMode == EditMode.AddRectangle)
        {
            CurrentEditMode = EditMode.None;
            StatusMessage = "Ready";
        }
        else
        {
            CurrentEditMode = EditMode.AddRectangle;
            StatusMessage = "Click on the page to add rectangle";
        }
    }

    [RelayCommand]
    private void AddEllipse()
    {
        if (!IsFileLoaded) return;

        if (CurrentEditMode == EditMode.AddEllipse)
        {
            CurrentEditMode = EditMode.None;
            StatusMessage = "Ready";
        }
        else
        {
            CurrentEditMode = EditMode.AddEllipse;
            StatusMessage = "Click on the page to add ellipse";
        }
    }

    [RelayCommand]
    private void AddLine()
    {
        if (!IsFileLoaded) return;

        if (CurrentEditMode == EditMode.AddLine)
        {
            CurrentEditMode = EditMode.None;
            StatusMessage = "Ready";
        }
        else
        {
            CurrentEditMode = EditMode.AddLine;
            StatusMessage = "Click on the page to add line";
        }
    }

    [RelayCommand]
    private void SelectText()
    {
        if (!IsFileLoaded) return;

        if (CurrentEditMode == EditMode.SelectText)
        {
            CurrentEditMode = EditMode.None;
            StatusMessage = "Ready";
        }
        else
        {
            CurrentEditMode = EditMode.SelectText;
            StatusMessage = "Drag to select text region for OCR";
        }
    }
    
    [RelayCommand]
    private void ScreenCapture()
    {
        if (!IsFileLoaded) return;

        if (CurrentEditMode == EditMode.ScreenCapture)
        {
            CurrentEditMode = EditMode.None;
            StatusMessage = "Ready";
        }
        else
        {
            CurrentEditMode = EditMode.ScreenCapture;
            StatusMessage = "Drag to select area to capture as image";
        }
    }

    #endregion

    #region Canvas Click Handler

    public void HandleCanvasClick(double x, double y, Action<object, AnnotationItem, double>? addPreviewCallback = null)
    {
        switch (CurrentEditMode)
        {
            case EditMode.AddText:
                HandleAddText(x, y, addPreviewCallback);
                break;
            case EditMode.AddImage:
                HandleAddImage(x, y, addPreviewCallback);
                break;
            case EditMode.AddRectangle:
                HandleAddShape(x, y, ShapeType.Rectangle, addPreviewCallback);
                break;
            case EditMode.AddEllipse:
                HandleAddShape(x, y, ShapeType.Ellipse, addPreviewCallback);
                break;
            case EditMode.AddLine:
                HandleAddShape(x, y, ShapeType.Line, addPreviewCallback);
                break;
        }
    }

    private void HandleAddText(double x, double y, Action<object, AnnotationItem, double>? addPreviewCallback)
    {
        var dialog = new Views.TextInputDialog();
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            double pdfX = x / ZoomScale;
            double pdfY = y / ZoomScale;

            var (textWidth, textHeight) = MeasureTextSize(
                dialog.InputText, 
                dialog.FontFamily, 
                dialog.FontSize, 
                dialog.IsBold, 
                dialog.IsItalic);

            var annotation = new TextAnnotation
            {
                PageNumber = CurrentPageIndex,
                X = pdfX,
                Y = pdfY,
                Text = dialog.InputText,
                FontFamily = dialog.FontFamily,
                FontSize = dialog.FontSize,
                Color = dialog.TextColor,
                BackgroundColor = dialog.BackgroundColor,
                BorderColor = dialog.BorderColor,
                BorderWidth = dialog.BorderWidth,
                IsBold = dialog.IsBold,
                IsItalic = dialog.IsItalic,
                IsUnderline = dialog.IsUnderline,
                TextAlignment = dialog.TextAlignment,
                Width = textWidth,
                Height = textHeight
            };

            var annotationItem = TextAnnotationItem.FromAnnotation(annotation);
            Annotations.Add(annotationItem);
            SelectedAnnotation = annotationItem;

            // Record undo action
            _undoRedoManager.RecordAction(new AddAnnotationAction(Annotations, annotationItem));

            addPreviewCallback?.Invoke(annotation, annotationItem, ZoomScale);

            StatusMessage = "Text added. Click 'Save' to apply changes.";
        }

        CurrentEditMode = EditMode.None;
    }

    private void HandleAddImage(double x, double y, Action<object, AnnotationItem, double>? addPreviewCallback)
    {
        if (string.IsNullOrEmpty(_pendingImagePath)) return;

        var sizeDialog = new Views.ImageSizeDialog();
        if (sizeDialog.ShowDialog() == true)
        {
            double pdfX = x / ZoomScale;
            double pdfY = y / ZoomScale;

            var annotation = new ImageAnnotation
            {
                PageNumber = CurrentPageIndex,
                X = pdfX,
                Y = pdfY,
                Width = sizeDialog.ImageWidth,
                Height = sizeDialog.ImageHeight,
                ImagePath = _pendingImagePath
            };

            var annotationItem = ImageAnnotationItem.FromAnnotation(annotation);
            Annotations.Add(annotationItem);
            SelectedAnnotation = annotationItem;

            // Record undo action
            _undoRedoManager.RecordAction(new AddAnnotationAction(Annotations, annotationItem));

            addPreviewCallback?.Invoke(annotation, annotationItem, ZoomScale);

            StatusMessage = "Image added. Click 'Save' to apply changes.";
        }

        _pendingImagePath = null;
        CurrentEditMode = EditMode.None;
    }

    private void HandleAddShape(double x, double y, ShapeType shapeType, Action<object, AnnotationItem, double>? addPreviewCallback)
    {
        double pdfX = x / ZoomScale;
        double pdfY = y / ZoomScale;

        var annotation = new ShapeAnnotation
        {
            PageNumber = CurrentPageIndex,
            X = pdfX,
            Y = pdfY,
            Width = shapeType == ShapeType.Line ? 0 : 100,
            Height = shapeType == ShapeType.Line ? 0 : 50,
            ShapeType = shapeType,
            X2 = shapeType == ShapeType.Line ? pdfX + 100 : 0,
            Y2 = shapeType == ShapeType.Line ? pdfY + 50 : 0
        };

        var annotationItem = ShapeAnnotationItem.FromAnnotation(annotation);
        Annotations.Add(annotationItem);
        SelectedAnnotation = annotationItem;

        // Record undo action
        _undoRedoManager.RecordAction(new AddAnnotationAction(Annotations, annotationItem));

        addPreviewCallback?.Invoke(annotation, annotationItem, ZoomScale);

        StatusMessage = $"{shapeType} added. Click 'Save' to apply changes.";
        CurrentEditMode = EditMode.None;
    }

    #endregion

    #region Text Measurement

    // Points to DIPs conversion factor (96 DPI / 72 points per inch)
    private const double POINTS_TO_DIPS = 96.0 / 72.0;

    /// <summary>
    /// Measure text size using WPF FormattedText
    /// Font size is expected in POINTS, will be converted to DIPs for WPF
    /// </summary>
    private static (double Width, double Height) MeasureTextSize(string text, string fontFamily, float fontSizePoints, bool isBold, bool isItalic)
    {
        // Convert font size from points to DIPs for WPF measurement
        double fontSizeDips = fontSizePoints * POINTS_TO_DIPS;
        
        var typeface = new System.Windows.Media.Typeface(
            new System.Windows.Media.FontFamily(fontFamily),
            isItalic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal,
            isBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
            System.Windows.FontStretches.Normal);

        var formattedText = new System.Windows.Media.FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSizeDips,
            System.Windows.Media.Brushes.Black,
            new System.Windows.Media.NumberSubstitution(),
            System.Windows.Media.TextFormattingMode.Display,
            96);

        double width = formattedText.Width + 4;
        double height = formattedText.Height + 4;

        return (width, height);
    }

    /// <summary>
    /// Calculate optimal font size to fit text within given box dimensions
    /// </summary>
    private static float CalculateOptimalFontSize(string text, double maxWidth, double maxHeight, string fontFamily, bool isBold, bool isItalic)
    {
        float minFont = 6f;
        float maxFont = 72f;
        float optimalSize = minFont;

        while (maxFont - minFont > 0.5f)
        {
            float midFont = (minFont + maxFont) / 2f;
            var (textWidth, textHeight) = MeasureTextSize(text, fontFamily, midFont, isBold, isItalic);

            if (textWidth <= maxWidth - 4 && textHeight <= maxHeight - 4)
            {
                optimalSize = midFont;
                minFont = midFont;
            }
            else
            {
                maxFont = midFont;
            }
        }

        return Math.Clamp(optimalSize, 8f, 72f);
    }

    #endregion

    #region OCR Text Creation

    /// <summary>
    /// Create text annotation from OCR result at specified position
    /// </summary>
    public void CreateTextFromOcr(string text, double x, double y, double width, double height, Action<object, AnnotationItem, double>? addPreviewCallback = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        double pdfX = x / ZoomScale;
        double pdfY = y / ZoomScale;
        double boxWidth = width / ZoomScale;
        double boxHeight = height / ZoomScale;

        float fontSize = CalculateOptimalFontSize(text, boxWidth, boxHeight, "Arial", false, false);

        var (textWidth, textHeight) = MeasureTextSize(text, "Arial", fontSize, false, false);

        var annotation = new TextAnnotation
        {
            PageNumber = CurrentPageIndex,
            X = pdfX,
            Y = pdfY,
            Text = text,
            FontSize = fontSize,
            Width = textWidth,
            Height = textHeight
        };

        var annotationItem = TextAnnotationItem.FromAnnotation(annotation);
        Annotations.Add(annotationItem);
        SelectedAnnotation = annotationItem;

        // Record undo action
        _undoRedoManager.RecordAction(new AddAnnotationAction(Annotations, annotationItem));

        addPreviewCallback?.Invoke(annotation, annotationItem, ZoomScale);

        CurrentEditMode = EditMode.None;
        StatusMessage = $"Text extracted (font: {fontSize:F1}pt): \"{(text.Length > 30 ? text[..30] + "..." : text)}\"";
    }

    #endregion

    #region Image Cropping

    public bool TryCropImageAnnotation(ImageAnnotationItem imageItem, double selectionX, double selectionY, double selectionWidth, double selectionHeight)
    {
        if (imageItem == null)
        {
            return false;
        }

        double displayWidth = imageItem.Width * ZoomScale;
        double displayHeight = imageItem.Height * ZoomScale;

        if (displayWidth <= 0 || displayHeight <= 0)
        {
            StatusMessage = "Invalid image size for cropping.";
            return false;
        }

        if (!TryNormalizeSelection(selectionX, selectionY, selectionWidth, selectionHeight, displayWidth, displayHeight, out var normalized))
        {
            StatusMessage = "Selection too small. Please select a larger area.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(imageItem.ImagePath) || !File.Exists(imageItem.ImagePath))
        {
            StatusMessage = "Image file not found for cropping.";
            return false;
        }

        try
        {
            using var bitmap = new Bitmap(imageItem.ImagePath);
            var cropRect = GetPixelCropRect(normalized, displayWidth, displayHeight, bitmap.Width, bitmap.Height);

            if (!TryCropBitmap(bitmap, cropRect, out var croppedBytes))
            {
                StatusMessage = "Failed to crop image.";
                return false;
            }

            string tempPath = SaveTempImageBytes(croppedBytes, "cropped_image", "png");

            var oldValues = new Dictionary<string, object?>
            {
                { nameof(ImageAnnotationItem.X), imageItem.X },
                { nameof(ImageAnnotationItem.Y), imageItem.Y },
                { nameof(ImageAnnotationItem.Width), imageItem.Width },
                { nameof(ImageAnnotationItem.Height), imageItem.Height },
                { nameof(ImageAnnotationItem.ImagePath), imageItem.ImagePath }
            };

            imageItem.X += normalized.X / ZoomScale;
            imageItem.Y += normalized.Y / ZoomScale;
            imageItem.Width = normalized.Width / ZoomScale;
            imageItem.Height = normalized.Height / ZoomScale;
            imageItem.ImagePath = tempPath;

            var newValues = new Dictionary<string, object?>
            {
                { nameof(ImageAnnotationItem.X), imageItem.X },
                { nameof(ImageAnnotationItem.Y), imageItem.Y },
                { nameof(ImageAnnotationItem.Width), imageItem.Width },
                { nameof(ImageAnnotationItem.Height), imageItem.Height },
                { nameof(ImageAnnotationItem.ImagePath), imageItem.ImagePath }
            };

            RecordUndoableAction(new ModifyAnnotationAction(imageItem, oldValues, newValues));
            RefreshAnnotationsRequested?.Invoke();
            StatusMessage = "Image cropped.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Image crop failed: {ex.Message}";
            return false;
        }
    }

    private static bool TryNormalizeSelection(double selectionX, double selectionY, double selectionWidth, double selectionHeight,
        double maxWidth, double maxHeight, out (double X, double Y, double Width, double Height) normalized)
    {
        double x = selectionX;
        double y = selectionY;
        double width = selectionWidth;
        double height = selectionHeight;

        if (width < 0)
        {
            x += width;
            width = Math.Abs(width);
        }

        if (height < 0)
        {
            y += height;
            height = Math.Abs(height);
        }

        x = Math.Clamp(x, 0, maxWidth);
        y = Math.Clamp(y, 0, maxHeight);
        width = Math.Min(width, maxWidth - x);
        height = Math.Min(height, maxHeight - y);

        normalized = (x, y, width, height);
        return width >= 5 && height >= 5;
    }

    private static Rectangle GetPixelCropRect((double X, double Y, double Width, double Height) selection,
        double displayWidth, double displayHeight, int pixelWidth, int pixelHeight)
    {
        double scaleX = pixelWidth / displayWidth;
        double scaleY = pixelHeight / displayHeight;

        int cropX = (int)Math.Round(selection.X * scaleX);
        int cropY = (int)Math.Round(selection.Y * scaleY);
        int cropWidth = (int)Math.Round(selection.Width * scaleX);
        int cropHeight = (int)Math.Round(selection.Height * scaleY);

        cropX = Math.Clamp(cropX, 0, Math.Max(0, pixelWidth - 1));
        cropY = Math.Clamp(cropY, 0, Math.Max(0, pixelHeight - 1));
        cropWidth = Math.Clamp(cropWidth, 1, Math.Max(1, pixelWidth - cropX));
        cropHeight = Math.Clamp(cropHeight, 1, Math.Max(1, pixelHeight - cropY));

        return new Rectangle(cropX, cropY, cropWidth, cropHeight);
    }

    private static bool TryCropBitmap(Bitmap source, Rectangle cropRect, out byte[] croppedBytes)
    {
        croppedBytes = Array.Empty<byte>();

        if (cropRect.Width <= 0 || cropRect.Height <= 0)
        {
            return false;
        }

        cropRect.X = Math.Clamp(cropRect.X, 0, Math.Max(0, source.Width - 1));
        cropRect.Y = Math.Clamp(cropRect.Y, 0, Math.Max(0, source.Height - 1));
        cropRect.Width = Math.Clamp(cropRect.Width, 1, source.Width - cropRect.X);
        cropRect.Height = Math.Clamp(cropRect.Height, 1, source.Height - cropRect.Y);

        // Clone and convert to 32bppArgb for compatibility with background removal
        using var cropped = source.Clone(cropRect, source.PixelFormat);
        using var argbBitmap = new Bitmap(cropped.Width, cropped.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(argbBitmap))
        {
            g.DrawImage(cropped, 0, 0, cropped.Width, cropped.Height);
        }
        
        using var stream = new MemoryStream();
        argbBitmap.Save(stream, ImageFormat.Png);
        croppedBytes = stream.ToArray();
        return true;
    }

    private static bool TryCropImageBytes(byte[] imageBytes, Rectangle cropRect, out byte[] croppedBytes)
    {
        croppedBytes = Array.Empty<byte>();
        if (imageBytes.Length == 0)
        {
            return false;
        }

        using var stream = new MemoryStream(imageBytes);
        using var bitmap = new Bitmap(stream);
        return TryCropBitmap(bitmap, cropRect, out croppedBytes);
    }

    private static string SaveTempImageBytes(byte[] bytes, string prefix, string extension)
    {
        string safeExtension = extension.TrimStart('.');
        string tempPath = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid()}.{safeExtension}");
        File.WriteAllBytes(tempPath, bytes);
        return tempPath;
    }

    #endregion

    #region Background Removal

    public async Task<bool> TryRemoveBackgroundAsync(ImageAnnotationItem imageItem)
    {
        if (imageItem == null)
        {
            StatusMessage = "No image selected for background removal.";
            System.Diagnostics.Debug.WriteLine("TryRemoveBackgroundAsync: imageItem is null");
            return false;
        }

        System.Diagnostics.Debug.WriteLine($"TryRemoveBackgroundAsync: ImagePath = '{imageItem.ImagePath}'");

        if (string.IsNullOrWhiteSpace(imageItem.ImagePath))
        {
            StatusMessage = "Image has no file path. Try converting to annotation first.";
            System.Diagnostics.Debug.WriteLine("TryRemoveBackgroundAsync: ImagePath is empty");
            return false;
        }

        if (!File.Exists(imageItem.ImagePath))
        {
            StatusMessage = $"Image file not found: {Path.GetFileName(imageItem.ImagePath)}";
            System.Diagnostics.Debug.WriteLine($"TryRemoveBackgroundAsync: File not found at '{imageItem.ImagePath}'");
            return false;
        }

        StatusMessage = "Removing background...";

        System.Diagnostics.Debug.WriteLine($"TryRemoveBackgroundAsync: Calling RemoveBackgroundAsync for '{imageItem.ImagePath}'");
        var result = await _backgroundRemovalService.RemoveBackgroundAsync(imageItem.ImagePath);
        
        if (!result.Success)
        {
            StatusMessage = result.ErrorMessage ?? "Background removal failed.";
            System.Diagnostics.Debug.WriteLine($"TryRemoveBackgroundAsync: Failed - {result.ErrorMessage}");
            return false;
        }
        
        if (result.ImageBytes == null || result.ImageBytes.Length == 0)
        {
            StatusMessage = "Background removal produced empty result.";
            System.Diagnostics.Debug.WriteLine("TryRemoveBackgroundAsync: ImageBytes is null or empty");
            return false;
        }
        
        System.Diagnostics.Debug.WriteLine($"TryRemoveBackgroundAsync: Success, got {result.ImageBytes.Length} bytes");

        string tempPath = SaveTempImageBytes(result.ImageBytes, "bg_removed", "png");

        var oldValues = new Dictionary<string, object?>
        {
            { nameof(ImageAnnotationItem.ImagePath), imageItem.ImagePath }
        };

        imageItem.ImagePath = tempPath;

        var newValues = new Dictionary<string, object?>
        {
            { nameof(ImageAnnotationItem.ImagePath), imageItem.ImagePath }
        };

        RecordUndoableAction(new ModifyAnnotationAction(imageItem, oldValues, newValues));
        RefreshAnnotationsRequested?.Invoke();
        StatusMessage = "Background removed.";
        return true;
    }

    #endregion

    #region Scanner Commands


    [RelayCommand]
    private async Task ScanDocument()
    {
        if (!IsFileLoaded)
        {
            StatusMessage = "Please open a PDF first before scanning.";
            return;
        }

        try
        {
            StatusMessage = "Starting scanner...";
            
            // Check if WIA is available
            var scanners = _scannerService.GetAvailableScanners();
            if (scanners.Count == 0)
            {
                StatusMessage = "No scanner found. Showing device help...";
                ShowPrinterScannerHelp();
                return;
            }

            // Scan with UI
            var settings = new ScannerService.ScanSettings { ShowUI = true };
            var scannedImage = await Task.Run(() => _scannerService.ScanDocument(settings: settings));
            
            if (scannedImage == null)
            {
                StatusMessage = "Scan cancelled.";
                return;
            }

            // Save to temp file
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), 
                $"scan_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scannedImage));
            using (var stream = new System.IO.FileStream(tempPath, System.IO.FileMode.Create))
            {
                encoder.Save(stream);
            }

            // Set as pending image and switch to AddImage mode
            _pendingImagePath = tempPath;
            CurrentEditMode = EditMode.AddImage;
            StatusMessage = "Scan complete. Click on the page to place the image.";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            ShowPrinterScannerHelp();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
            ShowPrinterScannerHelp();
        }
    }

    [RelayCommand]
    private async Task ScanToNewPdf()
    {
        try
        {
            StatusMessage = "Starting scanner...";
            
            // Check if WIA is available
            var scanners = _scannerService.GetAvailableScanners();
            if (scanners.Count == 0)
            {
                StatusMessage = "No scanner found. Showing device help...";
                ShowPrinterScannerHelp();
                return;
            }

            // Scan with UI
            var settings = new ScannerService.ScanSettings { ShowUI = true };
            var scannedImage = await Task.Run(() => _scannerService.ScanDocument(settings: settings));
            
            if (scannedImage == null)
            {
                StatusMessage = "Scan cancelled.";
                return;
            }

            // Ask where to save
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save Scanned PDF",
                FileName = $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (saveDialog.ShowDialog() != true)
            {
                StatusMessage = "Save cancelled.";
                return;
            }

            StatusMessage = "Creating PDF...";

            // Save scanned image to temp file
            string tempImagePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), 
                $"scan_temp_{Guid.NewGuid()}.png");
            
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scannedImage));
            using (var stream = new System.IO.FileStream(tempImagePath, System.IO.FileMode.Create))
            {
                encoder.Save(stream);
            }

            // Create PDF from image
            bool success = await _pdfService.CreatePdfFromImageAsync(
                tempImagePath, 
                saveDialog.FileName);

            // Clean up temp file
            try { System.IO.File.Delete(tempImagePath); } catch { }

            if (success)
            {
                StatusMessage = $"PDF created: {System.IO.Path.GetFileName(saveDialog.FileName)}";
                
                // Ask if user wants to open the new PDF
                var result = System.Windows.MessageBox.Show(
                    $"PDF created successfully!\n\n{saveDialog.FileName}\n\nOpen the new PDF?",
                    "Scan Complete",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await OpenFileFromPathAsync(saveDialog.FileName);
                }
            }
            else
            {
                StatusMessage = "Failed to create PDF.";
                System.Windows.MessageBox.Show("Failed to create PDF from scanned image.", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            ShowPrinterScannerHelp();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
            ShowPrinterScannerHelp();
        }
    }

    private void ShowPrinterScannerHelp()
    {
        var dialog = new Views.PrinterScannerHelpDialog();
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    #endregion

    #region Annotation Management Commands

    [RelayCommand]
    private void DeleteAnnotation(AnnotationItem? annotation)
    {
        if (annotation == null) return;

        // Record undo action before deleting
        var deleteAction = new DeleteAnnotationAction(Annotations, annotation);
        _undoRedoManager.RecordAction(deleteAction);

        Annotations.Remove(annotation);
        if (SelectedAnnotation == annotation)
        {
            SelectedAnnotation = null;
        }
        RefreshAnnotationsRequested?.Invoke();
        StatusMessage = "Element deleted.";
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedAnnotation != null)
        {
            DeleteAnnotation(SelectedAnnotation);
        }
    }

    [RelayCommand]
    private void ApplyChanges()
    {
        ClearAnnotationsRequested?.Invoke();
        RefreshAnnotationsRequested?.Invoke();
        StatusMessage = "Changes applied. Click 'Save' to save to file.";
    }

    /// <summary>
    /// Get all annotations for the current page
    /// </summary>
    public IEnumerable<AnnotationItem> GetCurrentPageAnnotations()
    {
        return Annotations.Where(a => a.PageNumber == CurrentPageIndex);
    }

    #endregion
}
