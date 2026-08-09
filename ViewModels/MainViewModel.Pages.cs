// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenJPDF.Models;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace OpenJPDF.ViewModels;

/// <summary>
/// MainViewModel - Page Management (Multi-Select, Reorder, Rotation)
/// </summary>
public partial class MainViewModel
{
    #region Performance Optimization
    
    /// <summary>
    /// PERFORMANCE FIX: Build index lookup dictionary to avoid O(n²) IndexOf calls
    /// </summary>
    private Dictionary<PageThumbnail, int> BuildThumbnailIndexMap()
    {
        var map = new Dictionary<PageThumbnail, int>(PageThumbnails.Count);
        for (int i = 0; i < PageThumbnails.Count; i++)
        {
            map[PageThumbnails[i]] = i;
        }
        return map;
    }
    
    /// <summary>
    /// PERFORMANCE FIX: Get sorted indices of selected thumbnails using dictionary lookup O(n) instead of O(n²)
    /// </summary>
    private List<int> GetSelectedIndicesSorted(bool descending = false)
    {
        var indexMap = BuildThumbnailIndexMap();
        var indices = SelectedThumbnails
            .Where(t => indexMap.ContainsKey(t))
            .Select(t => indexMap[t])
            .ToList();
        
        if (descending)
            indices.Sort((a, b) => b.CompareTo(a));
        else
            indices.Sort();
        
        return indices;
    }

    private List<PageThumbnail> CapturePageOrder()
    {
        return PageThumbnails.ToList();
    }

    private sealed record SelectedPageMoveData(
        List<PageThumbnail> Items,
        List<int> Indices,
        List<PageImage> PageImages);

    private SelectedPageMoveData GetSelectedPageMoveData()
    {
        var pageImages = ActiveDocument?.PageImages ?? PageImages;
        var indexMap = BuildThumbnailIndexMap();

        var selectedItems = SelectedThumbnails
            .Where(t => indexMap.ContainsKey(t))
            .OrderBy(t => indexMap[t])
            .ToList();

        var selectedIndices = selectedItems
            .Select(t => indexMap[t])
            .ToList();

        var selectedPageImages = selectedIndices
            .Where(idx => idx >= 0 && idx < pageImages.Count)
            .Select(idx => pageImages[idx])
            .ToList();

        return new SelectedPageMoveData(selectedItems, selectedIndices, selectedPageImages);
    }

    private void MoveSelectedPagesBatch(int targetIndex)
    {
        var pageImages = ActiveDocument?.PageImages ?? PageImages;
        var oldOrder = CapturePageOrder();
        var moveData = GetSelectedPageMoveData();

        if (moveData.Items.Count == 0)
            return;

        targetIndex = Math.Clamp(targetIndex, 0, PageThumbnails.Count);
        int adjustedTargetIndex = targetIndex - moveData.Indices.Count(idx => idx < targetIndex);
        adjustedTargetIndex = Math.Clamp(adjustedTargetIndex, 0, PageThumbnails.Count - moveData.Items.Count);

        foreach (var item in moveData.Items)
        {
            PageThumbnails.Remove(item);
        }

        foreach (var idx in moveData.Indices.OrderByDescending(i => i))
        {
            if (idx >= 0 && idx < pageImages.Count)
            {
                pageImages.RemoveAt(idx);
            }
        }

        for (int i = 0; i < moveData.Items.Count; i++)
        {
            PageThumbnails.Insert(adjustedTargetIndex + i, moveData.Items[i]);
        }

        int pageImagesTargetIndex = Math.Min(adjustedTargetIndex, pageImages.Count);
        for (int i = 0; i < moveData.PageImages.Count; i++)
        {
            pageImages.Insert(pageImagesTargetIndex + i, moveData.PageImages[i]);
        }

        UpdatePageNumbers();
        UpdatePageImageNumbers();
        RemapPageBoundStateAfterReorder(oldOrder);
    }

    private void RemapPageBoundStateAfterReorder(List<PageThumbnail> oldOrder)
    {
        if (oldOrder.Count == 0) return;

        var newIndexByThumbnail = BuildThumbnailIndexMap();
        var oldToNewIndex = new Dictionary<int, int>();

        for (int oldIndex = 0; oldIndex < oldOrder.Count; oldIndex++)
        {
            if (newIndexByThumbnail.TryGetValue(oldOrder[oldIndex], out int newIndex))
            {
                oldToNewIndex[oldIndex] = newIndex;
            }
        }

        var annotationsToRemove = new List<AnnotationItem>();
        foreach (var annotation in Annotations)
        {
            if (oldToNewIndex.TryGetValue(annotation.PageNumber, out int newPageNumber))
            {
                annotation.PageNumber = newPageNumber;
            }
            else
            {
                annotationsToRemove.Add(annotation);
            }
        }

        foreach (var annotation in annotationsToRemove)
        {
            Annotations.Remove(annotation);
        }

        if (_pageRotations.Count > 0)
        {
            var remappedRotations = new Dictionary<int, int>();
            foreach (var rotation in _pageRotations)
            {
                if (oldToNewIndex.TryGetValue(rotation.Key, out int newPageNumber))
                {
                    remappedRotations[newPageNumber] = rotation.Value;
                }
            }

            _pageRotations.Clear();
            foreach (var rotation in remappedRotations)
            {
                _pageRotations[rotation.Key] = rotation.Value;
            }
        }
    }
    
    #endregion

    #region Multi-Select Page Properties

    /// <summary>
    /// Collection of selected page thumbnails for batch operations
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PageThumbnail> selectedThumbnails = new();

    /// <summary>
    /// Number of currently selected pages
    /// </summary>
    public int SelectedPagesCount => SelectedThumbnails.Count;

    /// <summary>
    /// Whether multiple pages are selected
    /// </summary>
    public bool HasMultipleSelection => SelectedThumbnails.Count > 1;

    #endregion

    #region Multi-Select Page Methods

    /// <summary>
    /// Sync selected thumbnails from ListBox selection
    /// </summary>
    public void SyncSelectedThumbnails(System.Collections.IList selectedItems)
    {
        SelectedThumbnails.Clear();
        foreach (var item in selectedItems)
        {
            if (item is PageThumbnail thumbnail)
            {
                thumbnail.IsSelected = true;
                SelectedThumbnails.Add(thumbnail);
            }
        }
        
        // Clear IsSelected on unselected items
        foreach (var thumbnail in PageThumbnails)
        {
            if (!SelectedThumbnails.Contains(thumbnail))
            {
                thumbnail.IsSelected = false;
            }
        }
        
        OnPropertyChanged(nameof(SelectedPagesCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
    }

    /// <summary>
    /// Select all pages
    /// </summary>
    [RelayCommand]
    private void SelectAllPages()
    {
        SelectedThumbnails.Clear();
        foreach (var thumbnail in PageThumbnails)
        {
            thumbnail.IsSelected = true;
            SelectedThumbnails.Add(thumbnail);
        }
        OnPropertyChanged(nameof(SelectedPagesCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
        StatusMessage = $"Selected all {PageThumbnails.Count} pages";
    }

    /// <summary>
    /// Clear page selection
    /// </summary>
    [RelayCommand]
    private void ClearPageSelection()
    {
        foreach (var thumbnail in PageThumbnails)
        {
            thumbnail.IsSelected = false;
        }
        SelectedThumbnails.Clear();
        OnPropertyChanged(nameof(SelectedPagesCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
        StatusMessage = "Selection cleared";
    }

    #endregion

    #region Multi-Select Rotation Commands

    /// <summary>
    /// Rotate all selected pages to the right (clockwise 90°)
    /// </summary>
    [RelayCommand]
    private async Task RotateSelectedPagesRight()
    {
        if (SelectedThumbnails.Count == 0)
        {
            StatusMessage = "No pages selected";
            return;
        }

        int count = SelectedThumbnails.Count;
        
        // PERFORMANCE FIX: Use dictionary lookup instead of O(n²) IndexOf
        var indicesToRefresh = GetSelectedIndicesSorted(descending: false);

        foreach (var idx in indicesToRefresh)
        {
            int currentRotation = GetPageRotation(idx);
            int newRotation = (currentRotation + 90) % 360;
            _pageRotations[idx] = newRotation;
        }
        SyncPageRotationsToService();

        await RefreshThumbnailsAsync(indicesToRefresh);

        // Reload current page in Canvas if it was rotated
        if (indicesToRefresh.Contains(CurrentPageIndex))
        {
            CurrentPageRotation = GetPageRotation(CurrentPageIndex);
            LoadCurrentPage();
        }

        HasPageOrderChanged = true;
        StatusMessage = $"{count} page(s) rotated right. Save to apply changes.";
    }

    /// <summary>
    /// Rotate all selected pages to the left (counter-clockwise 90°)
    /// </summary>
    [RelayCommand]
    private async Task RotateSelectedPagesLeft()
    {
        if (SelectedThumbnails.Count == 0)
        {
            StatusMessage = "No pages selected";
            return;
        }

        int count = SelectedThumbnails.Count;
        
        // PERFORMANCE FIX: Use dictionary lookup instead of O(n²) IndexOf
        var indicesToRefresh = GetSelectedIndicesSorted(descending: false);

        foreach (var idx in indicesToRefresh)
        {
            int currentRotation = GetPageRotation(idx);
            int newRotation = (currentRotation + 270) % 360;
            _pageRotations[idx] = newRotation;
        }
        SyncPageRotationsToService();

        await RefreshThumbnailsAsync(indicesToRefresh);

        // Reload current page in Canvas if it was rotated
        if (indicesToRefresh.Contains(CurrentPageIndex))
        {
            CurrentPageRotation = GetPageRotation(CurrentPageIndex);
            LoadCurrentPage();
        }

        HasPageOrderChanged = true;
        StatusMessage = $"{count} page(s) rotated left. Save to apply changes.";
    }

    #endregion

    #region Multi-Select Move Commands

    /// <summary>
    /// Move all selected pages up by one position
    /// </summary>
    [RelayCommand]
    private void MoveSelectedPagesUp()
    {
        if (SelectedThumbnails.Count == 0)
        {
            StatusMessage = "No pages selected";
            return;
        }

        var sortedIndices = GetSelectedIndicesSorted(descending: false);
        if (sortedIndices.Count == 0 || sortedIndices[0] == 0) return;

        MoveSelectedPagesBatch(sortedIndices[0] - 1);
        SyncPageOrderToService();
        LoadCurrentPage();
        HasPageOrderChanged = true;
        StatusMessage = $"{SelectedThumbnails.Count} page(s) moved up. Save to apply changes.";
    }

    /// <summary>
    /// Move all selected pages down by one position
    /// </summary>
    [RelayCommand]
    private void MoveSelectedPagesDown()
    {
        if (SelectedThumbnails.Count == 0)
        {
            StatusMessage = "No pages selected";
            return;
        }

        var sortedIndices = GetSelectedIndicesSorted(descending: false);
        if (sortedIndices.Count == 0 || sortedIndices[^1] == PageThumbnails.Count - 1) return;

        MoveSelectedPagesBatch(sortedIndices[^1] + 2);
        SyncPageOrderToService();
        LoadCurrentPage();
        HasPageOrderChanged = true;
        StatusMessage = $"{SelectedThumbnails.Count} page(s) moved down. Save to apply changes.";
    }

    /// <summary>
    /// Move all selected pages to the beginning
    /// </summary>
    [RelayCommand]
    private void MoveSelectedPagesToFirst()
    {
        if (SelectedThumbnails.Count == 0)
        {
            StatusMessage = "No pages selected";
            return;
        }

        int selectedCount = SelectedThumbnails.Count;
        MoveSelectedPagesBatch(0);
        SyncPageOrderToService();
        
        // Navigate to first selected page
        CurrentPageIndex = 0;
        LoadCurrentPage();
        
        HasPageOrderChanged = true;
        StatusMessage = $"{selectedCount} page(s) moved to beginning. Save to apply changes.";
    }

    /// <summary>
    /// Move all selected pages to the end
    /// </summary>
    [RelayCommand]
    private void MoveSelectedPagesToLast()
    {
        if (SelectedThumbnails.Count == 0)
        {
            StatusMessage = "No pages selected";
            return;
        }

        int selectedCount = SelectedThumbnails.Count;
        MoveSelectedPagesBatch(PageThumbnails.Count);
        SyncPageOrderToService();
        
        // Navigate to first of moved pages
        CurrentPageIndex = PageThumbnails.Count - selectedCount;
        LoadCurrentPage();
        
        HasPageOrderChanged = true;
        StatusMessage = $"{selectedCount} page(s) moved to end. Save to apply changes.";
    }

    #endregion

    #region Multi-Select Delete Command

    /// <summary>
    /// Delete all selected pages
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedPages()
    {
        if (SelectedThumbnails.Count == 0)
        {
            StatusMessage = "No pages selected";
            return;
        }

        if (SelectedThumbnails.Count >= PageThumbnails.Count)
        {
            MessageBox.Show("Cannot delete all pages. At least one page must remain.",
                "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Delete {SelectedThumbnails.Count} selected page(s)?\n\nThis action will be applied when you save the document.",
            "Confirm Delete Pages",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        var pageImages = ActiveDocument?.PageImages ?? PageImages;
        var oldOrder = CapturePageOrder();

        // PERFORMANCE FIX: Use dictionary lookup instead of O(n²) IndexOf
        var sortedIndices = GetSelectedIndicesSorted(descending: true);

        int deletedCount = 0;
        foreach (var idx in sortedIndices)
        {
            pdfService.DeletePage(idx);

            // Remove from PageThumbnails (sidebar)
            if (idx < PageThumbnails.Count)
            {
                PageThumbnails.RemoveAt(idx);
                deletedCount++;
            }
            
            // Remove from PageImages (canvas/continuous scroll)
            if (idx < pageImages.Count)
            {
                pageImages.RemoveAt(idx);
            }

            if (_pageRotations.ContainsKey(idx))
            {
                _pageRotations.Remove(idx);
            }

            var annotationsToRemove = Annotations.Where(a => a.PageNumber == idx).ToList();
            foreach (var ann in annotationsToRemove)
            {
                Annotations.Remove(ann);
            }
        }

        UpdatePageNumbers();
        UpdatePageImageNumbers();
        RemapPageBoundStateAfterReorder(oldOrder);
        SyncPageRotationsToService();
        SelectedThumbnails.Clear();
        OnPropertyChanged(nameof(SelectedPagesCount));
        OnPropertyChanged(nameof(HasMultipleSelection));

        if (CurrentPageIndex >= PageThumbnails.Count)
        {
            CurrentPageIndex = Math.Max(0, PageThumbnails.Count - 1);
        }

        // Force reload current page
        LoadCurrentPage();
        
        ClearAnnotationsRequested?.Invoke();
        RefreshAnnotationsRequested?.Invoke();

        HasPageOrderChanged = true;
        StatusMessage = $"{deletedCount} page(s) deleted. Save to apply changes permanently.";
    }

    #endregion

    #region Page Reorder Methods

    /// <summary>
    /// Move selected pages to a target position (for drag-drop)
    /// </summary>
    public void MoveSelectedPagesToIndex(int targetIndex)
    {
        if (SelectedThumbnails.Count == 0) return;

        int selectedCount = SelectedThumbnails.Count;
        MoveSelectedPagesBatch(targetIndex);
        SyncPageOrderToService();
        LoadCurrentPage();
        HasPageOrderChanged = true;
        StatusMessage = $"{selectedCount} page(s) moved. Save to apply changes.";
    }

    /// <summary>
    /// Move a page from one position to another in the thumbnails list
    /// </summary>
    public void MovePage(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= PageThumbnails.Count ||
            toIndex < 0 || toIndex >= PageThumbnails.Count ||
            fromIndex == toIndex)
            return;

        var pageImages = ActiveDocument?.PageImages ?? PageImages;
        var oldOrder = CapturePageOrder();

        // Move in PageThumbnails (sidebar)
        var item = PageThumbnails[fromIndex];
        PageThumbnails.RemoveAt(fromIndex);
        PageThumbnails.Insert(toIndex, item);
        
        // Move in PageImages (canvas/continuous scroll)
        if (fromIndex < pageImages.Count)
        {
            var pageImage = pageImages[fromIndex];
            pageImages.RemoveAt(fromIndex);
            int targetIndex = Math.Min(toIndex, pageImages.Count);
            pageImages.Insert(targetIndex, pageImage);
        }

        UpdatePageNumbers();
        UpdatePageImageNumbers();
        RemapPageBoundStateAfterReorder(oldOrder);
        SyncPageOrderToService();
        LoadCurrentPage();
        HasPageOrderChanged = true;
        StatusMessage = $"Page moved. Save to apply changes.";
    }

    /// <summary>
    /// Update display page numbers after reorder
    /// </summary>
    private void UpdatePageNumbers()
    {
        for (int i = 0; i < PageThumbnails.Count; i++)
        {
            PageThumbnails[i].PageNumber = i + 1;
        }
        TotalPages = PageThumbnails.Count;
    }
    
    /// <summary>
    /// Update page indices in PageImages collection after reorder
    /// </summary>
    private void UpdatePageImageNumbers()
    {
        var pageImages = ActiveDocument?.PageImages ?? PageImages;
        for (int i = 0; i < pageImages.Count; i++)
        {
            pageImages[i].PageIndex = i;
            pageImages[i].PageNumber = i + 1;
        }
    }

    /// <summary>
    /// Sync current page order to the PDF service for saving
    /// </summary>
    private void SyncPageOrderToService()
    {
        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        var newOrder = PageThumbnails.Select(t => t.OriginalPageIndex).ToArray();
        pdfService.SetPageOrder(newOrder);
        SyncPageRotationsToService();
    }

    private void SyncPageRotationsToService()
    {
        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        pdfService.SetPageRotations(_pageRotations);
        ActiveDocument?.SetPageRotations(_pageRotations);
    }

    /// <summary>
    /// Get the currently selected thumbnail for drag-drop operations
    /// </summary>
    public PageThumbnail? GetThumbnailAtIndex(int index)
    {
        if (index >= 0 && index < PageThumbnails.Count)
            return PageThumbnails[index];
        return null;
    }

    #endregion

    #region Single Page Move Commands

    /// <summary>
    /// Move selected thumbnail up one position
    /// </summary>
    [RelayCommand]
    private void MovePageUp()
    {
        if (SelectedThumbnail == null) return;
        int currentIndex = PageThumbnails.IndexOf(SelectedThumbnail);
        if (currentIndex > 0)
        {
            MovePage(currentIndex, currentIndex - 1);
            CurrentPageIndex = currentIndex - 1;
        }
    }

    /// <summary>
    /// Move selected thumbnail down one position
    /// </summary>
    [RelayCommand]
    private void MovePageDown()
    {
        if (SelectedThumbnail == null) return;
        int currentIndex = PageThumbnails.IndexOf(SelectedThumbnail);
        if (currentIndex < PageThumbnails.Count - 1)
        {
            MovePage(currentIndex, currentIndex + 1);
            CurrentPageIndex = currentIndex + 1;
        }
    }

    /// <summary>
    /// Move selected thumbnail to first position
    /// </summary>
    [RelayCommand]
    private void MovePageToFirst()
    {
        if (SelectedThumbnail == null) return;
        int currentIndex = PageThumbnails.IndexOf(SelectedThumbnail);
        if (currentIndex > 0)
        {
            MovePage(currentIndex, 0);
            CurrentPageIndex = 0;
        }
    }

    /// <summary>
    /// Move selected thumbnail to last position
    /// </summary>
    [RelayCommand]
    private void MovePageToLast()
    {
        if (SelectedThumbnail == null) return;
        int currentIndex = PageThumbnails.IndexOf(SelectedThumbnail);
        if (currentIndex < PageThumbnails.Count - 1)
        {
            MovePage(currentIndex, PageThumbnails.Count - 1);
            CurrentPageIndex = PageThumbnails.Count - 1;
        }
    }

    #endregion

    #region Page Rotation

    /// <summary>
    /// Get the current rotation for a specific page
    /// </summary>
    public int GetPageRotation(int pageIndex)
    {
        return _pageRotations.TryGetValue(pageIndex, out int rotation) ? rotation : 0;
    }

    /// <summary>
    /// Clear all page rotations (called after save)
    /// </summary>
    private void ClearPageRotations()
    {
        _pageRotations.Clear();
        SyncPageRotationsToService();
        CurrentPageRotation = 0;
        PageRotationChanged?.Invoke(0);
        
        foreach (var thumbnail in PageThumbnails)
        {
            thumbnail.UpdateRotation(0);
        }
    }

    [RelayCommand]
    private void RotatePage()
    {
        if (!IsFileLoaded) return;
        RotatePageRight();
    }

    [RelayCommand]
    private void RotatePageRight()
    {
        if (!IsFileLoaded) return;

        int currentRotation = GetPageRotation(CurrentPageIndex);
        int newRotation = (currentRotation + 90) % 360;
        _pageRotations[CurrentPageIndex] = newRotation;
        CurrentPageRotation = newRotation;

        SyncPageRotationsToService();
        LoadCurrentPage();
        RefreshThumbnailRotation(CurrentPageIndex);

        StatusMessage = $"Page rotated to {newRotation}°. Save to apply changes permanently.";
    }

    [RelayCommand]
    private void RotatePageLeft()
    {
        if (!IsFileLoaded) return;

        int currentRotation = GetPageRotation(CurrentPageIndex);
        int newRotation = (currentRotation + 270) % 360;
        _pageRotations[CurrentPageIndex] = newRotation;
        CurrentPageRotation = newRotation;

        SyncPageRotationsToService();
        LoadCurrentPage();
        RefreshThumbnailRotation(CurrentPageIndex);

        StatusMessage = $"Page rotated to {newRotation}°. Save to apply changes permanently.";
    }

    /// <summary>
    /// Re-render thumbnail with rotation applied immediately
    /// </summary>
    private async void RefreshThumbnailRotation(int pageIndex)
    {
        if (pageIndex >= 0 && pageIndex < PageThumbnails.Count)
        {
            var pdfService = ActiveDocument?.PdfService ?? _pdfService;
            int rotation = GetPageRotation(pageIndex);
            int originalPageIndex = PageThumbnails[pageIndex].OriginalPageIndex;
            
            await Task.Run(() =>
            {
                // GetPageThumbnail already renders with rotation applied
                var thumbnail = pdfService.GetPageThumbnail(originalPageIndex, rotation);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (pageIndex < PageThumbnails.Count)
                    {
                        PageThumbnails[pageIndex].UpdateThumbnail(thumbnail);
                        // Don't reset RotationAngle - the bitmap is already rotated
                    }
                });
            });
        }
    }

    /// <summary>
    /// Refresh a single thumbnail image asynchronously
    /// </summary>
    public async Task RefreshThumbnailAsync(int pageIndex)
    {
        if (!IsFileLoaded || pageIndex < 0 || pageIndex >= TotalPages) return;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        int rotation = GetPageRotation(pageIndex);
        int originalPageIndex = PageThumbnails[pageIndex].OriginalPageIndex;

        await Task.Run(() =>
        {
            // GetPageThumbnail already renders with rotation applied
            var thumbnail = pdfService.GetPageThumbnail(originalPageIndex, rotation);
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (pageIndex < PageThumbnails.Count)
                {
                    // Composite H/F elements onto thumbnail if config exists
                    var compositedThumbnail = CompositeHeaderFooterOnThumbnail(thumbnail, pageIndex);
                    PageThumbnails[pageIndex].UpdateThumbnail(compositedThumbnail ?? thumbnail);
                    // Don't reset RotationAngle - the bitmap is already rotated
                }
            });
        });
    }
    
    /// <summary>
    /// Composite Header/Footer elements onto a thumbnail bitmap
    /// </summary>
    private BitmapSource? CompositeHeaderFooterOnThumbnail(BitmapSource? baseThumbnail, int pageIndex)
    {
        if (baseThumbnail == null || HeaderFooterConfig == null || !HasHeaderFooter)
            return baseThumbnail;
        
        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        int originalPageIndex = pageIndex >= 0 && pageIndex < PageThumbnails.Count
            ? PageThumbnails[pageIndex].OriginalPageIndex
            : pageIndex;
        var (pageWidthPts, pageHeightPts) = pdfService.GetPageDimensions(originalPageIndex);
        int rotation = GetPageRotation(pageIndex);
        if (((rotation % 360) + 360) % 360 is 90 or 270)
        {
            (pageWidthPts, pageHeightPts) = (pageHeightPts, pageWidthPts);
        }
        if (pageWidthPts <= 0 || pageHeightPts <= 0) return baseThumbnail;
        
        int currentPage = pageIndex + 1;
        
        // Check if H/F should apply to this page
        if (!HeaderFooterConfig.ShouldApplyToPage(currentPage, TotalPages))
            return baseThumbnail;
        
        try
        {
            // Calculate scale factor (thumbnail size vs page size)
            double scaleX = baseThumbnail.PixelWidth / pageWidthPts;
            double scaleY = baseThumbnail.PixelHeight / pageHeightPts;
            double scale = Math.Min(scaleX, scaleY);
            
            // Create a DrawingVisual to compose thumbnail + H/F elements
            var drawingVisual = new System.Windows.Media.DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                // Draw base thumbnail
                dc.DrawImage(baseThumbnail, new System.Windows.Rect(0, 0, baseThumbnail.PixelWidth, baseThumbnail.PixelHeight));
                
                // Draw CustomImageBoxes
                foreach (var imageBox in HeaderFooterConfig.CustomImageBoxes)
                {
                    if (!imageBox.ShouldApplyToPage(currentPage, TotalPages))
                        continue;
                    
                    if (string.IsNullOrEmpty(imageBox.ImagePath) || !System.IO.File.Exists(imageBox.ImagePath))
                        continue;
                    
                    try
                    {
                        var imgBitmap = new System.Windows.Media.Imaging.BitmapImage();
                        imgBitmap.BeginInit();
                        imgBitmap.UriSource = new Uri(imageBox.ImagePath);
                        imgBitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        imgBitmap.EndInit();
                        imgBitmap.Freeze();
                        
                        // Convert PDF coordinates to thumbnail coordinates
                        double x = imageBox.OffsetX * scale;
                        double y = baseThumbnail.PixelHeight - (imageBox.OffsetY + imageBox.Height) * scale;
                        double width = imageBox.Width * scale;
                        double height = imageBox.Height * scale;
                        
                        // Apply opacity
                        dc.PushOpacity(imageBox.Opacity);
                        
                        // Apply rotation if needed
                        if (Math.Abs(imageBox.Rotation) > 0.001)
                        {
                            var centerX = x + width / 2;
                            var centerY = y + height / 2;
                            dc.PushTransform(new System.Windows.Media.RotateTransform(imageBox.Rotation, centerX, centerY));
                        }
                        
                        dc.DrawImage(imgBitmap, new System.Windows.Rect(x, y, width, height));
                        
                        // Pop rotation transform
                        if (Math.Abs(imageBox.Rotation) > 0.001)
                            dc.Pop();
                        
                        // Pop opacity
                        dc.Pop();
                    }
                    catch { }
                }
                
                // Draw CustomTextBoxes using the same top-left box semantics as the main preview/export path.
                foreach (var textBox in HeaderFooterConfig.CustomTextBoxes)
                {
                    if (!textBox.ShouldApplyToPage(currentPage, TotalPages))
                        continue;
                    
                    if (string.IsNullOrEmpty(textBox.Text))
                        continue;
                    
                    try
                    {
                        double x = textBox.OffsetX * scale;
                        double width = textBox.BoxWidth * scale;
                        double height = textBox.BoxHeight * scale;
                        double y = baseThumbnail.PixelHeight - (textBox.OffsetY * scale) - height;
                        double fontSize = Math.Max(6, textBox.FontSize * scale);
                        double padding = 3 * scale;
                        
                        var formattedText = new System.Windows.Media.FormattedText(
                            textBox.Text,
                            System.Globalization.CultureInfo.CurrentCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            new System.Windows.Media.Typeface(textBox.FontFamily),
                            fontSize,
                            new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(textBox.Color)!),
                            1.0);

                        // Apply rotation if needed
                        if (Math.Abs(textBox.Rotation) > 0.001)
                        {
                            dc.PushTransform(new System.Windows.Media.RotateTransform(textBox.Rotation, x + width / 2, y + height / 2));
                        }

                        if (textBox.ShowBorder)
                        {
                            var pen = new System.Windows.Media.Pen(
                                new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(textBox.Color)!),
                                Math.Max(0.5, 0.5 * scale));
                            dc.DrawRectangle(null, pen, new System.Windows.Rect(x, y, width, height));
                        }

                        dc.PushClip(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(x, y, width, height)));
                        dc.DrawText(formattedText, new System.Windows.Point(x + padding, y + padding));
                        dc.Pop();
                        
                        if (Math.Abs(textBox.Rotation) > 0.001)
                            dc.Pop();
                    }
                    catch { }
                }
            }
            
            // Render to bitmap
            var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                baseThumbnail.PixelWidth, baseThumbnail.PixelHeight,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            renderBitmap.Render(drawingVisual);
            renderBitmap.Freeze();
            
            return renderBitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error compositing H/F on thumbnail: {ex.Message}");
            return baseThumbnail;
        }
    }

    /// <summary>
    /// Refresh multiple thumbnails asynchronously (parallel for performance)
    /// </summary>
    public async Task RefreshThumbnailsAsync(IEnumerable<int> pageIndices)
    {
        var tasks = pageIndices.Select(RefreshThumbnailAsync).ToList();
        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// Refresh all thumbnails to include Header/Footer elements
    /// </summary>
    public async Task RefreshAllThumbnailsWithHeaderFooterAsync()
    {
        if (!IsFileLoaded) return;
        
        var indices = Enumerable.Range(0, TotalPages).ToList();
        await RefreshThumbnailsAsync(indices);
    }

    #endregion
}
