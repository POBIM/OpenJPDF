// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenJPDF.Services;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace OpenJPDF.Views;

public partial class AdvancedBackgroundRemovalDialog : Window
{
    private readonly string _imagePath;
    private readonly BackgroundRemovalService _backgroundRemovalService;
    
    private WriteableBitmap? _maskBitmap;
    private BitmapImage? _sourceImage;
    private bool _isPainting;
    private WpfPoint _lastPaintPoint;
    
    // Mask colors: Green = Keep (255), Red = Remove (0)
    private readonly WpfColor _keepColor = WpfColor.FromArgb(180, 0, 200, 0);
    private readonly WpfColor _removeColor = WpfColor.FromArgb(180, 200, 0, 0);
    
    public byte[]? ResultImageBytes { get; private set; }
    
    public AdvancedBackgroundRemovalDialog(string imagePath, BackgroundRemovalService backgroundRemovalService)
    {
        InitializeComponent();
        _imagePath = imagePath;
        _backgroundRemovalService = backgroundRemovalService;
        
        Loaded += OnLoaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadImage();
        UpdateBrushCursor();
        
        // Auto-fit if image is larger than available space
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_sourceImage != null)
            {
                double availableWidth = ImageScrollViewer.ActualWidth - 20;
                double availableHeight = ImageScrollViewer.ActualHeight - 20;
                
                if (_sourceImage.PixelWidth > availableWidth || _sourceImage.PixelHeight > availableHeight)
                {
                    ZoomFitButton_Click(null!, null!);
                }
                else
                {
                    UpdateZoom();
                }
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    
    private void LoadImage()
    {
        try
        {
            _sourceImage = new BitmapImage();
            _sourceImage.BeginInit();
            _sourceImage.CacheOption = BitmapCacheOption.OnLoad;
            _sourceImage.UriSource = new Uri(_imagePath);
            _sourceImage.EndInit();
            _sourceImage.Freeze();
            
            SourceImage.Source = _sourceImage;
            SourceImage.Width = _sourceImage.PixelWidth;
            SourceImage.Height = _sourceImage.PixelHeight;
            
            // Create mask bitmap (same size as image)
            _maskBitmap = new WriteableBitmap(
                _sourceImage.PixelWidth,
                _sourceImage.PixelHeight,
                96, 96,
                PixelFormats.Bgra32,
                null);
            
            // Initialize mask as fully transparent
            ClearMask();
            
            MaskOverlay.Source = _maskBitmap;
            MaskOverlay.Width = _sourceImage.PixelWidth;
            MaskOverlay.Height = _sourceImage.PixelHeight;
            
            // Set canvas size
            ImageCanvas.Width = _sourceImage.PixelWidth;
            ImageCanvas.Height = _sourceImage.PixelHeight;
            
            StatusText.Text = $"Image loaded: {_sourceImage.PixelWidth} x {_sourceImage.PixelHeight}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading image: {ex.Message}";
        }
    }
    
    private void ClearMask()
    {
        if (_maskBitmap == null) return;
        
        int width = _maskBitmap.PixelWidth;
        int height = _maskBitmap.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        
        // All transparent
        Array.Clear(pixels, 0, pixels.Length);
        
        _maskBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
    }
    
    private void UpdateBrushCursor()
    {
        double size = BrushSizeSlider.Value;
        BrushCursor.Width = size;
        BrushCursor.Height = size;
        
        // Update cursor color based on mode
        if (KeepModeRadio.IsChecked == true)
        {
            BrushCursor.Stroke = new SolidColorBrush(Colors.LimeGreen);
        }
        else
        {
            BrushCursor.Stroke = new SolidColorBrush(Colors.Red);
        }
    }
    
    private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateBrushCursor();
    }
    
    private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPainting = true;
        _lastPaintPoint = e.GetPosition(ImageCanvas);
        PaintAt(_lastPaintPoint);
        ImageCanvas.CaptureMouse();
    }
    
    private void ImageCanvas_MouseMove(object sender, WpfMouseEventArgs e)
    {
        var pos = e.GetPosition(ImageCanvas);
        
        // Update brush cursor position
        double size = BrushSizeSlider.Value;
        Canvas.SetLeft(BrushCursor, pos.X - size / 2);
        Canvas.SetTop(BrushCursor, pos.Y - size / 2);
        BrushCursor.Visibility = Visibility.Visible;
        
        if (_isPainting)
        {
            // Draw line from last point to current point
            DrawLine(_lastPaintPoint, pos);
            _lastPaintPoint = pos;
        }
    }
    
    private void ImageCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPainting = false;
        ImageCanvas.ReleaseMouseCapture();
        
        if (ShowPreviewCheckbox.IsChecked == true)
        {
            UpdatePreview();
        }
    }
    
    private void ImageCanvas_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        BrushCursor.Visibility = Visibility.Collapsed;
        if (_isPainting)
        {
            _isPainting = false;
            ImageCanvas.ReleaseMouseCapture();
        }
    }
    
    private void PaintAt(WpfPoint center)
    {
        if (_maskBitmap == null) return;
        
        double radius = BrushSizeSlider.Value / 2;
        bool isKeepMode = KeepModeRadio.IsChecked == true;
        WpfColor paintColor = isKeepMode ? _keepColor : _removeColor;
        
        int centerX = (int)center.X;
        int centerY = (int)center.Y;
        int r = (int)radius;
        
        // Calculate bounds
        int minX = Math.Max(0, centerX - r);
        int maxX = Math.Min(_maskBitmap.PixelWidth - 1, centerX + r);
        int minY = Math.Max(0, centerY - r);
        int maxY = Math.Min(_maskBitmap.PixelHeight - 1, centerY + r);
        
        if (minX > maxX || minY > maxY) return;
        
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        
        // Read existing pixels
        _maskBitmap.CopyPixels(new Int32Rect(minX, minY, width, height), pixels, stride, 0);
        
        // Paint circle
        double radiusSquared = radius * radius;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int globalX = minX + x;
                int globalY = minY + y;
                
                double dx = globalX - center.X;
                double dy = globalY - center.Y;
                double distSquared = dx * dx + dy * dy;
                
                if (distSquared <= radiusSquared)
                {
                    int idx = y * stride + x * 4;
                    pixels[idx + 0] = paintColor.B;
                    pixels[idx + 1] = paintColor.G;
                    pixels[idx + 2] = paintColor.R;
                    pixels[idx + 3] = paintColor.A;
                }
            }
        }
        
        // Write back
        _maskBitmap.WritePixels(new Int32Rect(minX, minY, width, height), pixels, stride, 0);
    }
    
    private void DrawLine(WpfPoint from, WpfPoint to)
    {
        double distance = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
        double step = Math.Max(1, BrushSizeSlider.Value / 4);
        int steps = (int)(distance / step);
        
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0 : (double)i / steps;
            double x = from.X + (to.X - from.X) * t;
            double y = from.Y + (to.Y - from.Y) * t;
            PaintAt(new WpfPoint(x, y));
        }
    }
    
    private async void AutoDetectButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Running AI detection...";
        AutoDetectButton.IsEnabled = false;
        
        try
        {
            var result = await _backgroundRemovalService.RemoveBackgroundAsync(_imagePath);
            
            if (result.Success && result.ImageBytes != null)
            {
                // Load the AI result and extract mask
                using var ms = new MemoryStream(result.ImageBytes);
                var aiResult = new BitmapImage();
                aiResult.BeginInit();
                aiResult.CacheOption = BitmapCacheOption.OnLoad;
                aiResult.StreamSource = ms;
                aiResult.EndInit();
                
                // Convert AI result alpha to our mask
                ApplyAiMaskFromResult(aiResult);
                
                StatusText.Text = "AI detection complete. Refine with brush if needed.";
            }
            else
            {
                StatusText.Text = result.ErrorMessage ?? "AI detection failed.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            AutoDetectButton.IsEnabled = true;
        }
    }
    
    private void ApplyAiMaskFromResult(BitmapImage aiResult)
    {
        if (_maskBitmap == null || _sourceImage == null) return;
        
        // Create a FormatConvertedBitmap to get BGRA32 format
        var converted = new FormatConvertedBitmap(aiResult, PixelFormats.Bgra32, null, 0);
        
        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        byte[] aiPixels = new byte[height * stride];
        converted.CopyPixels(aiPixels, stride, 0);
        
        // Scale if needed
        int maskWidth = _maskBitmap.PixelWidth;
        int maskHeight = _maskBitmap.PixelHeight;
        int maskStride = maskWidth * 4;
        byte[] maskPixels = new byte[maskHeight * maskStride];
        
        double scaleX = (double)width / maskWidth;
        double scaleY = (double)height / maskHeight;
        
        for (int y = 0; y < maskHeight; y++)
        {
            for (int x = 0; x < maskWidth; x++)
            {
                int srcX = Math.Min((int)(x * scaleX), width - 1);
                int srcY = Math.Min((int)(y * scaleY), height - 1);
                
                int srcIdx = srcY * stride + srcX * 4;
                int dstIdx = y * maskStride + x * 4;
                
                byte alpha = aiPixels[srcIdx + 3];
                
                if (alpha > 127)
                {
                    // Foreground - mark as keep (green)
                    maskPixels[dstIdx + 0] = _keepColor.B;
                    maskPixels[dstIdx + 1] = _keepColor.G;
                    maskPixels[dstIdx + 2] = _keepColor.R;
                    maskPixels[dstIdx + 3] = _keepColor.A;
                }
                else
                {
                    // Background - mark as remove (red)
                    maskPixels[dstIdx + 0] = _removeColor.B;
                    maskPixels[dstIdx + 1] = _removeColor.G;
                    maskPixels[dstIdx + 2] = _removeColor.R;
                    maskPixels[dstIdx + 3] = _removeColor.A;
                }
            }
        }
        
        _maskBitmap.WritePixels(new Int32Rect(0, 0, maskWidth, maskHeight), maskPixels, maskStride, 0);
    }
    
    private void InvertMaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (_maskBitmap == null) return;
        
        int width = _maskBitmap.PixelWidth;
        int height = _maskBitmap.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        
        _maskBitmap.CopyPixels(pixels, stride, 0);
        
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 0) // Has paint
            {
                // Check if it's keep (green) or remove (red)
                bool isKeep = pixels[i + 1] > pixels[i + 2]; // G > R
                
                if (isKeep)
                {
                    // Change to remove
                    pixels[i + 0] = _removeColor.B;
                    pixels[i + 1] = _removeColor.G;
                    pixels[i + 2] = _removeColor.R;
                    pixels[i + 3] = _removeColor.A;
                }
                else
                {
                    // Change to keep
                    pixels[i + 0] = _keepColor.B;
                    pixels[i + 1] = _keepColor.G;
                    pixels[i + 2] = _keepColor.R;
                    pixels[i + 3] = _keepColor.A;
                }
            }
        }
        
        _maskBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        StatusText.Text = "Mask inverted.";
    }
    
    private void ClearMaskButton_Click(object sender, RoutedEventArgs e)
    {
        ClearMask();
        StatusText.Text = "Mask cleared.";
    }
    
    private void ShowPreviewCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (ShowPreviewCheckbox.IsChecked == true)
        {
            UpdatePreview();
        }
        else
        {
            // Show original image
            if (_sourceImage != null)
            {
                SourceImage.Source = _sourceImage;
            }
            MaskOverlay.Opacity = 0.5;
        }
    }
    
    private void UpdatePreview()
    {
        if (_maskBitmap == null || _sourceImage == null) return;
        
        try
        {
            var resultBytes = ApplyMaskToImage();
            if (resultBytes != null)
            {
                using var ms = new MemoryStream(resultBytes);
                var preview = new BitmapImage();
                preview.BeginInit();
                preview.CacheOption = BitmapCacheOption.OnLoad;
                preview.StreamSource = ms;
                preview.EndInit();
                preview.Freeze();
                
                SourceImage.Source = preview;
                MaskOverlay.Opacity = 0; // Hide mask in preview mode
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Preview error: {ex.Message}";
        }
    }
    
    private byte[]? ApplyMaskToImage()
    {
        if (_maskBitmap == null || _sourceImage == null) return null;
        
        // Convert source to BGRA32
        var convertedSource = new FormatConvertedBitmap(_sourceImage, PixelFormats.Bgra32, null, 0);
        
        int width = convertedSource.PixelWidth;
        int height = convertedSource.PixelHeight;
        int stride = width * 4;
        byte[] sourcePixels = new byte[height * stride];
        convertedSource.CopyPixels(sourcePixels, stride, 0);
        
        // Get mask pixels
        int maskWidth = _maskBitmap.PixelWidth;
        int maskHeight = _maskBitmap.PixelHeight;
        int maskStride = maskWidth * 4;
        byte[] maskPixels = new byte[maskHeight * maskStride];
        _maskBitmap.CopyPixels(maskPixels, maskStride, 0);
        
        // Apply mask
        double scaleX = (double)maskWidth / width;
        double scaleY = (double)maskHeight / height;
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int maskX = Math.Min((int)(x * scaleX), maskWidth - 1);
                int maskY = Math.Min((int)(y * scaleY), maskHeight - 1);
                
                int maskIdx = maskY * maskStride + maskX * 4;
                int srcIdx = y * stride + x * 4;
                
                byte maskAlpha = maskPixels[maskIdx + 3];
                
                if (maskAlpha > 0)
                {
                    // Check if keep (green) or remove (red)
                    bool isKeep = maskPixels[maskIdx + 1] > maskPixels[maskIdx + 2];
                    
                    if (!isKeep)
                    {
                        // Remove - set alpha to 0
                        sourcePixels[srcIdx + 3] = 0;
                    }
                    // Keep - leave as is
                }
                // Unpainted areas - leave as is (keep original)
            }
        }
        
        // Create result bitmap
        var resultBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, sourcePixels, stride);
        
        // Encode to PNG
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(resultBitmap));
        
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
    
    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ResultImageBytes = ApplyMaskToImage();
        
        if (ResultImageBytes != null)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.Text = "Failed to apply mask.";
        }
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    #region Zoom Controls
    
    private double _zoomLevel = 1.0;
    private const double ZoomStep = 0.25;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    
    private void UpdateZoom()
    {
        CanvasScale.ScaleX = _zoomLevel;
        CanvasScale.ScaleY = _zoomLevel;
        ZoomLevelText.Text = $"{_zoomLevel * 100:0}%";
    }
    
    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        _zoomLevel = Math.Min(MaxZoom, _zoomLevel + ZoomStep);
        UpdateZoom();
    }
    
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        _zoomLevel = Math.Max(MinZoom, _zoomLevel - ZoomStep);
        UpdateZoom();
    }
    
    private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceImage == null) return;
        
        double availableWidth = ImageScrollViewer.ActualWidth - 20;
        double availableHeight = ImageScrollViewer.ActualHeight - 20;
        
        if (availableWidth <= 0 || availableHeight <= 0) return;
        
        double scaleX = availableWidth / _sourceImage.PixelWidth;
        double scaleY = availableHeight / _sourceImage.PixelHeight;
        
        _zoomLevel = Math.Min(scaleX, scaleY);
        _zoomLevel = Math.Clamp(_zoomLevel, MinZoom, MaxZoom);
        UpdateZoom();
    }
    
    private void ImageCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Delta > 0)
            {
                _zoomLevel = Math.Min(MaxZoom, _zoomLevel + ZoomStep);
            }
            else
            {
                _zoomLevel = Math.Max(MinZoom, _zoomLevel - ZoomStep);
            }
            UpdateZoom();
            e.Handled = true;
        }
    }
    
    #endregion
}
