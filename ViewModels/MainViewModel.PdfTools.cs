// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using CommunityToolkit.Mvvm.Input;
using OpenJPDF.Models;
using WinForms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace OpenJPDF.ViewModels;

/// <summary>
/// MainViewModel - PDF Tools (Merge, Split, Extract, Delete, Duplicate)
/// </summary>
public partial class MainViewModel
{
    #region PDF Merge/Split/Extract

    [RelayCommand]
    private async Task MergePdf()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf",
            Title = "Select PDF Files to Merge",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 1)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save Merged PDF",
                FileName = "merged.pdf"
            };

            if (saveDialog.ShowDialog() == true)
            {
                StatusMessage = "Merging PDFs...";
                bool success = await _pdfService.MergePdfsAsync(dialog.FileNames, saveDialog.FileName);

                if (success)
                {
                    StatusMessage = "PDFs merged successfully";

                    // Open the merged file automatically
                    await OpenFileFromPathAsync(saveDialog.FileName);

                    MessageBox.Show($"PDFs merged successfully!\n\nSaved to: {saveDialog.FileName}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "Merge failed";
                    MessageBox.Show("Failed to merge PDF files.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        else if (dialog.FileNames.Length == 1)
        {
            MessageBox.Show("Please select at least 2 PDF files to merge.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task SplitPdf()
    {
        if (!IsFileLoaded) return;

        var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select folder to save split pages"
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            var pdfService = ActiveDocument?.PdfService ?? _pdfService;
            
            StatusMessage = "Splitting PDF...";
            bool success = await pdfService.SplitPdfAsync(FilePath!, dialog.SelectedPath);

            if (success)
            {
                StatusMessage = "PDF split successfully";
                MessageBox.Show($"PDF split successfully!\n\nPages saved to: {dialog.SelectedPath}", 
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = "Split failed";
                MessageBox.Show("Failed to split PDF file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task ExtractPages()
    {
        if (!IsFileLoaded) return;

        // Use new dialog with thumbnails and pre-selected pages from sidebar
        var preSelected = SelectedThumbnails.Count > 0 ? SelectedThumbnails : null;
        var dialog = new Views.ExtractPagesDialog(PageThumbnails, preSelected);

        if (dialog.ShowDialog() == true && dialog.SelectedPages.Length > 0)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save Extracted Pages",
                FileName = "extracted.pdf"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var pdfService = ActiveDocument?.PdfService ?? _pdfService;

                // Convert 0-based indices to 1-based page numbers
                var pageNumbers = dialog.SelectedPages.Select(idx => idx + 1).ToArray();

                StatusMessage = "Extracting pages...";
                bool success = await pdfService.ExtractPagesAsync(FilePath!, pageNumbers, saveDialog.FileName);

                if (success)
                {
                    StatusMessage = "Pages extracted successfully";

                    // Open the extracted file automatically
                    await OpenFileFromPathAsync(saveDialog.FileName);

                    MessageBox.Show($"Pages extracted successfully!\n\nSaved to: {saveDialog.FileName}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "Extraction failed";
                    MessageBox.Show("Failed to extract pages.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    #endregion

    #region Delete/Duplicate Page

    [RelayCommand]
    private void DeletePage()
    {
        if (!IsFileLoaded || TotalPages <= 1) return;

        var result = MessageBox.Show(
            $"Delete page {CurrentPageNumber}?\n\nThis action will be applied when you save the document.",
            "Confirm Delete Page",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            var pdfService = ActiveDocument?.PdfService ?? _pdfService;
            int deletedIndex = CurrentPageIndex;
            
            pdfService.DeletePage(deletedIndex);
            
            if (deletedIndex < PageThumbnails.Count)
            {
                PageThumbnails.RemoveAt(deletedIndex);
            }
            
            UpdatePageNumbers();
            
            if (_pageRotations.ContainsKey(deletedIndex))
            {
                _pageRotations.Remove(deletedIndex);
            }
            
            var keysToUpdate = _pageRotations.Keys.Where(k => k > deletedIndex).OrderByDescending(k => k).ToList();
            foreach (var key in keysToUpdate)
            {
                int rotation = _pageRotations[key];
                _pageRotations.Remove(key);
                _pageRotations[key - 1] = rotation;
            }
            
            var annotationsToRemove = Annotations.Where(a => a.PageNumber == deletedIndex).ToList();
            foreach (var ann in annotationsToRemove)
            {
                Annotations.Remove(ann);
            }
            foreach (var ann in Annotations.Where(a => a.PageNumber > deletedIndex))
            {
                ann.PageNumber--;
            }
            
            if (deletedIndex >= PageThumbnails.Count)
            {
                CurrentPageIndex = Math.Max(0, PageThumbnails.Count - 1);
            }
            else
            {
                int tempIndex = CurrentPageIndex;
                CurrentPageIndex = -1;
                CurrentPageIndex = tempIndex;
            }
            
            ClearAnnotationsRequested?.Invoke();
            RefreshAnnotationsRequested?.Invoke();
            
            HasPageOrderChanged = true;
            StatusMessage = $"Page {deletedIndex + 1} deleted. Save to apply changes permanently.";
        }
    }

    [RelayCommand]
    private void DuplicatePage()
    {
        if (!IsFileLoaded) return;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        int sourceIndex = CurrentPageIndex;
        
        pdfService.DuplicatePage(sourceIndex);
        
        var sourceThumbnail = PageThumbnails[sourceIndex];
        var newThumbnail = new PageThumbnail
        {
            PageNumber = sourceIndex + 2,
            OriginalPageIndex = sourceIndex,
            Thumbnail = sourceThumbnail.Thumbnail
        };
        
        if (_pageRotations.TryGetValue(sourceIndex, out int rotation))
        {
            newThumbnail.UpdateRotation(rotation);
        }
        
        PageThumbnails.Insert(sourceIndex + 1, newThumbnail);
        
        UpdatePageNumbers();
        
        var keysToUpdate = _pageRotations.Keys.Where(k => k > sourceIndex).OrderByDescending(k => k).ToList();
        foreach (var key in keysToUpdate)
        {
            int rot = _pageRotations[key];
            _pageRotations.Remove(key);
            _pageRotations[key + 1] = rot;
        }
        
        foreach (var ann in Annotations.Where(a => a.PageNumber > sourceIndex))
        {
            ann.PageNumber++;
        }
        
        CurrentPageIndex = sourceIndex + 1;
        
        HasPageOrderChanged = true;
        StatusMessage = $"Page {sourceIndex + 1} duplicated. Save to apply changes permanently.";
    }

    #endregion

    #region About

    [RelayCommand]
    private void About()
    {
        MessageBox.Show(
            "OpenJPDF - PDF Editor\n\n" +
            "Version 1.0.0\n\n" +
            "Features:\n" +
            "• View PDF files\n" +
            "• Add text and images\n" +
            "• Merge multiple PDFs\n" +
            "• Split PDF into pages\n" +
            "• Extract specific pages\n" +
            "• Rotate and delete pages\n" +
            "• Edit element properties",
            "About OpenJPDF",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion
}
