// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenJPDF.Models;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace OpenJPDF.ViewModels;

/// <summary>
/// MainViewModel - Multi-Document Tab Management
/// </summary>
public partial class MainViewModel
{
    #region Multi-Document Properties

    /// <summary>
    /// Collection of all open document tabs
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DocumentTab> openDocuments = new();

    /// <summary>
    /// Currently active document tab
    /// </summary>
    [ObservableProperty]
    private DocumentTab? activeDocument;

    /// <summary>
    /// Whether there are multiple documents open
    /// </summary>
    public bool HasMultipleDocuments => OpenDocuments.Count > 1;

    /// <summary>
    /// Whether there is at least one document open
    /// </summary>
    public bool HasOpenDocuments => OpenDocuments.Count > 0;

    #endregion

    #region Document Tab Changed Handler

    partial void OnActiveDocumentChanged(DocumentTab? oldValue, DocumentTab? newValue)
    {
        // Update old document state
        if (oldValue != null)
        {
            oldValue.IsActive = false;
        }

        // Update new document state
        if (newValue != null)
        {
            newValue.IsActive = true;
            SyncFromActiveDocument();
        }
        else
        {
            // No active document - reset state
            IsFileLoaded = false;
            FilePath = null;
            TotalPages = 0;
            CurrentPageIndex = 0;
            PageThumbnails.Clear();
            PageImages.Clear();
            Annotations.Clear();
            CurrentPageImage = null;
        }

        // Notify property changes
        OnPropertyChanged(nameof(HasMultipleDocuments));
        OnPropertyChanged(nameof(HasOpenDocuments));
        OnPropertyChanged(nameof(WindowTitle));

        // Refresh canvas
        ClearAnnotationsRequested?.Invoke();
        RefreshAnnotationsRequested?.Invoke();
    }

    #endregion

    #region Document Sync Methods

    /// <summary>
    /// Sync properties from active document to ViewModel
    /// </summary>
    private void SyncFromActiveDocument()
    {
        if (ActiveDocument == null) return;

        _isSyncingDocument = true;
        try
        {
            // 1. Sync collections FIRST (before CurrentPageIndex triggers LoadCurrentPage)
            PageThumbnails = ActiveDocument.PageThumbnails;
            PageImages = ActiveDocument.PageImages;
            Annotations = ActiveDocument.Annotations;
            SelectedThumbnails = ActiveDocument.SelectedThumbnails;
            SelectedThumbnail = ActiveDocument.SelectedThumbnail;
            SelectedAnnotation = ActiveDocument.SelectedAnnotation;

            // 2. Sync zoom BEFORE page index (LoadCurrentPage uses ZoomScale)
            ZoomScale = ActiveDocument.ZoomScale;
            ZoomLevel = ActiveDocument.ZoomLevel;

            // 3. Sync document state
            IsFileLoaded = ActiveDocument.IsFileLoaded;
            FilePath = ActiveDocument.FilePath;
            TotalPages = ActiveDocument.TotalPages;

            // 4. Sync current page image directly (avoid re-rendering)
            CurrentPageImage = ActiveDocument.CurrentPageImage;

            // 5. Set CurrentPageIndex LAST (triggers OnCurrentPageIndexChanged but skips LoadCurrentPage)
            CurrentPageIndex = ActiveDocument.CurrentPageIndex;
        }
        finally
        {
            _isSyncingDocument = false;
        }

        OnPropertyChanged(nameof(PageThumbnails));
        OnPropertyChanged(nameof(PageImages));
        OnPropertyChanged(nameof(Annotations));
        OnPropertyChanged(nameof(SelectedThumbnails));
        OnPropertyChanged(nameof(SelectedPagesCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
    }

    /// <summary>
    /// Sync properties to active document (for saving state)
    /// </summary>
    private void SyncToActiveDocument()
    {
        if (ActiveDocument == null) return;

        ActiveDocument.CurrentPageIndex = CurrentPageIndex;
        ActiveDocument.ZoomScale = ZoomScale;
        ActiveDocument.ZoomLevel = ZoomLevel;
        ActiveDocument.CurrentPageImage = CurrentPageImage;
        ActiveDocument.SelectedThumbnail = SelectedThumbnail;
        ActiveDocument.SelectedAnnotation = SelectedAnnotation;
    }

    #endregion

    #region Document Tab Commands

    /// <summary>
    /// Switch to a specific document tab
    /// </summary>
    [RelayCommand]
    private void SwitchDocument(DocumentTab? tab)
    {
        if (tab == null || tab == ActiveDocument) return;

        // Save current state before switching
        SyncToActiveDocument();

        // Switch to new tab
        ActiveDocument = tab;
    }

    /// <summary>
    /// Close a specific document tab
    /// </summary>
    [RelayCommand]
    private async Task CloseDocument(DocumentTab? tab)
    {
        if (tab == null) return;

        // Check for unsaved changes
        if (tab.HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                $"'{tab.FileName}' has unsaved changes.\n\nDo you want to save before closing?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
                return;

            if (result == MessageBoxResult.Yes)
            {
                // Switch to this tab and save
                ActiveDocument = tab;
                await Save();
            }
        }

        // Remove from collection
        int index = OpenDocuments.IndexOf(tab);
        OpenDocuments.Remove(tab);
        tab.Dispose();

        // Switch to adjacent tab if this was the active one
        if (tab == ActiveDocument)
        {
            if (OpenDocuments.Count > 0)
            {
                int newIndex = Math.Min(index, OpenDocuments.Count - 1);
                ActiveDocument = OpenDocuments[newIndex];
            }
            else
            {
                ActiveDocument = null;
            }
        }

        OnPropertyChanged(nameof(HasMultipleDocuments));
        OnPropertyChanged(nameof(HasOpenDocuments));
        StatusMessage = $"Closed '{tab.FileName}'";
    }

    /// <summary>
    /// Close all document tabs
    /// </summary>
    [RelayCommand]
    private async Task CloseAllDocuments()
    {
        foreach (var tab in OpenDocuments.ToList())
        {
            await CloseDocument(tab);
        }
    }

    #endregion
}
