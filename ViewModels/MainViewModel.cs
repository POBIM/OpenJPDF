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
    ScreenCapture   // Capture screen area as image annotation
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
        (HeaderFooterConfig.HeaderEnabled || HeaderFooterConfig.FooterEnabled || HeaderFooterConfig.CustomTextBoxes.Count > 0);

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
        float BoxHeight
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
        List<CustomTextBoxPreview> CustomTextBoxes
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

        // Create custom text box previews
        var customTextBoxPreviews = config.CustomTextBoxes.Select(tb => new CustomTextBoxPreview(
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
            BoxHeight: tb.BoxHeight
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
            CustomTextBoxes: customTextBoxPreviews
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
        HeaderFooterConfig = null;
        OnPropertyChanged(nameof(HasHeaderFooter));
        RefreshHeaderFooterPreview?.Invoke();
        StatusMessage = "Header/Footer cleared.";
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
