// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OpenJPDF.Models;
using OpenJPDF.Services;
using OpenJPDF.ViewModels;
using WpfImage = System.Windows.Controls.Image;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfLine = System.Windows.Shapes.Line;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPanel = System.Windows.Controls.Panel;
using WpfCursors = System.Windows.Input.Cursors;
using WpfTextBox = System.Windows.Controls.TextBox;
using DoubleCollection = System.Windows.Media.DoubleCollection;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfDataObject = System.Windows.DataObject;
using WpfDragDropEffects = System.Windows.DragDropEffects;

namespace OpenJPDF.Views;

public partial class MainWindow : Window
{
    // Drag state
    private bool _isDragging;
    private WpfPoint _dragStartPoint;
    private UIElement? _draggedElement;
    private AnnotationItem? _draggedAnnotation;
    private double _dragOriginalX;
    private double _dragOriginalY;

    // Presentation mode state
    private bool _isPresentationMode;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private bool _previousTopmost;

    // ===== Page Navigation State =====
    private DateTime _lastPageChangeTime = DateTime.MinValue;
    private bool _isChangingPage = false;
    private const int PAGE_CHANGE_DEBOUNCE_MS = 200;

    // OCR Selection state
    private bool _isSelecting;
    private WpfPoint _selectionStart;
    private WpfRectangle? _selectionRect;
    private readonly Services.OcrService _ocrService = new();

    // Thumbnail drag-drop state
    private bool _isThumbnailDragging;
    private WpfPoint _thumbnailDragStartPoint;
    private PageThumbnail? _draggedThumbnail;

    // Resize handles manager
    private ResizeHandlesManager? _resizeHandlesManager;

    // Image crop selection state
    private bool _isImageCropSelecting;
    private WpfPoint _imageCropStart;
    private WpfRectangle? _imageCropRect;
    private FrameworkElement? _imageCropTargetElement;
    private ImageAnnotationItem? _imageCropAnnotation;
    private ExtractedImageItem? _imageCropExtractedImage;
    
    // Image preview state (for placing new images)
    private WpfImage? _imagePreview;
    private Border? _imagePreviewBorder;
    private string? _pendingImagePath;
    private double _pendingImageWidth;
    private double _pendingImageHeight;
    
    // Screen capture selection state
    private bool _isScreenCapturing;
    private WpfPoint _screenCaptureStart;
    private WpfRectangle? _screenCaptureRect;

    public MainWindow()
    {
        InitializeComponent();
        
        // Add keyboard shortcut for fullscreen (F11)
        KeyDown += MainWindow_KeyDown;
        
        // Subscribe to clear annotations event
        Loaded += MainWindow_Loaded;
        Unloaded += MainWindow_Unloaded;

        // Initialize OCR engine
        InitializeOcr();
    }

    private async void InitializeOcr()
    {
        await Task.Run(() =>
        {
            if (!_ocrService.Initialize("eng+tha"))
            {
                Dispatcher.Invoke(() =>
                {
                    if (DataContext is MainViewModel vm)
                    {
                        vm.StatusMessage = "OCR: Please download tessdata files to: " + 
                            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                    }
                });
            }
        });
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ClearAnnotationsRequested += ClearAnnotationPreviews;
            vm.RefreshAnnotationsRequested += RefreshAnnotationPreviews;
            vm.PageRotationChanged += OnPageRotationChanged;
            vm.RefreshHeaderFooterPreview += RefreshHeaderFooterPreviewOnCanvas;
            vm.RefreshExtractedContentRequested += RefreshExtractedContentOnCanvas;
            vm.PropertyChanged += ViewModel_PropertyChanged;

            // Initialize resize handles manager
            _resizeHandlesManager = new ResizeHandlesManager(AnnotationCanvas, vm, RefreshAnnotationPreviews);
            
            // Subscribe to image selection event
            vm.ImageSelectedForPlacement += OnImageSelectedForPlacement;
        }

        // Show about dialog after window is loaded (skip if opened with file)
        if (!App.OpenedWithFile)
        {
            ShowAboutDialog();
        }
    }
    
    private void OnImageSelectedForPlacement(string imagePath)
    {
        try
        {
            // Load image to get dimensions and create preview
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath);
            bitmap.EndInit();
            bitmap.Freeze();
            
            _pendingImagePath = imagePath;
            
            // Calculate default size (max 300px, maintain aspect ratio)
            double maxSize = 300;
            double scale = Math.Min(maxSize / bitmap.PixelWidth, maxSize / bitmap.PixelHeight);
            scale = Math.Min(scale, 1.0); // Don't upscale
            
            _pendingImageWidth = bitmap.PixelWidth * scale;
            _pendingImageHeight = bitmap.PixelHeight * scale;
            
            // Create preview image with border
            _imagePreview = new WpfImage
            {
                Source = bitmap,
                Width = _pendingImageWidth,
                Height = _pendingImageHeight,
                Opacity = 0.7,
                IsHitTestVisible = false
            };
            
            _imagePreviewBorder = new Border
            {
                Child = _imagePreview,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(33, 150, 243)),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(WpfColor.FromArgb(30, 255, 255, 255)),
                IsHitTestVisible = false
            };
            
            WpfPanel.SetZIndex(_imagePreviewBorder, 999);
            AnnotationCanvas.Children.Add(_imagePreviewBorder);
            
            // Subscribe to mouse events
            PdfContainer.MouseMove += ImagePreview_MouseMove;
            PdfContainer.MouseLeftButtonDown += ImagePreview_MouseLeftButtonDown;
            PdfContainer.MouseRightButtonDown += ImagePreview_Cancel;
            
            if (DataContext is MainViewModel vm)
            {
                vm.StatusMessage = "Click to place image. Right-click to cancel. Scroll wheel to resize.";
            }
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.StatusMessage = $"Failed to load image: {ex.Message}";
            }
            CancelImagePreview();
        }
    }
    
    private void ImagePreview_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_imagePreviewBorder == null) return;
        
        var pos = e.GetPosition(AnnotationCanvas);
        Canvas.SetLeft(_imagePreviewBorder, pos.X - _pendingImageWidth / 2);
        Canvas.SetTop(_imagePreviewBorder, pos.Y - _pendingImageHeight / 2);
    }
    
    private void ImagePreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_imagePreviewBorder == null || string.IsNullOrEmpty(_pendingImagePath)) return;
        if (DataContext is not MainViewModel vm) return;
        
        var pos = e.GetPosition(PdfImage);
        
        // Calculate position (center the image on click point)
        double x = pos.X - _pendingImageWidth / 2;
        double y = pos.Y - _pendingImageHeight / 2;
        
        // Clamp to canvas bounds
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        
        // Create annotation
        double pdfX = x / vm.ZoomScale;
        double pdfY = y / vm.ZoomScale;
        double pdfWidth = _pendingImageWidth / vm.ZoomScale;
        double pdfHeight = _pendingImageHeight / vm.ZoomScale;
        
        vm.PlaceImageAnnotation(_pendingImagePath, pdfX, pdfY, pdfWidth, pdfHeight);
        
        // Clean up preview
        CancelImagePreview();
        e.Handled = true;
    }
    
    private void ImagePreview_Cancel(object sender, MouseButtonEventArgs e)
    {
        CancelImagePreview();
        if (DataContext is MainViewModel vm)
        {
            vm.CurrentEditMode = EditMode.None;
            vm.StatusMessage = "Image placement cancelled.";
        }
        e.Handled = true;
    }
    
    private void CancelImagePreview()
    {
        if (_imagePreviewBorder != null)
        {
            AnnotationCanvas.Children.Remove(_imagePreviewBorder);
            _imagePreviewBorder = null;
            _imagePreview = null;
        }
        
        _pendingImagePath = null;
        
        PdfContainer.MouseMove -= ImagePreview_MouseMove;
        PdfContainer.MouseLeftButtonDown -= ImagePreview_MouseLeftButtonDown;
        PdfContainer.MouseRightButtonDown -= ImagePreview_Cancel;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedAnnotation))
        {
            // Hide resize handles when selection changes - they will be shown when element is clicked
            _resizeHandlesManager?.HideHandles();
        }
    }

    private void ShowAboutDialog()
    {
        var aboutDialog = new AboutDialog
        {
            Owner = this
        };
        aboutDialog.ShowDialog();
    }

    private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ClearAnnotationsRequested -= ClearAnnotationPreviews;
            vm.RefreshAnnotationsRequested -= RefreshAnnotationPreviews;
            vm.PageRotationChanged -= OnPageRotationChanged;
            vm.RefreshHeaderFooterPreview -= RefreshHeaderFooterPreviewOnCanvas;
            vm.RefreshExtractedContentRequested -= RefreshExtractedContentOnCanvas;
            vm.PropertyChanged -= ViewModel_PropertyChanged;
            vm.ImageSelectedForPlacement -= OnImageSelectedForPlacement;
        }
        _resizeHandlesManager?.HideHandles();
        CancelImagePreview();
    }

    /// <summary>
    /// Handle page rotation change - no longer needed since rotation is applied during render
    /// </summary>
    private void OnPageRotationChanged(int degrees)
    {
        // Rotation is now applied during PDF rendering, no visual transform needed
        // This method kept for compatibility but does nothing
    }

    /// <summary>
    /// Handle mouse wheel for page navigation and zoom.
    /// - Ctrl+Scroll = Zoom
    /// - No scrollbar = scroll ลง/ขึ้น เปลี่ยนหน้าทันที
    /// - Has scrollbar = scroll จนสุดก่อน แล้วค่อยเปลี่ยนหน้า
    /// </summary>
    private void PdfScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsFileLoaded) return;
        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer == null) return;

        // Ctrl+Scroll = Zoom
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (e.Delta > 0)
                vm.ZoomInCommand.Execute(null);
            else
                vm.ZoomOutCommand.Execute(null);
            return;
        }

        // Check debounce
        if (_isChangingPage || (DateTime.Now - _lastPageChangeTime).TotalMilliseconds < PAGE_CHANGE_DEBOUNCE_MS)
            return;

        bool scrollingDown = e.Delta < 0;  // Delta negative = scroll down
        bool scrollingUp = e.Delta > 0;    // Delta positive = scroll up
        
        double scrollableHeight = scrollViewer.ScrollableHeight;
        double currentOffset = scrollViewer.VerticalOffset;

        // CASE 1: ไม่มี scrollbar (content พอดีหน้าจอ หรือเล็กกว่า)
        if (scrollableHeight <= 0)
        {
            e.Handled = true;
            if (scrollingDown && vm.CurrentPageIndex < vm.TotalPages - 1)
            {
                ChangePage(vm, next: true);
            }
            else if (scrollingUp && vm.CurrentPageIndex > 0)
            {
                ChangePage(vm, next: false);
            }
            return;
        }

        // CASE 2: มี scrollbar - ต้อง scroll จนสุดก่อน
        bool atBottom = currentOffset >= scrollableHeight - 1;  // tolerance 1px
        bool atTop = currentOffset <= 1;  // tolerance 1px

        if (scrollingDown && atBottom && vm.CurrentPageIndex < vm.TotalPages - 1)
        {
            e.Handled = true;
            ChangePage(vm, next: true);
        }
        else if (scrollingUp && atTop && vm.CurrentPageIndex > 0)
        {
            e.Handled = true;
            ChangePage(vm, next: false);
        }
        // Otherwise let ScrollViewer handle normal scrolling
    }

    /// <summary>
    /// Change to next/previous page
    /// </summary>
    private void ChangePage(MainViewModel vm, bool next)
    {
        _isChangingPage = true;
        _lastPageChangeTime = DateTime.Now;

        if (next)
            vm.NextPageCommand.Execute(null);
        else
            vm.PreviousPageCommand.Execute(null);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            PdfScrollViewer?.ScrollToVerticalOffset(0);
            _isChangingPage = false;
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// ScrollChanged - now only used for UI updates, not page navigation
    /// </summary>
    private void PdfScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Page navigation is handled in PreviewMouseWheel
        // This can be used for other scroll-related UI updates if needed
    }

    /// <summary>
    /// Handle click on a page in continuous scroll view (not used in current hybrid approach)
    /// </summary>
    private void PageContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Currently using hybrid approach - continuous scroll for viewing
        // Annotations are handled by PdfContainer_MouseLeftButtonDown
    }

    private void PdfContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var position = e.GetPosition(PdfImage);
            
            if (PdfImage.Source != null)
            {
                var imageWidth = PdfImage.Source.Width;
                var imageHeight = PdfImage.Source.Height;
                
                if (position.X >= 0 && position.X <= imageWidth &&
                    position.Y >= 0 && position.Y <= imageHeight)
                {
                    // Handle OCR selection mode
                    if (vm.CurrentEditMode == EditMode.SelectText)
                    {
                        StartOcrSelection(position);
                        e.Handled = true;
                        return;
                    }
                    
                    // Handle screen capture mode
                    if (vm.CurrentEditMode == EditMode.ScreenCapture)
                    {
                        StartScreenCapture(position);
                        e.Handled = true;
                        return;
                    }

                    if (vm.IsEditMode)
                    {
                        vm.HandleCanvasClick(position.X, position.Y, AddAnnotationPreview);
                    }
                }
            }
        }
    }

    #region OCR Selection

    private void StartOcrSelection(WpfPoint startPoint)
    {
        _isSelecting = true;
        _selectionStart = startPoint;

        // Create selection rectangle
        _selectionRect = new WpfRectangle
        {
            Stroke = new SolidColorBrush(WpfColor.FromRgb(33, 150, 243)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(WpfColor.FromArgb(50, 33, 150, 243))
        };

        Canvas.SetLeft(_selectionRect, startPoint.X);
        Canvas.SetTop(_selectionRect, startPoint.Y);
        _selectionRect.Width = 0;
        _selectionRect.Height = 0;

        AnnotationCanvas.Children.Add(_selectionRect);
        AnnotationCanvas.CaptureMouse();

        // Subscribe to mouse events on canvas
        AnnotationCanvas.MouseMove += SelectionCanvas_MouseMove;
        AnnotationCanvas.MouseLeftButtonUp += SelectionCanvas_MouseLeftButtonUp;
    }

    private void SelectionCanvas_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isSelecting || _selectionRect == null) return;

        var currentPoint = e.GetPosition(PdfImage);

        // Calculate rectangle bounds
        double x = Math.Min(_selectionStart.X, currentPoint.X);
        double y = Math.Min(_selectionStart.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - _selectionStart.X);
        double height = Math.Abs(currentPoint.Y - _selectionStart.Y);

        Canvas.SetLeft(_selectionRect, x);
        Canvas.SetTop(_selectionRect, y);
        _selectionRect.Width = width;
        _selectionRect.Height = height;
    }

    private async void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting || _selectionRect == null) return;

        _isSelecting = false;
        AnnotationCanvas.ReleaseMouseCapture();
        AnnotationCanvas.MouseMove -= SelectionCanvas_MouseMove;
        AnnotationCanvas.MouseLeftButtonUp -= SelectionCanvas_MouseLeftButtonUp;

        // Get selection bounds
        double x = Canvas.GetLeft(_selectionRect);
        double y = Canvas.GetTop(_selectionRect);
        double width = _selectionRect.Width;
        double height = _selectionRect.Height;

        // Remove selection rectangle
        AnnotationCanvas.Children.Remove(_selectionRect);
        _selectionRect = null;

        // Minimum size check
        if (width < 10 || height < 10)
        {
            if (DataContext is MainViewModel vm1)
            {
                vm1.StatusMessage = "Selection too small. Please drag a larger area.";
            }
            return;
        }

        // Perform OCR
        await PerformOcrOnRegion(x, y, width, height);
    }

    private async Task PerformOcrOnRegion(double x, double y, double width, double height)
    {
        if (DataContext is not MainViewModel vm) return;

        if (!_ocrService.IsInitialized)
        {
            vm.StatusMessage = "OCR not initialized. Download tessdata files first.";
            System.Windows.MessageBox.Show(
                "OCR engine not initialized.\n\n" +
                "Please download Tesseract trained data files:\n" +
                "1. Download eng.traineddata and tha.traineddata from:\n" +
                "   https://github.com/tesseract-ocr/tessdata\n\n" +
                "2. Place them in:\n" +
                $"   {System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata")}",
                "OCR Setup Required",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        vm.StatusMessage = "Performing OCR...";

        try
        {
            // Get the current page image as bitmap
            var bitmapSource = vm.CurrentPageImage;
            if (bitmapSource == null) return;

            // Convert BitmapSource to System.Drawing.Bitmap
            using var stream = new System.IO.MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            encoder.Save(stream);
            stream.Position = 0;

            using var bitmap = new System.Drawing.Bitmap(stream);

            // Define region (convert from screen coordinates)
            var region = new System.Drawing.Rectangle(
                (int)x, (int)y, (int)width, (int)height);

            // Perform OCR
            string text = await Task.Run(() => _ocrService.RecognizeTextInRegion(bitmap, region));

            if (string.IsNullOrWhiteSpace(text))
            {
                vm.StatusMessage = "No text detected in selected region.";
                return;
            }

            // Clean up text (remove extra whitespace)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            // Create editable text annotation
            vm.CreateTextFromOcr(text, x, y, width, height, AddAnnotationPreview);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"OCR failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OCR Error: {ex}");
        }
    }

    #endregion
    
    #region Clipboard Paste
    
    private void PasteImageFromClipboard()
    {
        if (DataContext is not MainViewModel vm) return;
        
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var bitmapSource = System.Windows.Clipboard.GetImage();
                if (bitmapSource == null)
                {
                    vm.StatusMessage = "Failed to get image from clipboard.";
                    return;
                }
                
                // Convert BitmapSource to PNG bytes
                using var stream = new System.IO.MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(stream);
                
                // Save to temp file
                string tempPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"clipboard_image_{Guid.NewGuid()}.png");
                System.IO.File.WriteAllBytes(tempPath, stream.ToArray());
                
                // Show preview that follows mouse
                vm.CurrentEditMode = EditMode.AddImage;
                OnImageSelectedForPlacement(tempPath);
                
                vm.StatusMessage = "Image pasted. Click to place. Right-click to cancel.";
            }
            else if (System.Windows.Clipboard.ContainsFileDropList())
            {
                var files = System.Windows.Clipboard.GetFileDropList();
                if (files.Count > 0)
                {
                    string? imagePath = null;
                    foreach (var file in files)
                    {
                        if (file != null)
                        {
                            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
                            {
                                imagePath = file;
                                break;
                            }
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
                    {
                        vm.CurrentEditMode = EditMode.AddImage;
                        OnImageSelectedForPlacement(imagePath);
                        vm.StatusMessage = "Image pasted. Click to place. Right-click to cancel.";
                    }
                    else
                    {
                        vm.StatusMessage = "No supported image file in clipboard.";
                    }
                }
            }
            else
            {
                vm.StatusMessage = "No image in clipboard. Copy an image first.";
            }
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Paste failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Paste Error: {ex}");
        }
    }
    
    #endregion
    
    #region Screen Capture Selection
    
    private void StartScreenCapture(WpfPoint startPoint)
    {
        _isScreenCapturing = true;
        _screenCaptureStart = startPoint;

        // Create selection rectangle (green for screen capture)
        _screenCaptureRect = new WpfRectangle
        {
            Stroke = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80)), // Green
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(WpfColor.FromArgb(50, 76, 175, 80))
        };

        Canvas.SetLeft(_screenCaptureRect, startPoint.X);
        Canvas.SetTop(_screenCaptureRect, startPoint.Y);
        _screenCaptureRect.Width = 0;
        _screenCaptureRect.Height = 0;

        AnnotationCanvas.Children.Add(_screenCaptureRect);
        AnnotationCanvas.CaptureMouse();

        // Subscribe to mouse events on canvas
        AnnotationCanvas.MouseMove += ScreenCapture_MouseMove;
        AnnotationCanvas.MouseLeftButtonUp += ScreenCapture_MouseLeftButtonUp;
    }

    private void ScreenCapture_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isScreenCapturing || _screenCaptureRect == null) return;

        var currentPoint = e.GetPosition(PdfImage);

        // Calculate rectangle bounds
        double x = Math.Min(_screenCaptureStart.X, currentPoint.X);
        double y = Math.Min(_screenCaptureStart.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - _screenCaptureStart.X);
        double height = Math.Abs(currentPoint.Y - _screenCaptureStart.Y);

        Canvas.SetLeft(_screenCaptureRect, x);
        Canvas.SetTop(_screenCaptureRect, y);
        _screenCaptureRect.Width = width;
        _screenCaptureRect.Height = height;
    }

    private void ScreenCapture_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isScreenCapturing || _screenCaptureRect == null) return;

        _isScreenCapturing = false;
        AnnotationCanvas.ReleaseMouseCapture();
        AnnotationCanvas.MouseMove -= ScreenCapture_MouseMove;
        AnnotationCanvas.MouseLeftButtonUp -= ScreenCapture_MouseLeftButtonUp;

        // Get selection bounds
        double x = Canvas.GetLeft(_screenCaptureRect);
        double y = Canvas.GetTop(_screenCaptureRect);
        double width = _screenCaptureRect.Width;
        double height = _screenCaptureRect.Height;

        // Remove selection rectangle
        AnnotationCanvas.Children.Remove(_screenCaptureRect);
        _screenCaptureRect = null;

        // Minimum size check
        if (width < 10 || height < 10)
        {
            if (DataContext is MainViewModel vm1)
            {
                vm1.StatusMessage = "Selection too small. Please drag a larger area.";
            }
            return;
        }

        // Capture the region as image and show preview
        CaptureRegionAsImage(x, y, width, height);
    }

    private void CaptureRegionAsImage(double x, double y, double width, double height)
    {
        if (DataContext is not MainViewModel vm) return;

        try
        {
            // Get the current page image as bitmap
            var bitmapSource = vm.CurrentPageImage;
            if (bitmapSource == null) return;

            // Convert BitmapSource to System.Drawing.Bitmap
            using var stream = new System.IO.MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            encoder.Save(stream);
            stream.Position = 0;

            using var fullBitmap = new System.Drawing.Bitmap(stream);

            // Clamp region to image bounds
            int cropX = Math.Max(0, (int)x);
            int cropY = Math.Max(0, (int)y);
            int cropWidth = Math.Min((int)width, fullBitmap.Width - cropX);
            int cropHeight = Math.Min((int)height, fullBitmap.Height - cropY);

            if (cropWidth <= 0 || cropHeight <= 0)
            {
                vm.StatusMessage = "Invalid capture region.";
                return;
            }

            var cropRect = new System.Drawing.Rectangle(cropX, cropY, cropWidth, cropHeight);

            // Crop the region
            using var croppedBitmap = fullBitmap.Clone(cropRect, fullBitmap.PixelFormat);

            // Save to temp file
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), 
                $"screen_capture_{Guid.NewGuid()}.png");
            croppedBitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);

            // Show preview that follows mouse
            vm.CurrentEditMode = EditMode.AddImage;
            OnImageSelectedForPlacement(tempPath);
            
            vm.StatusMessage = "Click to place captured image. Right-click to cancel.";
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Screen capture failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Screen Capture Error: {ex}");
        }
    }
    
    #endregion

    // Inline editing state
    private WpfTextBox? _editingTextBox;
    private Border? _editingBorder;
    private TextAnnotationItem? _editingAnnotation;

    #region Drag & Drop Support

    private void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Check for double-click to edit text
        if (e.ClickCount == 2 && sender is Border border && border.Tag is TextAnnotationItem textAnnotation)
        {
            StartInlineEdit(border, textAnnotation);
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement element && element.Tag is AnnotationItem annotation)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(AnnotationCanvas);
            _draggedElement = element;
            _draggedAnnotation = annotation;
            
            // Store original position for undo
            _dragOriginalX = annotation.X;
            _dragOriginalY = annotation.Y;
            
            element.CaptureMouse();
            
            // Select this annotation in ViewModel and show resize handles
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedAnnotation = annotation;
                
                // Show resize handles for resizable elements (images and shapes)
                if (annotation is ImageAnnotationItem || annotation is ShapeAnnotationItem)
                {
                    _resizeHandlesManager?.ShowHandles(element, annotation);
                }
            }
            
            e.Handled = true;
        }
    }

    private void Element_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_isDragging && _draggedElement != null && _draggedAnnotation != null)
        {
            var currentPosition = e.GetPosition(AnnotationCanvas);
            var delta = currentPosition - _dragStartPoint;
            
            // Update element position on canvas
            double newLeft = Canvas.GetLeft(_draggedElement) + delta.X;
            double newTop = Canvas.GetTop(_draggedElement) + delta.Y;
            
            Canvas.SetLeft(_draggedElement, newLeft);
            Canvas.SetTop(_draggedElement, newTop);
            
            _dragStartPoint = currentPosition;
            e.Handled = true;
        }
    }

    private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging && _draggedElement != null && _draggedAnnotation != null)
        {
            _draggedElement.ReleaseMouseCapture();
            
            // Update annotation position in ViewModel
            if (DataContext is MainViewModel vm)
            {
                double newX = Canvas.GetLeft(_draggedElement) / vm.ZoomScale;
                double newY = Canvas.GetTop(_draggedElement) / vm.ZoomScale;
                
                // Record move action for undo if position changed significantly
                if (Math.Abs(newX - _dragOriginalX) > 0.1 || Math.Abs(newY - _dragOriginalY) > 0.1)
                {
                    var moveAction = new Services.MoveAnnotationAction(
                        _draggedAnnotation, 
                        _dragOriginalX, _dragOriginalY, 
                        newX, newY);
                    vm.RecordUndoableAction(moveAction);
                }
                
                _draggedAnnotation.X = newX;
                _draggedAnnotation.Y = newY;
                
                // Update resize handles position after dragging
                if (_draggedAnnotation is ImageAnnotationItem || _draggedAnnotation is ShapeAnnotationItem)
                {
                    if (_draggedElement is FrameworkElement fe)
                    {
                        _resizeHandlesManager?.ShowHandles(fe, _draggedAnnotation);
                    }
                }
            }
            
            _isDragging = false;
            _draggedElement = null;
            _draggedAnnotation = null;
            e.Handled = true;
        }
    }

    #endregion

    #region Inline Text Editing

    private void StartInlineEdit(Border border, TextAnnotationItem annotation)
    {
        if (_editingTextBox != null) return; // Already editing

        var textBlock = border.Child as TextBlock;
        if (textBlock == null) return;

        _editingBorder = border;
        _editingAnnotation = annotation;

        // Create TextBox with same styling as TextBlock
        _editingTextBox = new WpfTextBox
        {
            Text = textBlock.Text,
            FontSize = textBlock.FontSize,
            FontFamily = textBlock.FontFamily,
            FontWeight = textBlock.FontWeight,
            FontStyle = textBlock.FontStyle,
            Foreground = textBlock.Foreground,
            Background = border.Background == WpfBrushes.Transparent 
                ? new SolidColorBrush(WpfColor.FromArgb(240, 255, 255, 255)) 
                : border.Background,
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(33, 150, 243)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(2),
            MinWidth = 50,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap
        };

        // Handle key events
        _editingTextBox.KeyDown += EditingTextBox_KeyDown;
        _editingTextBox.LostFocus += EditingTextBox_LostFocus;

        // Replace TextBlock with TextBox
        border.Child = _editingTextBox;
        
        // Focus and select all text
        _editingTextBox.Focus();
        _editingTextBox.SelectAll();

        if (DataContext is MainViewModel vm)
        {
            vm.StatusMessage = "Editing text. Press Enter to confirm, Esc to cancel.";
        }
    }

    private void EditingTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FinishInlineEdit(save: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FinishInlineEdit(save: false);
            e.Handled = true;
        }
    }

    private void EditingTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Save on lost focus
        FinishInlineEdit(save: true);
    }

    private void FinishInlineEdit(bool save)
    {
        if (_editingTextBox == null || _editingBorder == null || _editingAnnotation == null) return;

        string newText = _editingTextBox.Text;
        var vm = DataContext as MainViewModel;

        // Unsubscribe events
        _editingTextBox.KeyDown -= EditingTextBox_KeyDown;
        _editingTextBox.LostFocus -= EditingTextBox_LostFocus;

        if (save && !string.IsNullOrWhiteSpace(newText))
        {
            // Update annotation
            _editingAnnotation.Text = newText;

            // Re-measure text size
            if (vm != null)
            {
                var (width, height) = MeasureTextSize(
                    newText,
                    _editingAnnotation.FontFamily,
                    _editingAnnotation.FontSize,
                    _editingAnnotation.IsBold,
                    _editingAnnotation.IsItalic);
                _editingAnnotation.Width = width;
                _editingAnnotation.Height = height;

                vm.StatusMessage = "Text updated.";
            }
        }
        else if (vm != null)
        {
            vm.StatusMessage = "Edit cancelled.";
        }

        // Clear editing state
        _editingTextBox = null;
        _editingBorder = null;
        _editingAnnotation = null;

        // Refresh display
        RefreshAnnotationPreviews();
    }

    /// <summary>
    /// Measure text size (copy from ViewModel for use in code-behind)
    /// Font size is expected in POINTS, will be converted to DIPs for WPF
    /// </summary>
    private static (double Width, double Height) MeasureTextSize(string text, string fontFamily, float fontSizePoints, bool isBold, bool isItalic)
    {
        // Convert font size from points to DIPs for WPF measurement
        const double POINTS_TO_DIPS = 96.0 / 72.0;
        double fontSizeDips = fontSizePoints * POINTS_TO_DIPS;
        
        var typeface = new System.Windows.Media.Typeface(
            new WpfFontFamily(fontFamily),
            isItalic ? FontStyles.Italic : FontStyles.Normal,
            isBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        var formattedText = new System.Windows.Media.FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSizeDips,
            WpfBrushes.Black,
            new System.Windows.Media.NumberSubstitution(),
            System.Windows.Media.TextFormattingMode.Display,
            96);

        return (formattedText.Width + 4, formattedText.Height + 4);
    }

    #endregion

    #region Context Menu

    private void Element_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is AnnotationItem annotation)
        {
            // Select this annotation
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedAnnotation = annotation;
            }
            
            // Show context menu
            var contextMenu = new ContextMenu();
            
            var copyItem = new MenuItem { Header = "Copy", Icon = new TextBlock { Text = "📋" } };
            copyItem.Click += (s, args) => CopyAnnotation(annotation);
            
            var deleteItem = new MenuItem { Header = "Delete", Icon = new TextBlock { Text = "🗑️" } };
            deleteItem.Click += (s, args) => DeleteAnnotation(annotation);
            
            var separator = new Separator();
            
            var bringToFrontItem = new MenuItem { Header = "Bring to Front", Icon = new TextBlock { Text = "⬆️" } };
            bringToFrontItem.Click += (s, args) => BringToFront(element);
            
            var sendToBackItem = new MenuItem { Header = "Send to Back", Icon = new TextBlock { Text = "⬇️" } };
            sendToBackItem.Click += (s, args) => SendToBack(element);
            
            contextMenu.Items.Add(copyItem);
            contextMenu.Items.Add(deleteItem);
            contextMenu.Items.Add(separator);
            contextMenu.Items.Add(bringToFrontItem);
            contextMenu.Items.Add(sendToBackItem);

            // Add Crop Image and OCR Image for image annotations
            if (annotation is ImageAnnotationItem imageAnnotation)
            {
                contextMenu.Items.Add(new Separator());

                var cropItem = new MenuItem { Header = "Crop Image" };
                cropItem.Click += (s, args) => StartImageAnnotationCrop(element, imageAnnotation);
                contextMenu.Items.Add(cropItem);

                var ocrItem = new MenuItem { Header = "OCR Image" };
                ocrItem.Click += async (s, args) => await OcrImageAnnotation(element, imageAnnotation);
                contextMenu.Items.Add(ocrItem);

                var removeBackgroundItem = new MenuItem { Header = "Remove Background" };
                removeBackgroundItem.Click += async (s, args) =>
                {
                    if (DataContext is MainViewModel vmBg)
                    {
                        await vmBg.TryRemoveBackgroundAsync(imageAnnotation);
                    }
                };
                contextMenu.Items.Add(removeBackgroundItem);
                
                var advancedRemoveItem = new MenuItem { Header = "Advanced Remove Background..." };
                advancedRemoveItem.Click += (s, args) =>
                {
                    OpenAdvancedBackgroundRemoval(imageAnnotation);
                };
                contextMenu.Items.Add(advancedRemoveItem);
            }

            
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void CopyAnnotation(AnnotationItem annotation)
    {
        if (DataContext is not MainViewModel vm) return;

        AnnotationItem? newAnnotation = null;

        if (annotation is TextAnnotationItem textItem)
        {
            newAnnotation = new TextAnnotationItem
            {
                PageNumber = textItem.PageNumber,
                X = textItem.X + 20,
                Y = textItem.Y + 20,
                Text = textItem.Text,
                FontFamily = textItem.FontFamily,
                FontSize = textItem.FontSize,
                Color = textItem.Color,
                BackgroundColor = textItem.BackgroundColor,
                BorderColor = textItem.BorderColor,
                BorderWidth = textItem.BorderWidth,
                IsBold = textItem.IsBold,
                IsItalic = textItem.IsItalic,
                IsUnderline = textItem.IsUnderline
            };
        }
        else if (annotation is ImageAnnotationItem imgItem)
        {
            newAnnotation = new ImageAnnotationItem
            {
                PageNumber = imgItem.PageNumber,
                X = imgItem.X + 20,
                Y = imgItem.Y + 20,
                Width = imgItem.Width,
                Height = imgItem.Height,
                ImagePath = imgItem.ImagePath
            };
        }
        else if (annotation is ShapeAnnotationItem shapeItem)
        {
            newAnnotation = new ShapeAnnotationItem
            {
                PageNumber = shapeItem.PageNumber,
                X = shapeItem.X + 20,
                Y = shapeItem.Y + 20,
                Width = shapeItem.Width,
                Height = shapeItem.Height,
                ShapeType = shapeItem.ShapeType,
                FillColor = shapeItem.FillColor,
                StrokeColor = shapeItem.StrokeColor,
                StrokeWidth = shapeItem.StrokeWidth,
                X2 = shapeItem.X2 + 20,
                Y2 = shapeItem.Y2 + 20
            };
        }

        if (newAnnotation != null)
        {
            vm.Annotations.Add(newAnnotation);
            vm.SelectedAnnotation = newAnnotation;
            RefreshAnnotationPreviews();
            vm.StatusMessage = "Element copied.";
        }
    }

    private void DeleteAnnotation(AnnotationItem annotation)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Annotations.Remove(annotation);
            if (vm.SelectedAnnotation == annotation)
            {
                vm.SelectedAnnotation = null;
            }
            RefreshAnnotationPreviews();
            vm.StatusMessage = "Element deleted.";
        }
    }

    private void BringToFront(UIElement element)
    {
        int maxZIndex = 0;
        foreach (UIElement child in AnnotationCanvas.Children)
        {
            int z = WpfPanel.GetZIndex(child);
            if (z > maxZIndex) maxZIndex = z;
        }
        WpfPanel.SetZIndex(element, maxZIndex + 1);
    }

    private void SendToBack(UIElement element)
    {
        int minZIndex = 0;
        foreach (UIElement child in AnnotationCanvas.Children)
        {
            int z = WpfPanel.GetZIndex(child);
            if (z < minZIndex) minZIndex = z;
        }
        WpfPanel.SetZIndex(element, minZIndex - 1);
    }
    
    private void OpenAdvancedBackgroundRemoval(ImageAnnotationItem imageAnnotation)
    {
        if (DataContext is not MainViewModel vm) return;
        
        if (string.IsNullOrWhiteSpace(imageAnnotation.ImagePath) || !System.IO.File.Exists(imageAnnotation.ImagePath))
        {
            vm.StatusMessage = "Image file not found for advanced removal.";
            return;
        }
        
        var dialog = new AdvancedBackgroundRemovalDialog(
            imageAnnotation.ImagePath, 
            vm.BackgroundRemovalService)
        {
            Owner = this
        };
        
        if (dialog.ShowDialog() == true && dialog.ResultImageBytes != null)
        {
            // Save result to temp file
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"advanced_bg_removed_{Guid.NewGuid()}.png");
            
            System.IO.File.WriteAllBytes(tempPath, dialog.ResultImageBytes);
            
            // Update the annotation
            imageAnnotation.ImagePath = tempPath;
            
            RefreshAnnotationPreviews();
            vm.StatusMessage = "Background removed with advanced tool.";
        }
    }

    #endregion

    #region Annotation Preview Rendering

    /// <summary>
    /// Add visual preview of annotation on canvas
    /// </summary>
    public void AddAnnotationPreview(object annotation, AnnotationItem annotationItem, double zoomScale)
    {
        if (annotation is TextAnnotation textAnn)
        {
            AddTextPreview(textAnn, zoomScale, annotationItem);
        }
        else if (annotation is ImageAnnotation imgAnn)
        {
            AddImagePreview(imgAnn, zoomScale, annotationItem);
        }
        else if (annotation is ShapeAnnotation shapeAnn)
        {
            AddShapePreview(shapeAnn, zoomScale, annotationItem);
        }
    }

    private void AddTextPreview(TextAnnotation textAnn, double zoomScale, AnnotationItem? annotationItem)
    {
        try
        {
            var textColor = ParseWpfColor(textAnn.Color, Colors.Black);
            var bgColor = ParseWpfColor(textAnn.BackgroundColor, Colors.Transparent);
            var borderColor = ParseWpfColor(textAnn.BorderColor, Colors.Transparent);

            // Convert font size from points to WPF DIPs (Device-Independent Pixels)
            // PDF uses points (72 per inch), WPF uses DIPs (96 per inch)
            // DIPs = points * (96/72) = points * 1.333
            const double POINTS_TO_DIPS = 96.0 / 72.0;
            double fontSizeInDips = textAnn.FontSize * POINTS_TO_DIPS * zoomScale;
            
            var textBlock = new TextBlock
            {
                Text = textAnn.Text ?? string.Empty,
                FontSize = Math.Max(1, fontSizeInDips),
                FontFamily = new WpfFontFamily(textAnn.FontFamily ?? "Arial"),
                Foreground = new SolidColorBrush(textColor),
                FontWeight = textAnn.IsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = textAnn.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                Padding = new Thickness(2),
                TextAlignment = textAnn.TextAlignment switch
                {
                    Models.TextAlignment.Center => System.Windows.TextAlignment.Center,
                    Models.TextAlignment.Right => System.Windows.TextAlignment.Right,
                    _ => System.Windows.TextAlignment.Left
                }
            };

            // Add underline if specified
            if (textAnn.IsUnderline)
            {
                textBlock.TextDecorations = TextDecorations.Underline;
            }

            // Create border - NO yellow highlight, true transparent
            var border = new Border
            {
                Background = bgColor == Colors.Transparent 
                    ? WpfBrushes.Transparent 
                    : new SolidColorBrush(bgColor),
                BorderBrush = borderColor == Colors.Transparent 
                    ? WpfBrushes.Transparent 
                    : new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(textAnn.BorderWidth * zoomScale),
                Child = textBlock,
                Cursor = WpfCursors.SizeAll,
                Tag = annotationItem
            };

            // Add selection indicator when hovering
            border.MouseEnter += (s, e) => {
                if (border.BorderBrush == WpfBrushes.Transparent)
                    border.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(100, 33, 150, 243));
                if (border.BorderThickness.Left == 0)
                    border.BorderThickness = new Thickness(1);
            };
            border.MouseLeave += (s, e) => {
                if (annotationItem != null)
                {
                    var bc = ParseWpfColor(textAnn.BorderColor, Colors.Transparent);
                    border.BorderBrush = bc == Colors.Transparent ? WpfBrushes.Transparent : new SolidColorBrush(bc);
                    border.BorderThickness = new Thickness(textAnn.BorderWidth * zoomScale);
                }
            };

            // Add drag & drop and right-click handlers
            if (annotationItem != null)
            {
                border.MouseLeftButtonDown += Element_MouseLeftButtonDown;
                border.MouseMove += Element_MouseMove;
                border.MouseLeftButtonUp += Element_MouseLeftButtonUp;
                border.MouseRightButtonDown += Element_MouseRightButtonDown;
            }

            Canvas.SetLeft(border, textAnn.X * zoomScale);
            Canvas.SetTop(border, textAnn.Y * zoomScale);

            AnnotationCanvas.Children.Add(border);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding text preview: {ex.Message}");
        }
    }

    private void AddShapePreview(ShapeAnnotation shapeAnn, double zoomScale, AnnotationItem? annotationItem)
    {
        try
        {
            var fillColor = ParseWpfColor(shapeAnn.FillColor, Colors.Transparent);
            var strokeColor = ParseWpfColor(shapeAnn.StrokeColor, Colors.Black);

            FrameworkElement shape;

            switch (shapeAnn.ShapeType)
            {
                case ShapeType.Rectangle:
                    shape = new WpfRectangle
                    {
                        Width = shapeAnn.Width * zoomScale,
                        Height = shapeAnn.Height * zoomScale,
                        Fill = fillColor == Colors.Transparent ? WpfBrushes.Transparent : new SolidColorBrush(fillColor),
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = shapeAnn.StrokeWidth * zoomScale,
                        Cursor = WpfCursors.SizeAll,
                        Tag = annotationItem
                    };
                    Canvas.SetLeft(shape, shapeAnn.X * zoomScale);
                    Canvas.SetTop(shape, shapeAnn.Y * zoomScale);
                    break;

                case ShapeType.Ellipse:
                    shape = new WpfEllipse
                    {
                        Width = shapeAnn.Width * zoomScale,
                        Height = shapeAnn.Height * zoomScale,
                        Fill = fillColor == Colors.Transparent ? WpfBrushes.Transparent : new SolidColorBrush(fillColor),
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = shapeAnn.StrokeWidth * zoomScale,
                        Cursor = WpfCursors.SizeAll,
                        Tag = annotationItem
                    };
                    Canvas.SetLeft(shape, shapeAnn.X * zoomScale);
                    Canvas.SetTop(shape, shapeAnn.Y * zoomScale);
                    break;

                case ShapeType.Line:
                    // For lines, wrap in a Canvas for easier dragging
                    var lineCanvas = new Canvas
                    {
                        Width = Math.Abs(shapeAnn.X2 - shapeAnn.X) * zoomScale + 10,
                        Height = Math.Abs(shapeAnn.Y2 - shapeAnn.Y) * zoomScale + 10,
                        Background = WpfBrushes.Transparent,
                        Cursor = WpfCursors.SizeAll,
                        Tag = annotationItem
                    };
                    var line = new WpfLine
                    {
                        X1 = 5,
                        Y1 = 5,
                        X2 = (shapeAnn.X2 - shapeAnn.X) * zoomScale + 5,
                        Y2 = (shapeAnn.Y2 - shapeAnn.Y) * zoomScale + 5,
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = shapeAnn.StrokeWidth * zoomScale
                    };
                    lineCanvas.Children.Add(line);
                    shape = lineCanvas;
                    Canvas.SetLeft(shape, Math.Min(shapeAnn.X, shapeAnn.X2) * zoomScale - 5);
                    Canvas.SetTop(shape, Math.Min(shapeAnn.Y, shapeAnn.Y2) * zoomScale - 5);
                    break;

                default:
                    return;
            }

            // Add drag & drop and right-click handlers
            if (annotationItem != null)
            {
                shape.MouseLeftButtonDown += Element_MouseLeftButtonDown;
                shape.MouseMove += Element_MouseMove;
                shape.MouseLeftButtonUp += Element_MouseLeftButtonUp;
                shape.MouseRightButtonDown += Element_MouseRightButtonDown;
            }

            AnnotationCanvas.Children.Add(shape);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding shape preview: {ex.Message}");
        }
    }

    private void AddImagePreview(ImageAnnotation imgAnn, double zoomScale, AnnotationItem? annotationItem)
    {
        try
        {
            if (string.IsNullOrEmpty(imgAnn.ImagePath) || !System.IO.File.Exists(imgAnn.ImagePath))
            {
                System.Diagnostics.Debug.WriteLine($"Image file not found: {imgAnn.ImagePath}");
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imgAnn.ImagePath);
            var decodeWidth = (int)(imgAnn.Width * zoomScale);
            if (decodeWidth > 0)
            {
                bitmap.DecodePixelWidth = decodeWidth;
            }
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new WpfImage
            {
                Source = bitmap,
                Width = imgAnn.Width * zoomScale,
                Height = imgAnn.Height * zoomScale
            };

            var border = new Border
            {
                BorderBrush = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Background = WpfBrushes.Transparent,
                Child = image,
                Cursor = WpfCursors.SizeAll,
                Tag = annotationItem
            };

            // Add selection indicator when hovering
            border.MouseEnter += (s, e) => {
                border.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(150, 33, 150, 243));
                border.BorderThickness = new Thickness(2);
            };
            border.MouseLeave += (s, e) => {
                border.BorderBrush = WpfBrushes.Transparent;
                border.BorderThickness = new Thickness(0);
            };

            // Add drag & drop and right-click handlers
            if (annotationItem != null)
            {
                border.MouseLeftButtonDown += Element_MouseLeftButtonDown;
                border.MouseMove += Element_MouseMove;
                border.MouseLeftButtonUp += Element_MouseLeftButtonUp;
                border.MouseRightButtonDown += Element_MouseRightButtonDown;
            }

            Canvas.SetLeft(border, imgAnn.X * zoomScale);
            Canvas.SetTop(border, imgAnn.Y * zoomScale);

            AnnotationCanvas.Children.Add(border);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading image preview: {ex.Message}");
        }
    }

    private static WpfColor ParseWpfColor(string colorString, WpfColor defaultColor)
    {
        if (string.IsNullOrEmpty(colorString) || colorString == "Transparent")
        {
            return defaultColor;
        }

        try
        {
            return (WpfColor)WpfColorConverter.ConvertFromString(colorString);
        }
        catch
        {
            return defaultColor;
        }
    }

    #endregion

    #region Canvas Management

    /// <summary>
    /// Clear all annotation previews (called after save or page change)
    /// </summary>
    public void ClearAnnotationPreviews()
    {
        _resizeHandlesManager?.HideHandles();
        AnnotationCanvas.Children.Clear();
    }

    /// <summary>
    /// Refresh annotation previews from ViewModel's annotation list
    /// </summary>
    private void RefreshAnnotationPreviews()
    {
        if (DataContext is not MainViewModel vm) return;

        // Clear existing previews
        AnnotationCanvas.Children.Clear();

        // Re-render all annotations for current page
        foreach (var annotation in vm.GetCurrentPageAnnotations())
        {
            if (annotation is TextAnnotationItem textItem)
            {
                var textAnn = textItem.ToAnnotation();
                AddTextPreview(textAnn, vm.ZoomScale, textItem);
            }
            else if (annotation is ImageAnnotationItem imgItem)
            {
                var imgAnn = imgItem.ToAnnotation();
                AddImagePreview(imgAnn, vm.ZoomScale, imgItem);
            }
            else if (annotation is ShapeAnnotationItem shapeItem)
            {
                var shapeAnn = shapeItem.ToAnnotation();
                AddShapePreview(shapeAnn, vm.ZoomScale, shapeItem);
            }
        }

        // Also render extracted content if in SelectContent mode
        if (vm.IsContentExtracted)
        {
            RenderExtractedContent(vm);
        }
    }

    /// <summary>
    /// Refresh extracted content display on canvas
    /// </summary>
    private void RefreshExtractedContentOnCanvas()
    {
        if (DataContext is not MainViewModel vm) return;

        // Refresh regular annotations first (clears canvas)
        RefreshAnnotationPreviews();
    }

    /// <summary>
    /// Render extracted content overlays on canvas
    /// </summary>
    private void RenderExtractedContent(MainViewModel vm)
    {
        var zoomScale = vm.ZoomScale;

        // Render extracted text items
        foreach (var textItem in vm.ExtractedTextItems)
        {
            if (textItem.IsDeleted) continue; // Skip deleted items

            var border = new Border
            {
                BorderBrush = textItem.IsModified
                    ? new SolidColorBrush(WpfColor.FromRgb(255, 165, 0))  // Orange for modified
                    : new SolidColorBrush(WpfColor.FromRgb(33, 150, 243)), // Blue for original
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(WpfColor.FromArgb(30, 33, 150, 243)),
                CornerRadius = new CornerRadius(2),
                Cursor = WpfCursors.SizeAll,
                Tag = textItem
            };

            // Set dashed border style for extracted content
            border.BorderBrush = new VisualBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new System.Windows.Rect(0, 0, 4, 4),
                ViewportUnits = BrushMappingMode.Absolute,
                Visual = new WpfRectangle
                {
                    Width = 4,
                    Height = 4,
                    Fill = textItem.IsModified
                        ? new SolidColorBrush(WpfColor.FromRgb(255, 165, 0))
                        : new SolidColorBrush(WpfColor.FromRgb(33, 150, 243))
                }
            };

            // Calculate position (PDF Y is from bottom, WPF Y is from top)
            double screenX = textItem.X * zoomScale;
            double screenY = textItem.Y * zoomScale;

            // Get page height to flip Y coordinate
            var (pageWidth, pageHeight) = vm.GetCurrentPageDimensionsInPoints();
            screenY = (pageHeight - textItem.Y - textItem.Height) * zoomScale;

            border.Width = textItem.Width * zoomScale;
            border.Height = textItem.Height * zoomScale;

            // Add text preview
            var textBlock = new TextBlock
            {
                Text = textItem.Text.Length > 50 ? textItem.Text[..50] + "..." : textItem.Text,
                FontSize = Math.Max(8, textItem.FontSize * zoomScale * 0.5),
                Foreground = WpfBrushes.DarkBlue,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(2)
            };
            border.Child = textBlock;

            Canvas.SetLeft(border, screenX);
            Canvas.SetTop(border, screenY);

            // Add context menu for extracted text
            border.ContextMenu = CreateExtractedTextContextMenu(textItem);

            // Add event handlers for drag
            border.MouseLeftButtonDown += ExtractedContent_MouseLeftButtonDown;
            border.MouseMove += ExtractedContent_MouseMove;
            border.MouseLeftButtonUp += ExtractedContent_MouseLeftButtonUp;

            AnnotationCanvas.Children.Add(border);
        }

        // Render extracted image items
        foreach (var imageItem in vm.ExtractedImageItems)
        {
            if (imageItem.IsDeleted) continue; // Skip deleted items

            var border = new Border
            {
                BorderBrush = imageItem.IsModified
                    ? new SolidColorBrush(WpfColor.FromRgb(255, 165, 0))  // Orange for modified
                    : new SolidColorBrush(WpfColor.FromRgb(76, 175, 80)), // Green for images
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(WpfColor.FromArgb(30, 76, 175, 80)),
                CornerRadius = new CornerRadius(2),
                Cursor = WpfCursors.SizeAll,
                Tag = imageItem
            };

            // Calculate position
            double screenX = imageItem.X * zoomScale;
            var (pageWidth, pageHeight) = vm.GetCurrentPageDimensionsInPoints();
            double screenY = (pageHeight - imageItem.Y - imageItem.Height) * zoomScale;

            border.Width = imageItem.Width * zoomScale;
            border.Height = imageItem.Height * zoomScale;

            // Try to display a preview of the image
            if (imageItem.ImageBytes.Length > 0)
            {
                try
                {
                    var bitmapImage = new BitmapImage();
                    using var ms = new System.IO.MemoryStream(imageItem.ImageBytes);
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = ms;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    var image = new WpfImage
                    {
                        Source = bitmapImage,
                        Stretch = Stretch.Uniform,
                        Opacity = 0.7
                    };
                    border.Child = image;
                }
                catch
                {
                    // If image can't be displayed, show placeholder
                    border.Child = new TextBlock
                    {
                        Text = "Image",
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Foreground = WpfBrushes.DarkGreen
                    };
                }
            }

            Canvas.SetLeft(border, screenX);
            Canvas.SetTop(border, screenY);

            // Add context menu for extracted image
            border.ContextMenu = CreateExtractedImageContextMenu(imageItem);

            // Add event handlers for drag
            border.MouseLeftButtonDown += ExtractedContent_MouseLeftButtonDown;
            border.MouseMove += ExtractedContent_MouseMove;
            border.MouseLeftButtonUp += ExtractedContent_MouseLeftButtonUp;

            AnnotationCanvas.Children.Add(border);
        }
    }

    #region Extracted Content Context Menus

    /// <summary>
    /// Create context menu for extracted text item
    /// </summary>
    private ContextMenu CreateExtractedTextContextMenu(ExtractedTextItem textItem)
    {
        var menu = new ContextMenu();

        // Convert to editable text annotation
        var convertItem = new MenuItem
        {
            Header = "Convert to Text Annotation",
            Icon = new TextBlock { Text = "📝", FontSize = 14 }
        };
        convertItem.Click += (s, e) => ConvertExtractedTextToAnnotation(textItem);
        menu.Items.Add(convertItem);

        menu.Items.Add(new Separator());

        // Delete option
        var deleteItem = new MenuItem
        {
            Header = textItem.IsDeleted ? "Restore" : "Delete",
            Icon = new TextBlock { Text = textItem.IsDeleted ? "↩️" : "🗑️", FontSize = 14 }
        };
        deleteItem.Click += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                if (textItem.IsDeleted)
                    vm.RestoreExtractedTextCommand.Execute(textItem);
                else
                    vm.DeleteExtractedTextCommand.Execute(textItem);
            }
        };
        menu.Items.Add(deleteItem);

        return menu;
    }

    /// <summary>
    /// Create context menu for extracted image item
    /// </summary>
    private ContextMenu CreateExtractedImageContextMenu(ExtractedImageItem imageItem)
    {
        var menu = new ContextMenu();

        // Convert to editable image annotation
        var convertItem = new MenuItem
        {
            Header = "Convert to Image Annotation",
            Icon = new TextBlock { Text = "🖼️", FontSize = 14 }
        };
        convertItem.Click += (s, e) => ConvertExtractedImageToAnnotation(imageItem);
        menu.Items.Add(convertItem);

        menu.Items.Add(new Separator());

        // Crop Image
        var cropItem = new MenuItem
        {
            Header = "Crop Image"
        };
        cropItem.Click += (s, e) => StartExtractedImageCrop(imageItem);
        menu.Items.Add(cropItem);

        // OCR Image
        var ocrItem = new MenuItem
        {
            Header = "OCR Image"
        };
        ocrItem.Click += async (s, e) => await OcrExtractedImage(imageItem);
        menu.Items.Add(ocrItem);


        menu.Items.Add(new Separator());

        // Delete option
        var deleteItem = new MenuItem
        {
            Header = imageItem.IsDeleted ? "Restore" : "Delete",
            Icon = new TextBlock { Text = imageItem.IsDeleted ? "↩️" : "🗑️", FontSize = 14 }
        };
        deleteItem.Click += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                if (imageItem.IsDeleted)
                    vm.RestoreExtractedImageCommand.Execute(imageItem);
                else
                    vm.DeleteExtractedImageCommand.Execute(imageItem);
            }
        };
        menu.Items.Add(deleteItem);

        return menu;
    }

    /// <summary>
    /// Convert extracted text to editable text annotation
    /// </summary>
    private void ConvertExtractedTextToAnnotation(ExtractedTextItem textItem)
    {
        if (DataContext is not MainViewModel vm) return;

        // Create new text annotation from extracted text
        var annotation = new TextAnnotationItem
        {
            PageNumber = textItem.PageNumber,
            X = textItem.X,
            Y = textItem.Y,
            Width = textItem.Width,
            Height = textItem.Height,
            Text = textItem.Text,
            FontFamily = !string.IsNullOrEmpty(textItem.FontName) ? textItem.FontName : "Arial",
            FontSize = textItem.FontSize > 0 ? textItem.FontSize : 12f,
            Color = textItem.Color ?? "#000000"
        };

        // Add to annotations
        vm.Annotations.Add(annotation);

        // Mark extracted item as deleted (will be redacted on save)
        textItem.IsDeleted = true;
        vm.DeleteExtractedTextCommand.Execute(textItem);

        vm.StatusMessage = $"Converted to editable annotation: {textItem.Text.Substring(0, Math.Min(20, textItem.Text.Length))}...";

        // Refresh display
        RefreshAnnotationPreviews();
    }

    /// <summary>
    /// Convert extracted image to editable image annotation
    /// </summary>
    private void ConvertExtractedImageToAnnotation(ExtractedImageItem imageItem)
    {
        if (DataContext is not MainViewModel vm) return;

        // Save image bytes to temp file
        string tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"extracted_image_{imageItem.ElementId}.{imageItem.Format}");

        try
        {
            System.IO.File.WriteAllBytes(tempPath, imageItem.ImageBytes);

            // Create new image annotation from extracted image
            var annotation = new ImageAnnotationItem
            {
                PageNumber = imageItem.PageNumber,
                X = imageItem.X,
                Y = imageItem.Y,
                Width = imageItem.Width,
                Height = imageItem.Height,
                ImagePath = tempPath
            };

            // Add to annotations
            vm.Annotations.Add(annotation);

            // Mark extracted item as deleted (will be redacted on save)
            imageItem.IsDeleted = true;
            vm.DeleteExtractedImageCommand.Execute(imageItem);

            vm.StatusMessage = $"Converted to editable image annotation";

            // Refresh display
            RefreshAnnotationPreviews();
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Error converting image: {ex.Message}";
        }
    }

    #endregion

    #region Image Crop and OCR

    /// <summary>
    /// Start crop selection for an image annotation using resize handles
    /// </summary>
    private void StartImageAnnotationCrop(FrameworkElement element, ImageAnnotationItem imageAnnotation)
    {
        if (_resizeHandlesManager == null) return;
        
        // Show handles and enter crop mode
        _resizeHandlesManager.ShowHandles(element, imageAnnotation);
        _resizeHandlesManager.EnterCropMode(element, imageAnnotation);
    }

    /// <summary>
    /// Start crop selection for an extracted image
    /// </summary>
    private void StartExtractedImageCrop(ExtractedImageItem imageItem)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.StatusMessage = "Drag to select crop area on image. Press Esc to cancel.";

        _imageCropAnnotation = null;
        _imageCropExtractedImage = imageItem;

        // Find the Border element for this extracted image on the canvas
        foreach (var child in AnnotationCanvas.Children)
        {
            if (child is Border border && border.Tag == imageItem)
            {
                _imageCropTargetElement = border;
                border.MouseLeftButtonDown += ImageCrop_MouseLeftButtonDown;
                border.Cursor = WpfCursors.Cross;
                break;
            }
        }
    }

    private void ImageCrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        _isImageCropSelecting = true;
        _imageCropStart = e.GetPosition(element);

        // Create selection rectangle with same style as OCR selection
        _imageCropRect = new WpfRectangle
        {
            Stroke = new SolidColorBrush(WpfColor.FromRgb(33, 150, 243)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(WpfColor.FromArgb(50, 33, 150, 243))
        };

        // Position the rectangle relative to the canvas (at element's position + start offset)
        double elementLeft = Canvas.GetLeft(element);
        double elementTop = Canvas.GetTop(element);

        Canvas.SetLeft(_imageCropRect, elementLeft + _imageCropStart.X);
        Canvas.SetTop(_imageCropRect, elementTop + _imageCropStart.Y);
        _imageCropRect.Width = 0;
        _imageCropRect.Height = 0;

        AnnotationCanvas.Children.Add(_imageCropRect);
        element.CaptureMouse();

        element.MouseMove += ImageCrop_MouseMove;
        element.MouseLeftButtonUp += ImageCrop_MouseLeftButtonUp;
        element.KeyDown += ImageCrop_KeyDown;

        e.Handled = true;
    }

    private void ImageCrop_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isImageCropSelecting || _imageCropRect == null) return;
        if (sender is not FrameworkElement element) return;

        var currentPoint = e.GetPosition(element);

        // Clamp to element bounds
        double maxWidth = element.ActualWidth;
        double maxHeight = element.ActualHeight;
        currentPoint.X = Math.Clamp(currentPoint.X, 0, maxWidth);
        currentPoint.Y = Math.Clamp(currentPoint.Y, 0, maxHeight);

        double startX = Math.Clamp(_imageCropStart.X, 0, maxWidth);
        double startY = Math.Clamp(_imageCropStart.Y, 0, maxHeight);

        // Calculate rectangle bounds
        double x = Math.Min(startX, currentPoint.X);
        double y = Math.Min(startY, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - startX);
        double height = Math.Abs(currentPoint.Y - startY);

        // Update rectangle position on canvas
        double elementLeft = Canvas.GetLeft(element);
        double elementTop = Canvas.GetTop(element);

        Canvas.SetLeft(_imageCropRect, elementLeft + x);
        Canvas.SetTop(_imageCropRect, elementTop + y);
        _imageCropRect.Width = width;
        _imageCropRect.Height = height;
    }

    private void ImageCrop_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isImageCropSelecting || _imageCropRect == null) return;
        if (sender is not FrameworkElement element) return;

        _isImageCropSelecting = false;
        element.ReleaseMouseCapture();
        element.MouseMove -= ImageCrop_MouseMove;
        element.MouseLeftButtonUp -= ImageCrop_MouseLeftButtonUp;
        element.MouseLeftButtonDown -= ImageCrop_MouseLeftButtonDown;
        element.KeyDown -= ImageCrop_KeyDown;
        element.Cursor = WpfCursors.SizeAll;

        // Get selection bounds relative to the element
        double elementLeft = Canvas.GetLeft(element);
        double elementTop = Canvas.GetTop(element);
        double rectLeft = Canvas.GetLeft(_imageCropRect);
        double rectTop = Canvas.GetTop(_imageCropRect);

        double selectionX = rectLeft - elementLeft;
        double selectionY = rectTop - elementTop;
        double selectionWidth = _imageCropRect.Width;
        double selectionHeight = _imageCropRect.Height;

        // Remove selection rectangle
        AnnotationCanvas.Children.Remove(_imageCropRect);
        _imageCropRect = null;

        // Minimum size check
        if (selectionWidth < 5 || selectionHeight < 5)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.StatusMessage = "Selection too small. Please drag a larger area.";
            }
            CleanupImageCropState();
            return;
        }

        // Apply crop
        if (DataContext is MainViewModel vmCrop)
        {
            if (_imageCropAnnotation != null)
            {
                vmCrop.TryCropImageAnnotation(_imageCropAnnotation, selectionX, selectionY, selectionWidth, selectionHeight);
            }
            else if (_imageCropExtractedImage != null)
            {
                vmCrop.TryCropExtractedImage(_imageCropExtractedImage, selectionX, selectionY, selectionWidth, selectionHeight);
            }
        }

        CleanupImageCropState();
        e.Handled = true;
    }

    private void ImageCrop_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelImageCrop(sender as FrameworkElement);
            e.Handled = true;
        }
    }

    private void CancelImageCrop(FrameworkElement? element)
    {
        _isImageCropSelecting = false;

        if (element != null)
        {
            element.ReleaseMouseCapture();
            element.MouseMove -= ImageCrop_MouseMove;
            element.MouseLeftButtonUp -= ImageCrop_MouseLeftButtonUp;
            element.MouseLeftButtonDown -= ImageCrop_MouseLeftButtonDown;
            element.KeyDown -= ImageCrop_KeyDown;
            element.Cursor = WpfCursors.SizeAll;
        }

        if (_imageCropRect != null)
        {
            AnnotationCanvas.Children.Remove(_imageCropRect);
            _imageCropRect = null;
        }

        CleanupImageCropState();

        if (DataContext is MainViewModel vm)
        {
            vm.StatusMessage = "Crop cancelled.";
        }
    }

    private void CleanupImageCropState()
    {
        _imageCropAnnotation = null;
        _imageCropExtractedImage = null;
        _imageCropTargetElement = null;
    }

    /// <summary>
    /// Perform OCR on an image annotation (full image)
    /// </summary>
    private async Task OcrImageAnnotation(FrameworkElement element, ImageAnnotationItem imageAnnotation)
    {
        if (DataContext is not MainViewModel vm) return;

        if (!_ocrService.IsInitialized)
        {
            vm.StatusMessage = "OCR not initialized. Download tessdata files first.";
            System.Windows.MessageBox.Show(
                "OCR engine not initialized.\n\n" +
                "Please download Tesseract trained data files:\n" +
                "1. Download eng.traineddata and tha.traineddata from:\n" +
                "   https://github.com/tesseract-ocr/tessdata\n\n" +
                "2. Place them in:\n" +
                $"   {System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata")}",
                "OCR Setup Required",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(imageAnnotation.ImagePath) || !System.IO.File.Exists(imageAnnotation.ImagePath))
        {
            vm.StatusMessage = "Image file not found.";
            return;
        }

        vm.StatusMessage = "Performing OCR on image...";

        try
        {
            using var bitmap = new System.Drawing.Bitmap(imageAnnotation.ImagePath);
            string text = await Task.Run(() => _ocrService.RecognizeText(bitmap));

            if (string.IsNullOrWhiteSpace(text))
            {
                vm.StatusMessage = "No text detected in image.";
                return;
            }

            // Clean up text
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            // Get screen coords of the image annotation
            double screenX = imageAnnotation.X * vm.ZoomScale;
            double screenY = imageAnnotation.Y * vm.ZoomScale;
            double screenWidth = imageAnnotation.Width * vm.ZoomScale;
            double screenHeight = imageAnnotation.Height * vm.ZoomScale;

            vm.CreateTextFromOcr(text, screenX, screenY, screenWidth, screenHeight, AddAnnotationPreview);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"OCR failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OCR Error: {ex}");
        }
    }

    /// <summary>
    /// Perform OCR on an extracted image (full image)
    /// </summary>
    private async Task OcrExtractedImage(ExtractedImageItem imageItem)
    {
        if (DataContext is not MainViewModel vm) return;

        if (!_ocrService.IsInitialized)
        {
            vm.StatusMessage = "OCR not initialized. Download tessdata files first.";
            System.Windows.MessageBox.Show(
                "OCR engine not initialized.\n\n" +
                "Please download Tesseract trained data files:\n" +
                "1. Download eng.traineddata and tha.traineddata from:\n" +
                "   https://github.com/tesseract-ocr/tessdata\n\n" +
                "2. Place them in:\n" +
                $"   {System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata")}",
                "OCR Setup Required",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (imageItem.ImageBytes.Length == 0)
        {
            vm.StatusMessage = "Image data is empty.";
            return;
        }

        vm.StatusMessage = "Performing OCR on extracted image...";

        try
        {
            using var stream = new System.IO.MemoryStream(imageItem.ImageBytes);
            using var bitmap = new System.Drawing.Bitmap(stream);
            string text = await Task.Run(() => _ocrService.RecognizeText(bitmap));

            if (string.IsNullOrWhiteSpace(text))
            {
                vm.StatusMessage = "No text detected in image.";
                return;
            }

            // Clean up text
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            // Compute screen coords for the extracted image (same as RenderExtractedContent)
            var (pageWidth, pageHeight) = vm.GetCurrentPageDimensionsInPoints();
            double screenX = imageItem.X * vm.ZoomScale;
            double screenY = (pageHeight - imageItem.Y - imageItem.Height) * vm.ZoomScale;
            double screenWidth = imageItem.Width * vm.ZoomScale;
            double screenHeight = imageItem.Height * vm.ZoomScale;

            vm.CreateTextFromOcr(text, screenX, screenY, screenWidth, screenHeight, AddAnnotationPreview);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"OCR failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"OCR Error: {ex}");
        }
    }

    #endregion

    #region Extracted Content Drag Handlers

    private ExtractedTextItem? _draggedExtractedText;
    private ExtractedImageItem? _draggedExtractedImage;
    private bool _isDraggingExtracted;
    private WpfPoint _extractedDragStart;
    private double _extractedOriginalX;
    private double _extractedOriginalY;

    private void ExtractedContent_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border) return;
        if (DataContext is not MainViewModel vm) return;

        _isDraggingExtracted = true;
        _extractedDragStart = e.GetPosition(AnnotationCanvas);

        if (border.Tag is ExtractedTextItem textItem)
        {
            _draggedExtractedText = textItem;
            _draggedExtractedImage = null;
            _extractedOriginalX = textItem.X;
            _extractedOriginalY = textItem.Y;
            vm.SelectedExtractedText = textItem;
        }
        else if (border.Tag is ExtractedImageItem imageItem)
        {
            _draggedExtractedImage = imageItem;
            _draggedExtractedText = null;
            _extractedOriginalX = imageItem.X;
            _extractedOriginalY = imageItem.Y;
            vm.SelectedExtractedImage = imageItem;
        }

        border.CaptureMouse();
        e.Handled = true;
    }

    private void ExtractedContent_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isDraggingExtracted) return;
        if (sender is not Border border) return;
        if (DataContext is not MainViewModel vm) return;

        var currentPos = e.GetPosition(AnnotationCanvas);
        var delta = currentPos - _extractedDragStart;

        // Convert delta from screen to PDF coordinates
        double deltaXPdf = delta.X / vm.ZoomScale;
        double deltaYPdf = -delta.Y / vm.ZoomScale; // Flip Y for PDF coords

        if (_draggedExtractedText != null)
        {
            double newX = _extractedOriginalX + deltaXPdf;
            double newY = _extractedOriginalY + deltaYPdf;

            // Update position on screen
            var (pageWidth, pageHeight) = vm.GetCurrentPageDimensionsInPoints();
            Canvas.SetLeft(border, newX * vm.ZoomScale);
            Canvas.SetTop(border, (pageHeight - newY - _draggedExtractedText.Height) * vm.ZoomScale);
        }
        else if (_draggedExtractedImage != null)
        {
            double newX = _extractedOriginalX + deltaXPdf;
            double newY = _extractedOriginalY + deltaYPdf;

            var (pageWidth, pageHeight) = vm.GetCurrentPageDimensionsInPoints();
            Canvas.SetLeft(border, newX * vm.ZoomScale);
            Canvas.SetTop(border, (pageHeight - newY - _draggedExtractedImage.Height) * vm.ZoomScale);
        }

        e.Handled = true;
    }

    private void ExtractedContent_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingExtracted) return;
        if (sender is not Border border) return;
        if (DataContext is not MainViewModel vm) return;

        var currentPos = e.GetPosition(AnnotationCanvas);
        var delta = currentPos - _extractedDragStart;

        // Only update if actually moved
        if (Math.Abs(delta.X) > 2 || Math.Abs(delta.Y) > 2)
        {
            double deltaXPdf = delta.X / vm.ZoomScale;
            double deltaYPdf = -delta.Y / vm.ZoomScale;

            if (_draggedExtractedText != null)
            {
                double newX = _extractedOriginalX + deltaXPdf;
                double newY = _extractedOriginalY + deltaYPdf;
                vm.UpdateExtractedTextPosition(_draggedExtractedText.ElementId, newX, newY);
                vm.StatusMessage = $"Text moved to ({newX:F0}, {newY:F0})";
            }
            else if (_draggedExtractedImage != null)
            {
                double newX = _extractedOriginalX + deltaXPdf;
                double newY = _extractedOriginalY + deltaYPdf;
                vm.UpdateExtractedImagePosition(_draggedExtractedImage.ElementId, newX, newY);
                vm.StatusMessage = $"Image moved to ({newX:F0}, {newY:F0})";
            }
        }

        _isDraggingExtracted = false;
        _draggedExtractedText = null;
        _draggedExtractedImage = null;
        border.ReleaseMouseCapture();
        e.Handled = true;
    }

    #endregion

    /// <summary>
    /// Refresh header/footer preview on canvas after Apply
    /// </summary>
    private void RefreshHeaderFooterPreviewOnCanvas()
    {
        if (DataContext is not MainViewModel vm) return;

        // First refresh annotations (this clears the canvas)
        RefreshAnnotationPreviews();

        // Get header/footer preview data
        var preview = vm.GetHeaderFooterPreview();
        if (preview == null) return;

        var data = preview;

        // Get the actual rendered image dimensions (in pixels)
        if (PdfImage.Source == null) return;
        
        double imageWidthPx = PdfImage.Source is System.Windows.Media.Imaging.BitmapSource bmp 
            ? bmp.PixelWidth 
            : PdfImage.ActualWidth;
        double imageHeightPx = PdfImage.Source is System.Windows.Media.Imaging.BitmapSource bmpH 
            ? bmpH.PixelHeight 
            : PdfImage.ActualHeight;

        // Get actual PDF page dimensions in points
        var (pageWidthPts, pageHeightPts) = vm.GetCurrentPageDimensionsInPoints();
        if (pageWidthPts <= 0 || pageHeightPts <= 0) return;

        // Calculate actual conversion factor: pixels per point
        // This accounts for any DPI and makes preview match saved PDF exactly
        double pixelsPerPointX = imageWidthPx / pageWidthPts;
        double pixelsPerPointY = imageHeightPx / pageHeightPts;
        
        // Use average for consistent scaling (should be same if aspect ratio is preserved)
        double pixelsPerPoint = (pixelsPerPointX + pixelsPerPointY) / 2.0;
        
        System.Diagnostics.Debug.WriteLine($"Header/Footer Preview: Image={imageWidthPx}x{imageHeightPx}px, Page={pageWidthPts}x{pageHeightPts}pts, Scale={pixelsPerPoint}px/pt");
        
        // Header margin from top (convert points to pixels)
        double headerY = data.HeaderMargin * pixelsPerPoint;
        
        // Footer margin from bottom (convert points to pixels)
        double footerFontSize = Math.Max(data.FooterCenter.FontSize, Math.Max(data.FooterLeft.FontSize, data.FooterRight.FontSize));
        if (footerFontSize < 6) footerFontSize = 10;
        double footerTextHeightPx = footerFontSize * pixelsPerPoint;
        double footerY = imageHeightPx - (data.FooterMargin * pixelsPerPoint) - footerTextHeightPx;

        // Side margins (50 points to match PdfService)
        double sideMargin = 50 * pixelsPerPoint;

        // Add header elements (left margin, center, right margin)
        AddHeaderFooterText(data.HeaderLeft, sideMargin, headerY, System.Windows.HorizontalAlignment.Left, pixelsPerPoint);
        AddHeaderFooterText(data.HeaderCenter, imageWidthPx / 2, headerY, System.Windows.HorizontalAlignment.Center, pixelsPerPoint);
        AddHeaderFooterText(data.HeaderRight, imageWidthPx - sideMargin, headerY, System.Windows.HorizontalAlignment.Right, pixelsPerPoint);

        // Add footer elements
        AddHeaderFooterText(data.FooterLeft, sideMargin, footerY, System.Windows.HorizontalAlignment.Left, pixelsPerPoint);
        AddHeaderFooterText(data.FooterCenter, imageWidthPx / 2, footerY, System.Windows.HorizontalAlignment.Center, pixelsPerPoint);
        AddHeaderFooterText(data.FooterRight, imageWidthPx - sideMargin, footerY, System.Windows.HorizontalAlignment.Right, pixelsPerPoint);

        // Add custom text boxes
        foreach (var customBox in data.CustomTextBoxes)
        {
            AddCustomTextBoxPreview(customBox, imageHeightPx, pixelsPerPoint);
        }
    }

    /// <summary>
    /// Add a custom text box preview to the canvas
    /// </summary>
    private void AddCustomTextBoxPreview(MainViewModel.CustomTextBoxPreview textBox, double imageHeightPx, double pixelsPerPoint)
    {
        if (string.IsNullOrEmpty(textBox.Text) && !textBox.ShowBorder) return;

        // Convert offsets from PDF points to screen pixels using actual conversion factor
        double x = textBox.OffsetX * pixelsPerPoint;
        // Y is from bottom in PDF, from top in WPF
        double boxHeightPx = textBox.BoxHeight * pixelsPerPoint;
        double y = imageHeightPx - (textBox.OffsetY * pixelsPerPoint) - boxHeightPx;

        double boxWidth = textBox.BoxWidth * pixelsPerPoint;
        double boxHeight = boxHeightPx;

        // Parse color
        System.Windows.Media.Color textColor;
        try
        {
            string colorStr = textBox.Color ?? "#000000";
            if (!colorStr.StartsWith("#")) colorStr = "#" + colorStr;
            textColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
        }
        catch
        {
            textColor = System.Windows.Media.Colors.Black;
        }

        // Create container border if needed
        if (textBox.ShowBorder)
        {
            var border = new Border
            {
                Width = boxWidth,
                Height = boxHeight,
                BorderBrush = new SolidColorBrush(textColor),
                BorderThickness = new Thickness(1),
                Background = System.Windows.Media.Brushes.Transparent
            };
            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            AnnotationCanvas.Children.Add(border);
        }

        // Add text if present (supports multiline)
        if (!string.IsNullOrEmpty(textBox.Text))
        {
            // Convert font size from points to pixels using actual conversion factor
            double fontSize = textBox.FontSize * pixelsPerPoint;
            if (fontSize < 6) fontSize = 10 * pixelsPerPoint;
            double padding = 3 * pixelsPerPoint;

            var textBlock = new TextBlock
            {
                Text = textBox.Text,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(textColor),
                FontWeight = textBox.IsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = textBox.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                TextWrapping = TextWrapping.Wrap,
                Width = boxWidth - (padding * 2),
                MaxHeight = boxHeight - (padding * 2)
            };

            // Try to set font family
            try
            {
                if (!string.IsNullOrEmpty(textBox.FontFamily))
                {
                    textBlock.FontFamily = new System.Windows.Media.FontFamily(textBox.FontFamily);
                }
            }
            catch { }

            // Position text inside the box (with small padding)
            Canvas.SetLeft(textBlock, x + padding);
            Canvas.SetTop(textBlock, y + padding);
            AnnotationCanvas.Children.Add(textBlock);
        }
    }

    /// <summary>
    /// Add a header/footer element (text or image) to the canvas with actual styling
    /// </summary>
    private void AddHeaderFooterText(MainViewModel.HeaderFooterPreviewElement element, double x, double y, System.Windows.HorizontalAlignment alignment, double pixelsPerPoint)
    {
        // Handle image element
        if (element.IsImage && !string.IsNullOrEmpty(element.ImagePath) && System.IO.File.Exists(element.ImagePath))
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(element.ImagePath);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                // Scale image dimensions (ImageWidth/Height are in points)
                double imgWidth = element.ImageWidth * pixelsPerPoint;
                double imgHeight = element.ImageHeight * pixelsPerPoint;

                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Width = imgWidth,
                    Height = imgHeight,
                    Stretch = System.Windows.Media.Stretch.Fill
                };

                // Calculate position based on alignment
                double imgLeft = alignment switch
                {
                    System.Windows.HorizontalAlignment.Center => x - (imgWidth / 2),
                    System.Windows.HorizontalAlignment.Right => x - imgWidth,
                    _ => x
                };

                // Center image vertically on the baseline
                double imgTop = y - (imgHeight / 2);

                Canvas.SetLeft(image, imgLeft);
                Canvas.SetTop(image, imgTop);
                AnnotationCanvas.Children.Add(image);
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading header/footer image preview: {ex.Message}");
                // Fall through to try text if image fails
            }
        }

        // Handle text element
        if (string.IsNullOrEmpty(element.Text)) return;

        // Parse color from hex string
        System.Windows.Media.Color textColor;
        try
        {
            string colorStr = element.Color ?? "#000000";
            if (!colorStr.StartsWith("#")) colorStr = "#" + colorStr;
            textColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
        }
        catch
        {
            textColor = System.Windows.Media.Colors.Black;
        }

        // Convert font size from points to pixels using actual conversion factor
        double fontSize = element.FontSize * pixelsPerPoint;
        if (fontSize < 6) fontSize = 10 * pixelsPerPoint;

        var textBlock = new TextBlock
        {
            Text = element.Text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(textColor),
            FontWeight = element.IsBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = element.IsItalic ? FontStyles.Italic : FontStyles.Normal
        };

        // Try to set font family
        try
        {
            if (!string.IsNullOrEmpty(element.FontFamily))
            {
                textBlock.FontFamily = new System.Windows.Media.FontFamily(element.FontFamily);
            }
        }
        catch
        {
            // Use default font if specified font is not available
        }

        // Measure text for alignment
        textBlock.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = textBlock.DesiredSize.Width;

        double left = alignment switch
        {
            System.Windows.HorizontalAlignment.Center => x - (textWidth / 2),
            System.Windows.HorizontalAlignment.Right => x - textWidth,
            _ => x
        };

        Canvas.SetLeft(textBlock, left);
        Canvas.SetTop(textBlock, y);
        AnnotationCanvas.Children.Add(textBlock);
    }

    #endregion

    #region Presentation Mode

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_isPresentationMode)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    ExitPresentationMode();
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.Up:
                case Key.PageUp:
                    PresentationPreviousPage();
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.Down:
                case Key.PageDown:
                case Key.Space:
                    PresentationNextPage();
                    e.Handled = true;
                    break;
                case Key.Home:
                    PresentationGoToPage(0);
                    e.Handled = true;
                    break;
                case Key.End:
                    if (DataContext is MainViewModel vm)
                        PresentationGoToPage(vm.TotalPages - 1);
                    e.Handled = true;
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                EnterPresentationMode();
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                // Toggle View/Edit mode
                if (DataContext is MainViewModel vm)
                {
                    vm.ToggleViewEditModeCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+Z: Undo
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is MainViewModel vmUndo && vmUndo.CanUndo)
                {
                    vmUndo.UndoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+Y: Redo
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is MainViewModel vmRedo && vmRedo.CanRedo)
                {
                    vmRedo.RedoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+V: Paste image from clipboard
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is MainViewModel vmPaste && vmPaste.IsFileLoaded && vmPaste.IsEditMode)
                {
                    PasteImageFromClipboard();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape && DataContext is MainViewModel vmEsc && vmEsc.IsEditMode)
            {
                // Exit edit mode with Escape
                vmEsc.ToggleViewEditModeCommand.Execute(null);
                e.Handled = true;
            }
            // Page navigation with PageUp/PageDown keys (non-presentation mode)
            else if (DataContext is MainViewModel vmNav && vmNav.IsFileLoaded)
            {
                if (e.Key == Key.PageDown)
                {
                    vmNav.NextPageCommand.Execute(null);
                    PdfScrollViewer?.ScrollToVerticalOffset(0);
                    e.Handled = true;
                }
                else if (e.Key == Key.PageUp)
                {
                    vmNav.PreviousPageCommand.Execute(null);
                    PdfScrollViewer?.ScrollToVerticalOffset(0);
                    e.Handled = true;
                }
                else if (e.Key == Key.Home && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    // Ctrl+Home: Go to first page
                    vmNav.CurrentPageIndex = 0;
                    PdfScrollViewer?.ScrollToVerticalOffset(0);
                    e.Handled = true;
                }
                else if (e.Key == Key.End && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    // Ctrl+End: Go to last page
                    vmNav.CurrentPageIndex = vmNav.TotalPages - 1;
                    PdfScrollViewer?.ScrollToVerticalOffset(0);
                    e.Handled = true;
                }
            }
        }
    }

    private void PresentationButton_Click(object sender, RoutedEventArgs e)
    {
        EnterPresentationMode();
    }

    private void EnterPresentationMode()
    {
        if (DataContext is not MainViewModel vm || !vm.IsFileLoaded) return;

        // Save current state
        _previousWindowState = WindowState;
        _previousWindowStyle = WindowStyle;
        _previousResizeMode = ResizeMode;
        _previousTopmost = Topmost;

        // Enter fullscreen - order matters for hiding taskbar!
        // 1. First set to Normal to ensure proper transition
        WindowState = WindowState.Normal;
        // 2. Remove window style
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        // 3. Set Topmost BEFORE maximizing to cover taskbar
        Topmost = true;
        // 4. Now maximize
        WindowState = WindowState.Maximized;

        // Show presentation overlay
        PresentationOverlay.Visibility = Visibility.Visible;
        UpdatePresentationImage();

        _isPresentationMode = true;
    }

    private void ExitPresentationMode()
    {
        // Hide presentation overlay
        PresentationOverlay.Visibility = Visibility.Collapsed;

        // Restore previous state - order matters!
        Topmost = _previousTopmost;
        WindowState = WindowState.Normal; // Reset first
        WindowStyle = _previousWindowStyle;
        ResizeMode = _previousResizeMode;
        WindowState = _previousWindowState;

        _isPresentationMode = false;
    }

    private void UpdatePresentationImage()
    {
        if (DataContext is MainViewModel vm && vm.IsFileLoaded)
        {
            PresentationImage.Source = vm.CurrentPageImage;
            PresentationPageText.Text = $"Page {vm.CurrentPageNumber} / {vm.TotalPages}";
        }
    }

    private void PresentationNextPage()
    {
        if (DataContext is MainViewModel vm)
        {
            vm.NextPageCommand.Execute(null);
            UpdatePresentationImage();
        }
    }

    private void PresentationPreviousPage()
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PreviousPageCommand.Execute(null);
            UpdatePresentationImage();
        }
    }

    private void PresentationGoToPage(int pageIndex)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.CurrentPageIndex = pageIndex;
            UpdatePresentationImage();
        }
    }

    private void PresentationOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Click to go to next page
        PresentationNextPage();
    }

    private void PresentationOverlay_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Right-click shows context menu (handled by XAML)
    }

    private void PresentationPrevPage_Click(object sender, RoutedEventArgs e)
    {
        PresentationPreviousPage();
    }

    private void PresentationNextPage_Click(object sender, RoutedEventArgs e)
    {
        PresentationNextPage();
    }

    private void PresentationExit_Click(object sender, RoutedEventArgs e)
    {
        ExitPresentationMode();
    }

    #endregion

    #region Thumbnail Drag & Drop Reorder

    /// <summary>
    /// Sync selection state when ListBox selection changes (for multi-select)
    /// </summary>
    private void ThumbnailListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SyncSelectedThumbnails(ThumbnailListBox.SelectedItems);
        }
    }

    /// <summary>
    /// Handle keyboard shortcuts for page management
    /// </summary>
    private void ThumbnailListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Ctrl+A: Select all
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ThumbnailListBox.SelectAll();
            e.Handled = true;
        }
        // Delete: Delete selected pages
        else if (e.Key == Key.Delete)
        {
            if (vm.SelectedThumbnails.Count > 1)
            {
                vm.DeleteSelectedPagesCommand.Execute(null);
            }
            else
            {
                vm.DeletePageCommand.Execute(null);
            }
            e.Handled = true;
        }
        // Ctrl+L: Rotate left
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (vm.SelectedThumbnails.Count > 1)
            {
                vm.RotateSelectedPagesLeftCommand.Execute(null);
            }
            else
            {
                vm.RotatePageLeftCommand.Execute(null);
            }
            e.Handled = true;
        }
        // Ctrl+R: Rotate right
        else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (vm.SelectedThumbnails.Count > 1)
            {
                vm.RotateSelectedPagesRightCommand.Execute(null);
            }
            else
            {
                vm.RotatePageRightCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void ThumbnailListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _thumbnailDragStartPoint = e.GetPosition(null);
    }

    private void ThumbnailListBox_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(null);
        var diff = _thumbnailDragStartPoint - position;

        // Check if we've moved enough to start drag
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            // Get the dragged ListBoxItem
            var listBox = sender as WpfListBox;
            var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            
            if (listBoxItem != null && listBox != null)
            {
                var thumbnail = listBoxItem.Content as PageThumbnail;
                if (thumbnail != null)
                {
                    var vm = DataContext as MainViewModel;
                    
                    // Check if we're dragging multiple selected items
                    bool isMultiDrag = vm != null && vm.SelectedThumbnails.Count > 1 && 
                                       vm.SelectedThumbnails.Contains(thumbnail);

                    if (isMultiDrag)
                    {
                        // Mark all selected thumbnails as dragging
                        foreach (var t in vm!.SelectedThumbnails)
                        {
                            t.IsDragging = true;
                        }
                        _isThumbnailDragging = true;

                        // Start the drag operation with multiple items
                        var data = new WpfDataObject("PageThumbnails", vm.SelectedThumbnails.ToList());
                        DragDrop.DoDragDrop(listBoxItem, data, WpfDragDropEffects.Move);

                        // Reset after drag completes
                        foreach (var t in vm.SelectedThumbnails)
                        {
                            t.IsDragging = false;
                        }
                        _isThumbnailDragging = false;
                    }
                    else
                    {
                        // Single item drag
                        _draggedThumbnail = thumbnail;
                        _isThumbnailDragging = true;
                        thumbnail.IsDragging = true;

                        // Start the drag operation
                        var data = new WpfDataObject("PageThumbnail", thumbnail);
                        DragDrop.DoDragDrop(listBoxItem, data, WpfDragDropEffects.Move);

                        // Reset after drag completes
                        thumbnail.IsDragging = false;
                        _isThumbnailDragging = false;
                        _draggedThumbnail = null;
                    }

                    // Clear all drag-over indicators
                    ClearDragOverIndicators();
                }
            }
        }
    }

    private void ThumbnailListBox_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        // Support both single and multi-drag
        if (!e.Data.GetDataPresent("PageThumbnail") && !e.Data.GetDataPresent("PageThumbnails"))
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = WpfDragDropEffects.Move;

        // Find the item under the cursor and highlight it
        var listBox = sender as WpfListBox;
        if (listBox == null) return;

        var position = e.GetPosition(listBox);
        var element = listBox.InputHitTest(position) as DependencyObject;
        var listBoxItem = FindAncestor<ListBoxItem>(element);

        // Clear previous indicators
        ClearDragOverIndicators();

        if (listBoxItem != null)
        {
            var targetThumbnail = listBoxItem.Content as PageThumbnail;
            if (targetThumbnail != null && targetThumbnail != _draggedThumbnail)
            {
                // For multi-drag, check if target is in the dragged set
                if (e.Data.GetDataPresent("PageThumbnails"))
                {
                    var draggedItems = e.Data.GetData("PageThumbnails") as List<PageThumbnail>;
                    if (draggedItems != null && !draggedItems.Contains(targetThumbnail))
                    {
                        targetThumbnail.IsDragOver = true;
                    }
                }
                else
                {
                    targetThumbnail.IsDragOver = true;
                }
            }
        }

        e.Handled = true;
    }

    private void ThumbnailListBox_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        ClearDragOverIndicators();
    }

    private void ThumbnailListBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        var listBox = sender as WpfListBox;
        if (listBox == null) return;

        // Find the drop target
        var position = e.GetPosition(listBox);
        var element = listBox.InputHitTest(position) as DependencyObject;
        var targetItem = FindAncestor<ListBoxItem>(element);
        
        if (targetItem == null)
        {
            ClearDragOverIndicators();
            return;
        }

        var targetThumbnail = targetItem.Content as PageThumbnail;
        if (targetThumbnail == null)
        {
            ClearDragOverIndicators();
            return;
        }

        var vm = DataContext as MainViewModel;
        if (vm == null)
        {
            ClearDragOverIndicators();
            return;
        }

        int toIndex = vm.PageThumbnails.IndexOf(targetThumbnail);
        if (toIndex < 0)
        {
            ClearDragOverIndicators();
            return;
        }

        // Handle multi-item drop
        if (e.Data.GetDataPresent("PageThumbnails"))
        {
            var draggedItems = e.Data.GetData("PageThumbnails") as List<PageThumbnail>;
            if (draggedItems != null && !draggedItems.Contains(targetThumbnail))
            {
                vm.MoveSelectedPagesToIndex(toIndex);
            }
        }
        // Handle single item drop
        else if (e.Data.GetDataPresent("PageThumbnail"))
        {
            var droppedData = e.Data.GetData("PageThumbnail") as PageThumbnail;
            if (droppedData != null && droppedData != targetThumbnail)
            {
                int fromIndex = vm.PageThumbnails.IndexOf(droppedData);
                if (fromIndex >= 0)
                {
                    vm.MovePage(fromIndex, toIndex);
                }
            }
        }

        ClearDragOverIndicators();
        e.Handled = true;
    }

    private void ClearDragOverIndicators()
    {
        if (DataContext is MainViewModel vm)
        {
            foreach (var thumbnail in vm.PageThumbnails)
            {
                thumbnail.IsDragOver = false;
            }
        }
    }

    /// <summary>
    /// Helper to find ancestor of a specific type in the visual tree
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T result)
                return result;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    #endregion

    #region Document Tab Management

    /// <summary>
    /// Handle click on a document tab to switch to it
    /// </summary>
    private void TabItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DocumentTab tab)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SwitchDocumentCommand.Execute(tab);
            }
        }
    }

    #endregion
}
