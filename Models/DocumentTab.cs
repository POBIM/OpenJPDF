// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenJPDF.Services;

namespace OpenJPDF.Models;

/// <summary>
/// Represents a single opened PDF document tab.
/// Each tab has its own PDF service, page state, and annotations.
/// </summary>
public partial class DocumentTab : ObservableObject, IDisposable
{
    private bool _disposed;

    #region Identity

    /// <summary>
    /// Unique identifier for this tab
    /// </summary>
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString();

    /// <summary>
    /// Full file path of the PDF
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string filePath = "";

    /// <summary>
    /// File name only (for tab header)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string fileName = "Untitled";

    /// <summary>
    /// Display name for the tab header (includes * if unsaved)
    /// </summary>
    public string DisplayName => HasUnsavedChanges ? $"{FileName} *" : FileName;

    #endregion

    #region State

    /// <summary>
    /// Whether this is the currently active tab
    /// </summary>
    [ObservableProperty]
    private bool isActive;

    /// <summary>
    /// Whether the document has unsaved changes
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private bool hasUnsavedChanges;

    /// <summary>
    /// Whether a PDF file is loaded in this tab
    /// </summary>
    [ObservableProperty]
    private bool isFileLoaded;

    #endregion

    #region Page Data

    /// <summary>
    /// Collection of page thumbnails for the sidebar
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PageThumbnail> pageThumbnails = new();

    /// <summary>
    /// Collection of rendered page images for continuous scroll view
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PageImage> pageImages = new();

    /// <summary>
    /// Currently viewed page index (0-based)
    /// </summary>
    [ObservableProperty]
    private int currentPageIndex;

    /// <summary>
    /// Total number of pages in the document
    /// </summary>
    [ObservableProperty]
    private int totalPages;

    /// <summary>
    /// Currently selected thumbnail
    /// </summary>
    [ObservableProperty]
    private PageThumbnail? selectedThumbnail;

    /// <summary>
    /// Collection of selected thumbnails for multi-select operations
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PageThumbnail> selectedThumbnails = new();

    #endregion

    #region Annotations

    /// <summary>
    /// All annotations for this document
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AnnotationItem> annotations = new();

    /// <summary>
    /// Currently selected annotation
    /// </summary>
    [ObservableProperty]
    private AnnotationItem? selectedAnnotation;

    #endregion

    #region Display Settings

    /// <summary>
    /// Current zoom scale (1.0 = 100%)
    /// </summary>
    [ObservableProperty]
    private double zoomScale = 1.0;

    /// <summary>
    /// Current zoom level as string (e.g., "100%")
    /// </summary>
    [ObservableProperty]
    private string zoomLevel = "100%";

    /// <summary>
    /// Current page image for single page view
    /// </summary>
    [ObservableProperty]
    private BitmapSource? currentPageImage;

    #endregion

    #region Services

    /// <summary>
    /// PDF service instance for this document
    /// Each tab has its own service to handle independent PDF operations
    /// </summary>
    public IPdfService PdfService { get; } = new PdfService();

    /// <summary>
    /// Cancellation token source for background thumbnail loading.
    /// Cancel when switching tabs or closing document.
    /// </summary>
    private CancellationTokenSource? _thumbnailLoadCts;

    /// <summary>
    /// Get or create a cancellation token for background loading operations.
    /// </summary>
    public CancellationToken GetLoadingCancellationToken()
    {
        _thumbnailLoadCts?.Cancel();
        _thumbnailLoadCts = new CancellationTokenSource();
        return _thumbnailLoadCts.Token;
    }

    /// <summary>
    /// Cancel any pending background loading operations.
    /// </summary>
    public void CancelBackgroundLoading()
    {
        _thumbnailLoadCts?.Cancel();
    }

    /// <summary>
    /// Page rotation state for this document (pageIndex -> degrees)
    /// </summary>
    private readonly Dictionary<int, int> _pageRotations = new();

    /// <summary>
    /// Get the current rotation for a specific page
    /// </summary>
    public int GetPageRotation(int pageIndex)
    {
        return _pageRotations.TryGetValue(pageIndex, out int rotation) ? rotation : 0;
    }

    /// <summary>
    /// Set the rotation for a specific page
    /// </summary>
    public void SetPageRotation(int pageIndex, int degrees)
    {
        _pageRotations[pageIndex] = degrees % 360;
    }

    /// <summary>
    /// Clear all page rotations (after save)
    /// </summary>
    public void ClearPageRotations()
    {
        _pageRotations.Clear();
    }

    #endregion

    #region Computed Properties

    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int CurrentPageNumber
    {
        get => CurrentPageIndex + 1;
        set => CurrentPageIndex = value - 1;
    }

    /// <summary>
    /// Number of selected pages
    /// </summary>
    public int SelectedPagesCount => SelectedThumbnails.Count;

    /// <summary>
    /// Whether multiple pages are selected
    /// </summary>
    public bool HasMultipleSelection => SelectedThumbnails.Count > 1;

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            // Cancel any pending background operations
            _thumbnailLoadCts?.Cancel();
            _thumbnailLoadCts?.Dispose();
            
            if (PdfService is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            PageThumbnails.Clear();
            PageImages.Clear();
            Annotations.Clear();
            SelectedThumbnails.Clear();
            _pageRotations.Clear();
            
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    #endregion
}
