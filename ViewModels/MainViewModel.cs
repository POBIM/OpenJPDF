// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenJPDF.Models;
using OpenJPDF.Services;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace OpenJPDF.ViewModels;

/// <summary>
/// Edit mode options for the PDF editor
/// </summary>
public enum EditMode
{
    None,       // View mode - continuous scroll
    Select,     // Edit mode - can select/move/delete annotations
    AddText,
    AddImage,
    AddRectangle,
    AddEllipse,
    AddLine,
    SelectText,     // OCR selection mode
    SelectContent,  // Select extracted content from original PDF
    ScreenCapture,  // Capture screen area as image annotation
    EditHeaderFooter, // Edit Header/Footer mode - drag CustomTextBox positions
    Calibrate,      // Calibrate measurement scale by drawing reference line
    MeasureDistance, // Measure distance between two points
    MeasureArea,    // Measure area by drawing polygon
    SelectMeasurement // Select and edit existing measurements
}

/// <summary>
/// Information about a side length for display in sidebar
/// </summary>
public class SideLengthInfo
{
    public string Name { get; set; } = "";
    public string Length { get; set; } = "";
}

/// <summary>
/// Main ViewModel for OpenJPDF application.
/// Split into partial classes for maintainability:
/// - MainViewModel.cs (this file) - Core properties, fields, events, constructor
/// - MainViewModel.Documents.cs - Multi-document tab management
/// - MainViewModel.Pages.cs - Page navigation, thumbnails, rotation, reorder
/// - MainViewModel.FileOperations.cs - Open, Save, Load, Navigation, Zoom
/// - MainViewModel.Annotations.cs - Text, Image, Shape annotations, OCR
/// - MainViewModel.PdfTools.cs - Merge, Split, Extract, Delete, Duplicate
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    #region Private Fields

    private readonly IPdfService _pdfService;
    private readonly ScannerService _scannerService = new();
    private readonly UndoRedoManager _undoRedoManager = new();
    private readonly BackgroundRemovalService _backgroundRemovalService = new();
    private bool _disposed;
    
    /// <summary>
    /// Expose BackgroundRemovalService for advanced removal dialog
    /// </summary>
    public BackgroundRemovalService BackgroundRemovalService => _backgroundRemovalService;
    private string? _pendingImagePath;
    
    /// <summary>
    /// Page rotation state for real-time visual rotation (pageIndex -> degrees)
    /// </summary>
    private readonly Dictionary<int, int> _pageRotations = new();

    /// <summary>
    /// Flag to prevent re-loading page during document sync
    /// </summary>
    private bool _isSyncingDocument;

    #endregion

    #region Events

    /// <summary>
    /// Event fired when annotation previews should be cleared
    /// </summary>
    public event Action? ClearAnnotationsRequested;

    /// <summary>
    /// Event fired when annotations need to be refreshed on canvas
    /// </summary>
    public event Action? RefreshAnnotationsRequested;

    /// <summary>
    /// Event fired when page rotation changes
    /// </summary>
    public event Action<int>? PageRotationChanged;

    /// <summary>
    /// Event fired when header/footer needs to be refreshed on canvas
    /// </summary>
    public event Action? RefreshHeaderFooterPreview;
    
    /// <summary>
    /// Event fired when an image is selected for placement (preview follows mouse)
    /// </summary>
    public event Action<string>? ImageSelectedForPlacement;

    #endregion

    #region Observable Properties - Document State

    [ObservableProperty]
    private bool isFileLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? filePath;

    /// <summary>
    /// Window title showing app name and current file
    /// </summary>
    public string WindowTitle => string.IsNullOrEmpty(FilePath) 
        ? "OpenJPDF - PDF Editor" 
        : $"OpenJPDF - {Path.GetFileName(FilePath)}";

    [ObservableProperty]
    private int currentPageNumber = 1;

    [ObservableProperty]
    private int currentPageIndex;

    [ObservableProperty]
    private int totalPages;

    [ObservableProperty]
    private BitmapSource? currentPageImage;

    [ObservableProperty]
    private string statusMessage = "Ready";

    #endregion

    #region Observable Properties - Zoom

    [ObservableProperty]
    private string zoomLevel = "100%";

    /// <summary>
    /// Available zoom levels for the ComboBox
    /// </summary>
    public string[] ZoomLevels { get; } = ["50%", "75%", "100%", "125%", "150%", "200%"];

    [ObservableProperty]
    private double zoomScale = 1.0;

    #endregion

    #region Observable Properties - Page Collections

    [ObservableProperty]
    private ObservableCollection<PageThumbnail> pageThumbnails = new();

    [ObservableProperty]
    private ObservableCollection<PageImage> pageImages = new();

    [ObservableProperty]
    private PageThumbnail? selectedThumbnail;

    [ObservableProperty]
    private bool hasPageOrderChanged;

    #endregion

    #region Observable Properties - Edit Mode

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMeasuring))]
    [NotifyPropertyChangedFor(nameof(ShowMeasurementPanel))]
    private EditMode currentEditMode = EditMode.None;

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private string editModeMessage = "";

    [ObservableProperty]
    private Cursor editCursor = Cursors.Arrow;

    [ObservableProperty]
    private int currentPageRotation;

    #endregion

    #region Observable Properties - Annotations

    [ObservableProperty]
    private ObservableCollection<AnnotationItem> annotations = new();

    [ObservableProperty]
    private AnnotationItem? selectedAnnotation;

    [ObservableProperty]
    private bool hasSelectedAnnotation;

    [ObservableProperty]
    private bool isTextSelected;

    [ObservableProperty]
    private bool isImageSelected;

    [ObservableProperty]
    private TextAnnotationItem? selectedTextAnnotation;

    [ObservableProperty]
    private ImageAnnotationItem? selectedImageAnnotation;

    [ObservableProperty]
    private ShapeAnnotationItem? selectedShapeAnnotation;

    [ObservableProperty]
    private bool isShapeSelected;

    #endregion

    #region Observable Properties - Undo/Redo

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool canUndo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    private bool canRedo;

    [ObservableProperty]
    private string? undoDescription;

    [ObservableProperty]
    private string? redoDescription;

    #endregion

    #region Observable Properties - Header/Footer

    [ObservableProperty]
    private HeaderFooterConfig? headerFooterConfig;
    
    /// <summary>
    /// Whether header/footer is configured
    /// </summary>
    public bool HasHeaderFooter => HeaderFooterConfig != null && 
        (HeaderFooterConfig.HeaderEnabled || HeaderFooterConfig.FooterEnabled || 
         HeaderFooterConfig.CustomTextBoxes.Count > 0 || HeaderFooterConfig.CustomImageBoxes.Count > 0);

    /// <summary>
    /// Whether we're in Edit Header/Footer mode (CustomTextBox shown as temporary annotations)
    /// </summary>
    public bool IsEditingHeaderFooter => CurrentEditMode == EditMode.EditHeaderFooter;

    #endregion

    #region Observable Properties - Measurement Tool

    [ObservableProperty]
    private MeasurementCalibration measurementCalibration = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMeasurementPanel))]
    private ObservableCollection<MeasurementAnnotation> measurements = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMeasurementSelected))]
    [NotifyPropertyChangedFor(nameof(IsLineMeasurementSelected))]
    [NotifyPropertyChangedFor(nameof(IsAreaMeasurementSelected))]
    [NotifyPropertyChangedFor(nameof(SelectedMeasurementDistance))]
    [NotifyPropertyChangedFor(nameof(SelectedMeasurementArea))]
    [NotifyPropertyChangedFor(nameof(SelectedMeasurementPerimeter))]
    [NotifyPropertyChangedFor(nameof(SelectedAreaSideLengths))]
    private MeasurementAnnotation? selectedMeasurement;

    /// <summary>
    /// Selected line measurement (for sidebar property panel)
    /// </summary>
    [ObservableProperty]
    private LineMeasurement? selectedLineMeasurement;

    /// <summary>
    /// Selected area measurement (for sidebar property panel)
    /// </summary>
    [ObservableProperty]
    private AreaMeasurement? selectedAreaMeasurement;

    /// <summary>
    /// Whether any measurement is selected
    /// </summary>
    public bool IsMeasurementSelected => SelectedMeasurement != null;

    /// <summary>
    /// Whether a line measurement is selected
    /// </summary>
    public bool IsLineMeasurementSelected => SelectedMeasurement is LineMeasurement;

    /// <summary>
    /// Whether an area measurement is selected
    /// </summary>
    public bool IsAreaMeasurementSelected => SelectedMeasurement is AreaMeasurement;

    /// <summary>
    /// Get formatted distance of selected measurement
    /// </summary>
    public string SelectedMeasurementDistance => SelectedMeasurement != null 
        ? MeasurementCalibration.FormatDistance(SelectedMeasurement.GetPixelLength())
        : "";

    /// <summary>
    /// Get formatted area of selected measurement (for area measurements)
    /// </summary>
    public string SelectedMeasurementArea => SelectedMeasurement is AreaMeasurement area
        ? MeasurementCalibration.FormatArea(area.GetPixelArea())
        : "";

    /// <summary>
    /// Get formatted perimeter of selected area measurement
    /// </summary>
    public string SelectedMeasurementPerimeter => SelectedMeasurement is AreaMeasurement area
        ? MeasurementCalibration.FormatDistance(area.GetPixelLength())
        : "";

    /// <summary>
    /// Get list of side lengths for selected area measurement (for sidebar display)
    /// </summary>
    public List<SideLengthInfo> SelectedAreaSideLengths
    {
        get
        {
            if (SelectedMeasurement is not AreaMeasurement area || area.Points.Count < 3)
                return new List<SideLengthInfo>();

            var sideLengths = new List<SideLengthInfo>();
            for (int i = 0; i < area.Points.Count; i++)
            {
                var p1 = area.Points[i];
                var p2 = area.Points[(i + 1) % area.Points.Count];
                double length = p1.DistanceTo(p2);
                sideLengths.Add(new SideLengthInfo
                {
                    Name = $"Side {i + 1}:",
                    Length = MeasurementCalibration.FormatDistance(length)
                });
            }
            return sideLengths;
        }
    }

    /// <summary>
    /// Whether measurement tool is calibrated and ready to use
    /// </summary>
    public bool IsCalibrated => MeasurementCalibration.IsCalibrated;

    /// <summary>
    /// Whether we're in any measurement mode
    /// </summary>
    public bool IsMeasuring => CurrentEditMode == EditMode.Calibrate || 
                                CurrentEditMode == EditMode.MeasureDistance || 
                                CurrentEditMode == EditMode.MeasureArea ||
                                CurrentEditMode == EditMode.SelectMeasurement;

    /// <summary>
    /// Whether to show the measurement sidebar (when in measurement mode OR has measurements)
    /// </summary>
    public bool ShowMeasurementPanel => IsMeasuring || Measurements.Count > 0 || IsCalibrated;

    /// <summary>
    /// Event fired when measurements need to be refreshed on canvas
    /// </summary>
    public event Action? RefreshMeasurementsRequested;

    /// <summary>
    /// Event fired when calibration dialog should be shown
    /// </summary>
    public event Action<double>? ShowCalibrationDialogRequested;

    /// <summary>
    /// Handle selected measurement changed - update typed properties
    /// </summary>
    partial void OnSelectedMeasurementChanged(MeasurementAnnotation? value)
    {
        SelectedLineMeasurement = value as LineMeasurement;
        SelectedAreaMeasurement = value as AreaMeasurement;
    }

    #endregion

    #region Static Data - Font/Color Options

    public float[] FontSizes { get; } = { 
        6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f, 17f, 18f, 20f, 22f, 24f, 26f, 28f, 
        30f, 32f, 36f, 40f, 44f, 48f, 54f, 60f, 66f, 72f, 80f, 88f, 96f, 108f, 120f, 144f 
    };

    public string[] TextColors { get; } = { "#000000", "#FFFFFF", "#FF0000", "#0000FF", "#008000", "#FF6600", "#800080", "#00CED1", "#FFD700", "#808080" };

    public string[] BackgroundColors { get; } = { "Transparent", "#FFFFFF", "#FFFF00", "#90EE90", "#ADD8E6", "#FFB6C1", "#E6E6FA", "#F0E68C", "#D3D3D3" };

    public string[] FontFamilies { get; } = { 
        // Thai Fonts
        "TH Sarabun New", "TH SarabunPSK", "Angsana New", "Cordia New", "Browallia New",
        "Leelawadee UI", "Leelawadee", "Norasi", "Garuda",
        // Sans-serif
        "Arial", "Tahoma", "Verdana", "Calibri", "Segoe UI", "Helvetica", "Trebuchet MS",
        // Serif
        "Times New Roman", "Georgia", "Cambria", "Garamond", "Palatino Linotype",
        // Monospace
        "Courier New", "Consolas", "Lucida Console",
        // Display
        "Impact", "Comic Sans MS"
    };

    public float[] StrokeWidths { get; } = { 0.5f, 1f, 2f, 3f, 4f, 5f };

    public ShapeType[] ShapeTypes { get; } = { ShapeType.Rectangle, ShapeType.Ellipse, ShapeType.Line };

    public TextAlignment[] TextAlignments { get; } = { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right };

    #endregion

    #region Constructor

    public MainViewModel()
    {
        _pdfService = new PdfService();
        
        // Subscribe to undo/redo state changes
        _undoRedoManager.StateChanged += OnUndoRedoStateChanged;
    }

    #endregion

    #region Undo/Redo Commands

    private void OnUndoRedoStateChanged()
    {
        CanUndo = _undoRedoManager.CanUndo;
        CanRedo = _undoRedoManager.CanRedo;
        UndoDescription = _undoRedoManager.UndoDescription;
        RedoDescription = _undoRedoManager.RedoDescription;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _undoRedoManager.Undo();
        RefreshAnnotationsRequested?.Invoke();
        StatusMessage = UndoDescription != null ? $"Undone: {UndoDescription}" : "Undo";
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _undoRedoManager.Redo();
        RefreshAnnotationsRequested?.Invoke();
        StatusMessage = RedoDescription != null ? $"Redone: {RedoDescription}" : "Redo";
    }

    /// <summary>
    /// Record an undoable action (for actions performed externally like drag)
    /// </summary>
    public void RecordUndoableAction(IUndoableAction action)
    {
        _undoRedoManager.RecordAction(action);
    }

    /// <summary>
    /// Clear undo/redo history (e.g., when loading a new document)
    /// </summary>
    public void ClearUndoHistory()
    {
        _undoRedoManager.Clear();
    }

    #endregion

    #region Property Changed Handlers

    partial void OnSelectedAnnotationChanged(AnnotationItem? value)
    {
        foreach (var ann in Annotations)
        {
            ann.IsSelected = false;
        }

        HasSelectedAnnotation = value != null;
        IsTextSelected = value is TextAnnotationItem;
        IsImageSelected = value is ImageAnnotationItem;
        IsShapeSelected = value is ShapeAnnotationItem;

        if (value != null)
        {
            value.IsSelected = true;
        }

        SelectedTextAnnotation = value as TextAnnotationItem;
        SelectedImageAnnotation = value as ImageAnnotationItem;
        SelectedShapeAnnotation = value as ShapeAnnotationItem;
    }

    partial void OnCurrentEditModeChanged(EditMode value)
    {
        IsEditMode = value != EditMode.None;
        OnPropertyChanged(nameof(IsViewMode));
        OnPropertyChanged(nameof(ViewEditModeText));
        OnPropertyChanged(nameof(ViewEditModeIcon));
        
        EditModeMessage = value switch
        {
            EditMode.Select => "Edit Mode - Click elements to select, drag to move",
            EditMode.AddText => "Click on the PDF to place text",
            EditMode.AddImage => "Click on the PDF to place image",
            EditMode.AddRectangle => "Click on the PDF to place rectangle",
            EditMode.AddEllipse => "Click on the PDF to place ellipse",
            EditMode.AddLine => "Click on the PDF to place line start point",
            EditMode.SelectText => "Drag to select text region for OCR",
            _ => ""
        };

        EditCursor = value switch
        {
            EditMode.AddText or EditMode.AddImage or EditMode.AddRectangle 
                or EditMode.AddEllipse or EditMode.AddLine or EditMode.SelectText => Cursors.Cross,
            _ => Cursors.Arrow
        };
    }

    partial void OnCurrentPageIndexChanged(int value)
    {
        if (IsFileLoaded && value >= 0 && value < TotalPages)
        {
            CurrentPageNumber = value + 1;
            ClearAnnotationsRequested?.Invoke();
            
            // Skip re-loading if we're syncing from ActiveDocument (image already set)
            if (!_isSyncingDocument)
            {
                LoadCurrentPage();
            }
            
            RefreshAnnotationsRequested?.Invoke();
            
            // Refresh header/footer preview for new page (fixes issue where header doesn't render until zoom)
            RefreshHeaderFooterPreview?.Invoke();

            // Update extracted content for new page
            OnPageChangedUpdateExtractedContent();

            CurrentPageRotation = GetPageRotation(value);
            PageRotationChanged?.Invoke(CurrentPageRotation);
        }
    }

    partial void OnCurrentPageNumberChanged(int value)
    {
        if (IsFileLoaded && value >= 1 && value <= TotalPages)
        {
            CurrentPageIndex = value - 1;
        }
    }

    #endregion

    #region View/Edit Mode

    public bool IsViewMode => CurrentEditMode == EditMode.None;
    public string ViewEditModeText => IsViewMode ? "View" : "Edit";
    public string ViewEditModeIcon => IsViewMode ? "👁️" : "✏️";

    [RelayCommand]
    private void ToggleViewEditMode()
    {
        if (!IsFileLoaded) return;

        if (CurrentEditMode == EditMode.None)
        {
            CurrentEditMode = EditMode.Select;
            StatusMessage = "Edit Mode - Select, move, or add elements";
        }
        else
        {
            CurrentEditMode = EditMode.None;
            StatusMessage = "View Mode";
        }
    }

    #endregion

    #region Header/Footer

    /// <summary>
    /// Preview element data for header/footer rendering
    /// </summary>
    public record HeaderFooterPreviewElement(
        string? Text,
        string? ImagePath,
        bool IsImage,
        float FontSize,
        string FontFamily,
        string Color,
        bool IsBold,
        bool IsItalic,
        double ImageWidth,
        double ImageHeight
    );

    /// <summary>
    /// Preview data for custom text box
    /// </summary>
    public record CustomTextBoxPreview(
        string? Text,
        string Label,
        float OffsetX,
        float OffsetY,
        float FontSize,
        string FontFamily,
        string Color,
        bool IsBold,
        bool IsItalic,
        bool ShowBorder,
        float BoxWidth,
        float BoxHeight,
        double Rotation
    );
    
    /// <summary>
    /// Preview data for custom image box
    /// </summary>
    public record CustomImageBoxPreview(
        string Label,
        string? ImagePath,
        float OffsetX,
        float OffsetY,
        float Width,
        float Height,
        double Rotation,
        float Opacity
    );

    /// <summary>
    /// Full preview data structure for header/footer
    /// </summary>
    public record HeaderFooterPreviewData(
        HeaderFooterPreviewElement HeaderLeft,
        HeaderFooterPreviewElement HeaderCenter,
        HeaderFooterPreviewElement HeaderRight,
        HeaderFooterPreviewElement FooterLeft,
        HeaderFooterPreviewElement FooterCenter,
        HeaderFooterPreviewElement FooterRight,
        float HeaderMargin,
        float FooterMargin,
        List<CustomTextBoxPreview> CustomTextBoxes,
        List<CustomImageBoxPreview> CustomImageBoxes
    );

    /// <summary>
    /// Get header/footer preview data for current page with full styling info
    /// </summary>
    public HeaderFooterPreviewData? GetHeaderFooterPreview()
    {
        if (HeaderFooterConfig == null || !HasHeaderFooter)
            return null;

        var config = HeaderFooterConfig;
        string fileName = FilePath != null ? Path.GetFileName(FilePath) : "document.pdf";
        DateTime now = DateTime.Now;
        int currentPage = CurrentPageIndex + 1;

        HeaderFooterPreviewElement CreateElement(HeaderFooterElement element, bool isEnabled)
        {
            string? text = null;
            if (isEnabled && !element.IsImage && !string.IsNullOrEmpty(element.Text))
            {
                text = element.GetFormattedText(currentPage, TotalPages, fileName, now);
            }
            return new HeaderFooterPreviewElement(
                Text: text,
                ImagePath: isEnabled && element.IsImage ? element.ImagePath : null,
                IsImage: element.IsImage,
                FontSize: element.FontSize,
                FontFamily: element.FontFamily,
                Color: element.Color,
                IsBold: element.IsBold,
                IsItalic: element.IsItalic,
                ImageWidth: element.ImageWidth,
                ImageHeight: element.ImageHeight
            );
        }

        // Create custom text box previews (filter by page scope)
        var customTextBoxPreviews = config.CustomTextBoxes
            .Where(tb => tb.ShouldApplyToPage(currentPage, TotalPages))
            .Select(tb => new CustomTextBoxPreview(
                Text: tb.GetFormattedText(currentPage, TotalPages, fileName, now),
                Label: tb.Label,
                OffsetX: tb.OffsetX,
                OffsetY: tb.OffsetY,
                FontSize: tb.FontSize,
                FontFamily: tb.FontFamily,
                Color: tb.Color,
                IsBold: tb.IsBold,
                IsItalic: tb.IsItalic,
                ShowBorder: tb.ShowBorder,
                BoxWidth: tb.BoxWidth,
                BoxHeight: tb.BoxHeight,
                Rotation: tb.Rotation
            )).ToList();
        
        // Create custom image box previews (filter by page scope)
        var customImageBoxPreviews = config.CustomImageBoxes
            .Where(ib => ib.ShouldApplyToPage(currentPage, TotalPages))
            .Select(ib => new CustomImageBoxPreview(
                Label: ib.Label,
                ImagePath: ib.ImagePath,
                OffsetX: ib.OffsetX,
                OffsetY: ib.OffsetY,
                Width: ib.Width,
                Height: ib.Height,
                Rotation: ib.Rotation,
                Opacity: ib.Opacity
            )).ToList();

        return new HeaderFooterPreviewData(
            HeaderLeft: CreateElement(config.HeaderLeft, config.HeaderEnabled),
            HeaderCenter: CreateElement(config.HeaderCenter, config.HeaderEnabled),
            HeaderRight: CreateElement(config.HeaderRight, config.HeaderEnabled),
            FooterLeft: CreateElement(config.FooterLeft, config.FooterEnabled),
            FooterCenter: CreateElement(config.FooterCenter, config.FooterEnabled),
            FooterRight: CreateElement(config.FooterRight, config.FooterEnabled),
            HeaderMargin: config.HeaderMargin,
            FooterMargin: config.FooterMargin,
            CustomTextBoxes: customTextBoxPreviews,
            CustomImageBoxes: customImageBoxPreviews
        );
    }

    [RelayCommand]
    private void OpenHeaderFooterDialog()
    {
        if (!IsFileLoaded)
        {
            System.Windows.MessageBox.Show("Please open a PDF file first.", "No Document", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = HeaderFooterConfig != null
                ? new Views.HeaderFooterDialog(HeaderFooterConfig, TotalPages)
                : new Views.HeaderFooterDialog(TotalPages);

            if (dialog.ShowDialog() == true && dialog.Config != null)
            {
                HeaderFooterConfig = dialog.Config;
                OnPropertyChanged(nameof(HasHeaderFooter));
                RefreshHeaderFooterPreview?.Invoke();
                
                // Refresh all thumbnails to show H/F elements
                _ = RefreshAllThumbnailsWithHeaderFooterAsync();
                
                StatusMessage = HasHeaderFooter 
                    ? "Header/Footer configured. Preview shown on canvas."
                    : "Header/Footer cleared.";
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error opening Header/Footer dialog: {ex.Message}", "Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"Header/Footer dialog error: {ex}");
        }
    }

    [RelayCommand]
    private void ClearHeaderFooter()
    {
        // Exit edit mode first if active
        if (CurrentEditMode == EditMode.EditHeaderFooter)
        {
            _tempHeaderFooterAnnotations.Clear();
            _tempHeaderFooterImageAnnotations.Clear();
            CurrentEditMode = EditMode.None;
        }
        
        HeaderFooterConfig = null;
        OnPropertyChanged(nameof(HasHeaderFooter));
        OnPropertyChanged(nameof(IsEditingHeaderFooter));
        RefreshHeaderFooterPreview?.Invoke();
        RefreshAnnotationsRequested?.Invoke();
        
        // Refresh all thumbnails to remove H/F elements
        _ = RefreshAllThumbnailsWithHeaderFooterAsync();
        
        StatusMessage = "Header/Footer cleared.";
    }

    // Track temporary annotations created during Edit H/F mode
    private readonly List<TextAnnotationItem> _tempHeaderFooterAnnotations = new();
    private readonly List<ImageAnnotationItem> _tempHeaderFooterImageAnnotations = new();

    [RelayCommand]
    private void ToggleEditHeaderFooter()
    {
        if (!IsFileLoaded)
        {
            StatusMessage = "Please open a PDF file first.";
            return;
        }

        if (!HasHeaderFooter)
        {
            StatusMessage = "No Header/Footer configured. Add text via 'Header/Footer' dialog or right-click annotation.";
            return;
        }

        if (CurrentEditMode == EditMode.EditHeaderFooter)
        {
            // Exit Edit Header/Footer mode - save changes back to CustomTextBox
            ExitEditHeaderFooterMode();
        }
        else
        {
            // Enter Edit Header/Footer mode - convert CustomTextBox to annotations
            EnterEditHeaderFooterMode();
        }

        OnPropertyChanged(nameof(IsEditingHeaderFooter));
    }

    /// <summary>
    /// Enter Edit Header/Footer mode - convert CustomTextBox and CustomImageBox to temporary annotations
    /// </summary>
    private void EnterEditHeaderFooterMode()
    {
        if (HeaderFooterConfig == null) return;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        var (pageWidthPts, pageHeightPts) = pdfService.GetPageDimensions(CurrentPageIndex);

        // Get scale factor: annotation uses ZoomScale, CustomTextBox uses actual PDF points
        // annotation.X/Y = screenPixels / ZoomScale (NOT real PDF points!)
        // PDF points = screenPixels / pixelsPerPoint
        // 
        // Relationship:
        // screenPixels = PDF_X * pixelsPerPoint
        // annotation.X = screenPixels / ZoomScale = PDF_X * pixelsPerPoint / ZoomScale
        // 
        // So to convert PDF points to annotation coordinates:
        // annotation.X = PDF_X * (pixelsPerPoint / ZoomScale)
        // annotation.Y = (pageHeightPts - PDF_OffsetY) * (pixelsPerPoint / ZoomScale)

        // Calculate pixelsPerPoint from rendered image
        double pixelsPerPoint = GetPixelsPerPoint();
        double scaleFactor = pixelsPerPoint / ZoomScale;

        // Convert each CustomTextBox to a temporary TextAnnotation
        foreach (var textBox in HeaderFooterConfig.CustomTextBoxes)
        {
            // Convert PDF coordinates to annotation coordinates
            double annotationX = textBox.OffsetX * scaleFactor;
            double annotationY = (pageHeightPts - textBox.OffsetY) * scaleFactor;

            var annotation = new TextAnnotationItem
            {
                PageNumber = CurrentPageIndex,
                X = annotationX,
                Y = annotationY,
                Text = textBox.Text,
                FontFamily = textBox.FontFamily,
                FontSize = textBox.FontSize,
                Color = textBox.Color,
                IsBold = textBox.IsBold,
                IsItalic = textBox.IsItalic,
                BackgroundColor = "#C8E6C9", // Light green to indicate H/F edit mode
                BorderColor = "#4CAF50",
                BorderWidth = 2
            };

            _tempHeaderFooterAnnotations.Add(annotation);
            Annotations.Add(annotation);
        }
        
        // Convert each CustomImageBox to a temporary ImageAnnotation
        foreach (var imageBox in HeaderFooterConfig.CustomImageBoxes)
        {
            // Convert PDF coordinates to annotation coordinates
            // ImageBox.OffsetY is from bottom, annotation Y is from top
            double annotationX = imageBox.OffsetX * scaleFactor;
            double annotationY = (pageHeightPts - imageBox.OffsetY - imageBox.Height) * scaleFactor;
            double annotationWidth = imageBox.Width * scaleFactor;
            double annotationHeight = imageBox.Height * scaleFactor;

            var annotation = new ImageAnnotationItem
            {
                PageNumber = CurrentPageIndex,
                X = annotationX,
                Y = annotationY,
                Width = annotationWidth,
                Height = annotationHeight,
                ImagePath = imageBox.ImagePath,
                Rotation = imageBox.Rotation,
                // Note: Opacity will be handled in the view layer
            };

            _tempHeaderFooterImageAnnotations.Add(annotation);
            Annotations.Add(annotation);
        }

        CurrentEditMode = EditMode.EditHeaderFooter;
        
        // Hide normal H/F preview, show as annotations
        RefreshHeaderFooterPreview?.Invoke();
        RefreshAnnotationsRequested?.Invoke();
        
        StatusMessage = "Edit Header/Footer: Drag to move text/images. Click 'Edit H/F' again to save.";
    }

    /// <summary>
    /// Exit Edit Header/Footer mode - save annotation positions back to CustomTextBox and CustomImageBox
    /// </summary>
    private void ExitEditHeaderFooterMode()
    {
        if (HeaderFooterConfig == null) return;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        var (pageWidthPts, pageHeightPts) = pdfService.GetPageDimensions(CurrentPageIndex);

        // Get scale factor for conversion
        double pixelsPerPoint = GetPixelsPerPoint();
        double scaleFactor = pixelsPerPoint / ZoomScale;

        // Update CustomTextBox from annotations
        for (int i = 0; i < _tempHeaderFooterAnnotations.Count && i < HeaderFooterConfig.CustomTextBoxes.Count; i++)
        {
            var annotation = _tempHeaderFooterAnnotations[i];
            var textBox = HeaderFooterConfig.CustomTextBoxes[i];

            // Convert annotation coordinates back to PDF coordinates
            // PDF_X = annotation.X / scaleFactor
            // PDF_OffsetY = pageHeightPts - (annotation.Y / scaleFactor)
            float pdfX = (float)(annotation.X / scaleFactor);
            float pdfY = (float)(pageHeightPts - (annotation.Y / scaleFactor));

            // Update position and text
            textBox.OffsetX = pdfX;
            textBox.OffsetY = pdfY;
            textBox.Text = annotation.Text;
            textBox.FontFamily = annotation.FontFamily;
            textBox.FontSize = annotation.FontSize;
            textBox.Color = annotation.Color;
            textBox.IsBold = annotation.IsBold;
            textBox.IsItalic = annotation.IsItalic;
        }
        
        // Update CustomImageBox from annotations
        for (int i = 0; i < _tempHeaderFooterImageAnnotations.Count && i < HeaderFooterConfig.CustomImageBoxes.Count; i++)
        {
            var annotation = _tempHeaderFooterImageAnnotations[i];
            var imageBox = HeaderFooterConfig.CustomImageBoxes[i];

            // Convert annotation coordinates back to PDF coordinates
            // annotation.Y is from top, ImageBox.OffsetY is from bottom
            float pdfX = (float)(annotation.X / scaleFactor);
            float pdfWidth = (float)(annotation.Width / scaleFactor);
            float pdfHeight = (float)(annotation.Height / scaleFactor);
            float pdfY = (float)(pageHeightPts - (annotation.Y / scaleFactor) - pdfHeight);

            // Update position, size, rotation and image path (for Remove Background)
            imageBox.OffsetX = pdfX;
            imageBox.OffsetY = pdfY;
            imageBox.Width = pdfWidth;
            imageBox.Height = pdfHeight;
            imageBox.Rotation = annotation.Rotation;
            imageBox.ImagePath = annotation.ImagePath; // Update ImagePath (changed by Remove Background)
        }

        // Remove temporary text annotations
        foreach (var annotation in _tempHeaderFooterAnnotations)
        {
            Annotations.Remove(annotation);
        }
        _tempHeaderFooterAnnotations.Clear();
        
        // Remove temporary image annotations
        foreach (var annotation in _tempHeaderFooterImageAnnotations)
        {
            Annotations.Remove(annotation);
        }
        _tempHeaderFooterImageAnnotations.Clear();

        CurrentEditMode = EditMode.None;
        
        // Refresh to show normal H/F preview
        ClearAnnotationsRequested?.Invoke();
        RefreshAnnotationsRequested?.Invoke();
        RefreshHeaderFooterPreview?.Invoke();
        
        // Refresh all thumbnails to show updated H/F elements
        _ = RefreshAllThumbnailsWithHeaderFooterAsync();
        
        StatusMessage = "Header/Footer updated. Save to apply changes.";
    }

    /// <summary>
    /// Get pixels per point for current rendered image (requested via event)
    /// </summary>
    public Func<double>? GetPixelsPerPointFunc { get; set; }
    
    private double GetPixelsPerPoint()
    {
        return GetPixelsPerPointFunc?.Invoke() ?? ZoomScale;
    }

    /// <summary>
    /// Get the current page dimensions in PDF points
    /// </summary>
    public (float Width, float Height) GetCurrentPageDimensionsInPoints()
    {
        if (!IsFileLoaded) return (0, 0);
        
        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        return pdfService.GetPageDimensions(CurrentPageIndex);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            // Unsubscribe from events to prevent memory leaks
            _undoRedoManager.StateChanged -= OnUndoRedoStateChanged;
            
            if (_pdfService is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _scannerService.Dispose();
            _backgroundRemovalService.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    #endregion
}
