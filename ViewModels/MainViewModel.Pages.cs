// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
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

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        int count = SelectedThumbnails.Count;
        var indicesToRefresh = new List<int>();

        foreach (var thumbnail in SelectedThumbnails.ToList())
        {
            int idx = PageThumbnails.IndexOf(thumbnail);
            if (idx < 0) continue;

            int currentRotation = GetPageRotation(idx);
            int newRotation = (currentRotation + 90) % 360;
            _pageRotations[idx] = newRotation;
            pdfService.RotatePage(idx, 90);
            indicesToRefresh.Add(idx);
        }

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

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        int count = SelectedThumbnails.Count;
        var indicesToRefresh = new List<int>();

        foreach (var thumbnail in SelectedThumbnails.ToList())
        {
            int idx = PageThumbnails.IndexOf(thumbnail);
            if (idx < 0) continue;

            int currentRotation = GetPageRotation(idx);
            int newRotation = (currentRotation + 270) % 360;
            _pageRotations[idx] = newRotation;
            pdfService.RotatePage(idx, -90);
            indicesToRefresh.Add(idx);
        }

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

        var sortedIndices = SelectedThumbnails
            .Select(t => PageThumbnails.IndexOf(t))
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();

        if (sortedIndices.Count == 0 || sortedIndices[0] == 0) return;

        foreach (var idx in sortedIndices)
        {
            int targetIdx = idx - 1;
            if (targetIdx >= 0 && !sortedIndices.Contains(targetIdx))
            {
                MovePageInternal(idx, targetIdx);
            }
        }

        SyncPageOrderToService();
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

        var sortedIndices = SelectedThumbnails
            .Select(t => PageThumbnails.IndexOf(t))
            .Where(i => i >= 0)
            .OrderByDescending(i => i)
            .ToList();

        if (sortedIndices.Count == 0 || sortedIndices[0] == PageThumbnails.Count - 1) return;

        foreach (var idx in sortedIndices)
        {
            int targetIdx = idx + 1;
            if (targetIdx < PageThumbnails.Count && !sortedIndices.Contains(targetIdx))
            {
                MovePageInternal(idx, targetIdx);
            }
        }

        SyncPageOrderToService();
        HasPageOrderChanged = true;
        StatusMessage = $"{SelectedThumbnails.Count} page(s) moved down. Save to apply changes.";
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

        var sortedIndices = SelectedThumbnails
            .Select(t => PageThumbnails.IndexOf(t))
            .Where(i => i >= 0)
            .OrderByDescending(i => i)
            .ToList();

        int deletedCount = 0;
        foreach (var idx in sortedIndices)
        {
            pdfService.DeletePage(idx);

            if (idx < PageThumbnails.Count)
            {
                PageThumbnails.RemoveAt(idx);
                deletedCount++;
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
        SelectedThumbnails.Clear();
        OnPropertyChanged(nameof(SelectedPagesCount));
        OnPropertyChanged(nameof(HasMultipleSelection));

        if (CurrentPageIndex >= PageThumbnails.Count)
        {
            CurrentPageIndex = Math.Max(0, PageThumbnails.Count - 1);
        }

        ClearAnnotationsRequested?.Invoke();
        RefreshAnnotationsRequested?.Invoke();

        HasPageOrderChanged = true;
        StatusMessage = $"{deletedCount} page(s) deleted. Save to apply changes permanently.";
    }

    #endregion

    #region Page Reorder Methods

    /// <summary>
    /// Move page without firing events (internal use for batch operations)
    /// </summary>
    private void MovePageInternal(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= PageThumbnails.Count ||
            toIndex < 0 || toIndex >= PageThumbnails.Count ||
            fromIndex == toIndex)
            return;

        var item = PageThumbnails[fromIndex];
        PageThumbnails.RemoveAt(fromIndex);
        PageThumbnails.Insert(toIndex, item);
        UpdatePageNumbers();
    }

    /// <summary>
    /// Move selected pages to a target position (for drag-drop)
    /// </summary>
    public void MoveSelectedPagesToIndex(int targetIndex)
    {
        if (SelectedThumbnails.Count == 0) return;

        var selectedItems = SelectedThumbnails
            .OrderBy(t => PageThumbnails.IndexOf(t))
            .ToList();

        foreach (var item in selectedItems)
        {
            PageThumbnails.Remove(item);
        }

        targetIndex = Math.Min(targetIndex, PageThumbnails.Count);

        for (int i = 0; i < selectedItems.Count; i++)
        {
            PageThumbnails.Insert(targetIndex + i, selectedItems[i]);
        }

        UpdatePageNumbers();
        SyncPageOrderToService();
        HasPageOrderChanged = true;
        StatusMessage = $"{selectedItems.Count} page(s) moved. Save to apply changes.";
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

        var item = PageThumbnails[fromIndex];
        PageThumbnails.RemoveAt(fromIndex);
        PageThumbnails.Insert(toIndex, item);

        UpdatePageNumbers();
        SyncPageOrderToService();
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
    /// Sync current page order to the PDF service for saving
    /// </summary>
    private void SyncPageOrderToService()
    {
        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        var newOrder = PageThumbnails.Select(t => t.OriginalPageIndex).ToArray();
        pdfService.SetPageOrder(newOrder);
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

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;

        int currentRotation = GetPageRotation(CurrentPageIndex);
        int newRotation = (currentRotation + 90) % 360;
        _pageRotations[CurrentPageIndex] = newRotation;
        CurrentPageRotation = newRotation;

        pdfService.RotatePage(CurrentPageIndex, 90);
        LoadCurrentPage();
        RefreshThumbnailRotation(CurrentPageIndex);

        StatusMessage = $"Page rotated to {newRotation}°. Save to apply changes permanently.";
    }

    [RelayCommand]
    private void RotatePageLeft()
    {
        if (!IsFileLoaded) return;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;

        int currentRotation = GetPageRotation(CurrentPageIndex);
        int newRotation = (currentRotation + 270) % 360;
        _pageRotations[CurrentPageIndex] = newRotation;
        CurrentPageRotation = newRotation;

        pdfService.RotatePage(CurrentPageIndex, -90);
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
            
            await Task.Run(() =>
            {
                // GetPageThumbnail already renders with rotation applied
                var thumbnail = pdfService.GetPageThumbnail(pageIndex, rotation);
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

        await Task.Run(() =>
        {
            // GetPageThumbnail already renders with rotation applied
            var thumbnail = pdfService.GetPageThumbnail(pageIndex, rotation);
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

    /// <summary>
    /// Refresh multiple thumbnails asynchronously (parallel for performance)
    /// </summary>
    public async Task RefreshThumbnailsAsync(IEnumerable<int> pageIndices)
    {
        var tasks = pageIndices.Select(RefreshThumbnailAsync).ToList();
        await Task.WhenAll(tasks);
    }

    #endregion
}
