// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenJPDF.Models;
using OpenJPDF.Services;

namespace OpenJPDF.ViewModels;

/// <summary>
/// MainViewModel - Content Editing (Extract, Move, Delete existing PDF content)
/// </summary>
public partial class MainViewModel
{
    #region Private Fields for Content Editing

    private ContentExtractionService? _contentExtractionService;
    private readonly Dictionary<int, List<ExtractedTextElement>> _extractedTextByPage = new();
    private readonly Dictionary<int, List<ExtractedImageElement>> _extractedImagesByPage = new();

    #endregion

    #region Observable Properties for Content Editing

    [ObservableProperty]
    private ObservableCollection<ExtractedTextItem> extractedTextItems = new();

    [ObservableProperty]
    private ObservableCollection<ExtractedImageItem> extractedImageItems = new();

    [ObservableProperty]
    private bool isContentExtracted;

    [ObservableProperty]
    private ExtractedTextItem? selectedExtractedText;

    [ObservableProperty]
    private ExtractedImageItem? selectedExtractedImage;

    [ObservableProperty]
    private bool isExtractedTextSelected;

    [ObservableProperty]
    private bool isExtractedImageSelected;

    #endregion

    #region Events for Content Editing

    /// <summary>
    /// Event fired when extracted content should be rendered on canvas
    /// </summary>
    public event Action? RefreshExtractedContentRequested;

    #endregion

    #region Commands for Content Editing

    [RelayCommand]
    private async Task ExtractPageContent()
    {
        if (!IsFileLoaded || FilePath == null) return;

        try
        {
            StatusMessage = "Extracting content from page...";

            // Initialize extraction service if needed
            _contentExtractionService ??= new ContentExtractionService(FilePath);

            // Check if already extracted for this page
            if (!_extractedTextByPage.ContainsKey(CurrentPageIndex))
            {
                var textElements = await Task.Run(() =>
                    _contentExtractionService.ExtractTextFromPage(CurrentPageIndex));
                _extractedTextByPage[CurrentPageIndex] = textElements;
            }

            if (!_extractedImagesByPage.ContainsKey(CurrentPageIndex))
            {
                var imageElements = await Task.Run(() =>
                    _contentExtractionService.ExtractImagesFromPage(CurrentPageIndex));
                _extractedImagesByPage[CurrentPageIndex] = imageElements;
            }

            // Update UI collections
            UpdateExtractedContentForCurrentPage();

            // Switch to SelectContent mode
            CurrentEditMode = EditMode.SelectContent;
            IsContentExtracted = true;

            var textCount = ExtractedTextItems.Count;
            var imageCount = ExtractedImageItems.Count;
            StatusMessage = $"Found {textCount} text blocks and {imageCount} images. Click to select.";

            // Notify UI to render extracted content
            RefreshExtractedContentRequested?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error extracting content: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Content extraction error: {ex}");
        }
    }

    [RelayCommand]
    private void ClearExtractedContent()
    {
        _extractedTextByPage.Clear();
        _extractedImagesByPage.Clear();
        ExtractedTextItems.Clear();
        ExtractedImageItems.Clear();
        IsContentExtracted = false;
        SelectedExtractedText = null;
        SelectedExtractedImage = null;

        if (CurrentEditMode == EditMode.SelectContent)
        {
            CurrentEditMode = EditMode.None;
        }

        StatusMessage = "Extracted content cleared";
        RefreshExtractedContentRequested?.Invoke();
    }

    [RelayCommand]
    private void DeleteExtractedText(ExtractedTextItem? item)
    {
        if (item == null) return;

        item.IsDeleted = true;

        // Update the underlying element
        if (_extractedTextByPage.TryGetValue(item.PageNumber, out var elements))
        {
            var element = elements.FirstOrDefault(e => e.Id == item.ElementId);
            if (element != null)
            {
                element.IsDeleted = true;
            }
        }

        StatusMessage = $"Text marked for deletion: {item.DisplayName}";
        RefreshExtractedContentRequested?.Invoke();
    }

    [RelayCommand]
    private void DeleteExtractedImage(ExtractedImageItem? item)
    {
        if (item == null) return;

        item.IsDeleted = true;

        // Update the underlying element
        if (_extractedImagesByPage.TryGetValue(item.PageNumber, out var elements))
        {
            var element = elements.FirstOrDefault(e => e.Id == item.ElementId);
            if (element != null)
            {
                element.IsDeleted = true;
            }
        }

        StatusMessage = $"Image marked for deletion: {item.DisplayName}";
        RefreshExtractedContentRequested?.Invoke();
    }

    [RelayCommand]
    private void RestoreExtractedText(ExtractedTextItem? item)
    {
        if (item == null) return;

        item.IsDeleted = false;
        item.IsModified = false;

        // Restore original position
        if (_extractedTextByPage.TryGetValue(item.PageNumber, out var elements))
        {
            var element = elements.FirstOrDefault(e => e.Id == item.ElementId);
            if (element != null)
            {
                element.IsDeleted = false;
                element.IsModified = false;
                element.X = element.OriginalX;
                element.Y = element.OriginalY;
                element.Text = element.OriginalText;

                item.X = element.X;
                item.Y = element.Y;
                item.Text = element.Text;
            }
        }

        StatusMessage = $"Text restored: {item.DisplayName}";
        RefreshExtractedContentRequested?.Invoke();
    }

    [RelayCommand]
    private void RestoreExtractedImage(ExtractedImageItem? item)
    {
        if (item == null) return;

        item.IsDeleted = false;
        item.IsModified = false;

        // Restore original position and size
        if (_extractedImagesByPage.TryGetValue(item.PageNumber, out var elements))
        {
            var element = elements.FirstOrDefault(e => e.Id == item.ElementId);
            if (element != null)
            {
                element.IsDeleted = false;
                element.IsModified = false;
                element.X = element.OriginalX;
                element.Y = element.OriginalY;
                element.Width = element.OriginalWidth;
                element.Height = element.OriginalHeight;

                item.X = element.X;
                item.Y = element.Y;
                item.Width = element.Width;
                item.Height = element.Height;
            }
        }

        StatusMessage = $"Image restored: {item.DisplayName}";
        RefreshExtractedContentRequested?.Invoke();
    }

    #endregion

    #region Helper Methods for Content Editing

    private void UpdateExtractedContentForCurrentPage()
    {
        ExtractedTextItems.Clear();
        ExtractedImageItems.Clear();

        if (_extractedTextByPage.TryGetValue(CurrentPageIndex, out var textElements))
        {
            foreach (var elem in textElements)
            {
                ExtractedTextItems.Add(ExtractedTextItem.FromElement(elem));
            }
        }

        if (_extractedImagesByPage.TryGetValue(CurrentPageIndex, out var imageElements))
        {
            foreach (var elem in imageElements)
            {
                ExtractedImageItems.Add(ExtractedImageItem.FromElement(elem));
            }
        }
    }

    /// <summary>
    /// Update extracted element position after drag
    /// </summary>
    public void UpdateExtractedTextPosition(Guid elementId, double newX, double newY)
    {
        var item = ExtractedTextItems.FirstOrDefault(t => t.ElementId == elementId);
        if (item == null) return;

        item.X = newX;
        item.Y = newY;
        item.IsModified = true;

        // Update underlying element
        if (_extractedTextByPage.TryGetValue(item.PageNumber, out var elements))
        {
            var element = elements.FirstOrDefault(e => e.Id == elementId);
            if (element != null)
            {
                element.X = (float)newX;
                element.Y = (float)newY;
                element.IsModified = true;
            }
        }
    }

    /// <summary>
    /// Update extracted image position after drag
    /// </summary>
    public void UpdateExtractedImagePosition(Guid elementId, double newX, double newY)
    {
        var item = ExtractedImageItems.FirstOrDefault(i => i.ElementId == elementId);
        if (item == null) return;

        item.X = newX;
        item.Y = newY;
        item.IsModified = true;

        // Update underlying element
        if (_extractedImagesByPage.TryGetValue(item.PageNumber, out var elements))
        {
            var element = elements.FirstOrDefault(e => e.Id == elementId);
            if (element != null)
            {
                element.X = (float)newX;
                element.Y = (float)newY;
                element.IsModified = true;
            }
        }
    }

    /// <summary>
    /// Update extracted image size after resize
    /// </summary>
    public void UpdateExtractedImageSize(Guid elementId, double newWidth, double newHeight)
    {
        var item = ExtractedImageItems.FirstOrDefault(i => i.ElementId == elementId);
        if (item == null) return;

        item.Width = newWidth;
        item.Height = newHeight;
        item.IsModified = true;

        // Update underlying element
        if (_extractedImagesByPage.TryGetValue(item.PageNumber, out var elements))
        {
            var element = elements.FirstOrDefault(e => e.Id == elementId);
            if (element != null)
            {
                element.Width = (float)newWidth;
                element.Height = (float)newHeight;
                element.IsModified = true;
            }
        }
    }

    /// <summary>
    /// Crop extracted image and update bytes/size/position
    /// selectionX/Y/Width/Height are in screen pixels relative to the image top-left.
    /// </summary>
    public bool TryCropExtractedImage(ExtractedImageItem imageItem, double selectionX, double selectionY, double selectionWidth, double selectionHeight)
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

        try
        {
            if (!TryGetImageSize(imageItem.ImageBytes, out var pixelWidth, out var pixelHeight))
            {
                StatusMessage = "Unable to read image data for cropping.";
                return false;
            }

            var cropRect = GetPixelCropRect(normalized, displayWidth, displayHeight, pixelWidth, pixelHeight);

            if (!TryCropImageBytes(imageItem.ImageBytes, cropRect, out var croppedBytes))
            {
                StatusMessage = "Failed to crop extracted image.";
                return false;
            }

            double newWidth = normalized.Width / ZoomScale;
            double newHeight = normalized.Height / ZoomScale;
            double newX = imageItem.X + normalized.X / ZoomScale;
            double newY = imageItem.Y + imageItem.Height - (normalized.Y / ZoomScale) - newHeight;

            imageItem.X = newX;
            imageItem.Y = newY;
            imageItem.Width = newWidth;
            imageItem.Height = newHeight;
            imageItem.ImageBytes = croppedBytes;
            imageItem.Format = "png";
            imageItem.IsModified = true;

            if (_extractedImagesByPage.TryGetValue(imageItem.PageNumber, out var elements))
            {
                var element = elements.FirstOrDefault(e => e.Id == imageItem.ElementId);
                if (element != null)
                {
                    element.X = (float)newX;
                    element.Y = (float)newY;
                    element.Width = (float)newWidth;
                    element.Height = (float)newHeight;
                    element.ImageBytes = croppedBytes;
                    element.Format = "png";
                    element.IsModified = true;
                }
            }

            RefreshExtractedContentRequested?.Invoke();
            StatusMessage = "Extracted image cropped.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Extracted image crop failed: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetImageSize(byte[] imageBytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (imageBytes.Length == 0)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(imageBytes);
            using var bitmap = new System.Drawing.Bitmap(stream);
            width = bitmap.Width;
            height = bitmap.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get all modifications for saving
    /// </summary>
    public (List<ExtractedTextElement> deletedTexts, List<ExtractedTextElement> movedTexts,
            List<ExtractedImageElement> deletedImages, List<ExtractedImageElement> movedImages) GetContentModifications()
    {
        var deletedTexts = new List<ExtractedTextElement>();
        var movedTexts = new List<ExtractedTextElement>();
        var deletedImages = new List<ExtractedImageElement>();
        var movedImages = new List<ExtractedImageElement>();

        foreach (var pageTexts in _extractedTextByPage.Values)
        {
            foreach (var text in pageTexts)
            {
                if (text.IsDeleted)
                    deletedTexts.Add(text);
                else if (text.IsModified)
                    movedTexts.Add(text);
            }
        }

        foreach (var pageImages in _extractedImagesByPage.Values)
        {
            foreach (var image in pageImages)
            {
                if (image.IsDeleted)
                    deletedImages.Add(image);
                else if (image.IsModified)
                    movedImages.Add(image);
            }
        }

        return (deletedTexts, movedTexts, deletedImages, movedImages);
    }

    /// <summary>
    /// Called when page changes - update extracted content display
    /// This is called from MainViewModel.OnCurrentPageIndexChanged
    /// </summary>
    public void OnPageChangedUpdateExtractedContent()
    {
        if (IsContentExtracted)
        {
            UpdateExtractedContentForCurrentPage();
            RefreshExtractedContentRequested?.Invoke();
        }
    }

    /// <summary>
    /// Reset extraction service when file changes
    /// </summary>
    private void ResetContentExtractionOnFileChange()
    {
        _contentExtractionService = null;
        _extractedTextByPage.Clear();
        _extractedImagesByPage.Clear();
        ExtractedTextItems.Clear();
        ExtractedImageItems.Clear();
        IsContentExtracted = false;
        SelectedExtractedText = null;
        SelectedExtractedImage = null;
    }

    #endregion

    #region Property Change Handlers

    partial void OnSelectedExtractedTextChanged(ExtractedTextItem? value)
    {
        IsExtractedTextSelected = value != null;
        if (value != null)
        {
            SelectedExtractedImage = null;
        }
    }

    partial void OnSelectedExtractedImageChanged(ExtractedImageItem? value)
    {
        IsExtractedImageSelected = value != null;
        if (value != null)
        {
            SelectedExtractedText = null;
        }
    }

    #endregion

    #region Save Integration

    /// <summary>
    /// Apply content modifications (redactions and moved content) to PdfService before save
    /// </summary>
    public void ApplyContentModificationsToService(IPdfService pdfService)
    {
        if (!IsContentExtracted) return;

        var (deletedTexts, movedTexts, deletedImages, movedImages) = GetContentModifications();

        // Add redactions for deleted text
        foreach (var text in deletedTexts)
        {
            // Add redaction at original position
            pdfService.AddRedaction(text.PageNumber, text.OriginalX, text.OriginalY, text.Width, text.Height);
        }

        // Add redactions for moved text (redact original position) and add new text
        foreach (var text in movedTexts)
        {
            // Redact original position
            pdfService.AddRedaction(text.PageNumber, text.OriginalX, text.OriginalY, text.Width, text.Height);
            // Add text at new position
            pdfService.AddMovedText(text);
        }

        // Add redactions for deleted images
        foreach (var image in deletedImages)
        {
            pdfService.AddRedaction(image.PageNumber, image.OriginalX, image.OriginalY, image.OriginalWidth, image.OriginalHeight);
        }

        // Add redactions for moved images and add new images
        foreach (var image in movedImages)
        {
            // Redact original position
            pdfService.AddRedaction(image.PageNumber, image.OriginalX, image.OriginalY, image.OriginalWidth, image.OriginalHeight);
            // Add image at new position
            pdfService.AddMovedImage(image);
        }

        System.Diagnostics.Debug.WriteLine($"Content modifications applied: {deletedTexts.Count} deleted texts, {movedTexts.Count} moved texts, {deletedImages.Count} deleted images, {movedImages.Count} moved images");
    }

    #endregion
}
