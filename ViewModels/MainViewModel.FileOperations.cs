// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.IO;
using CommunityToolkit.Mvvm.Input;
using OpenJPDF.Models;
using OpenJPDF.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace OpenJPDF.ViewModels;

/// <summary>
/// MainViewModel - File Operations (Open, Save, Load, Navigation, Zoom)
/// </summary>
public partial class MainViewModel
{
    #region Open File Commands

    [RelayCommand]
    private async Task OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
            Title = "Open PDF File",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var fileName in dialog.FileNames)
            {
                await OpenFileInNewTab(fileName);
            }
        }
    }

    /// <summary>
    /// Open a PDF file from command line argument or external source
    /// </summary>
    public async Task OpenFileFromPathAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;
            
        await OpenFileInNewTab(filePath);
    }

    /// <summary>
    /// Open a PDF file in a new tab (or switch to existing if already open)
    /// </summary>
    private async Task OpenFileInNewTab(string fileName)
    {
        // Check if already open
        var existing = OpenDocuments.FirstOrDefault(d => 
            string.Equals(d.FilePath, fileName, StringComparison.OrdinalIgnoreCase));
        
        if (existing != null)
        {
            ActiveDocument = existing;
            StatusMessage = $"Switched to: {existing.FileName}";
            return;
        }

        StatusMessage = "Loading PDF...";

        // Create a new document tab
        var newTab = new DocumentTab();
        bool success = await newTab.PdfService.LoadPdfAsync(fileName);

        if (success)
        {
            newTab.FilePath = fileName;
            newTab.FileName = Path.GetFileName(fileName);
            newTab.TotalPages = newTab.PdfService.PageCount;
            newTab.IsFileLoaded = true;
            newTab.CurrentPageIndex = 0;

            // Load thumbnails for the new tab
            await LoadThumbnailsForTab(newTab);
            
            // Load all pages for continuous scroll
            await LoadAllPagesForTab(newTab);

            // Add to collection and make active
            OpenDocuments.Add(newTab);
            ActiveDocument = newTab;

            OnPropertyChanged(nameof(HasMultipleDocuments));
            OnPropertyChanged(nameof(HasOpenDocuments));
            StatusMessage = $"Loaded: {newTab.FileName}";
        }
        else
        {
            newTab.Dispose();
            StatusMessage = "Failed to load PDF";
            MessageBox.Show($"Failed to load the PDF file:\n{fileName}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Load Page Methods

    /// <summary>
    /// Load thumbnails for a specific document tab with batched UI updates.
    /// Uses progressive loading - creates placeholders first, then loads images in background.
    /// </summary>
    private async Task LoadThumbnailsForTab(DocumentTab tab)
    {
        tab.PageThumbnails.Clear();
        
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Phase 1: Create all placeholder thumbnails immediately (instant UI)
        var placeholders = new List<PageThumbnail>();
        for (int i = 0; i < tab.TotalPages; i++)
        {
            placeholders.Add(new PageThumbnail
            {
                PageNumber = i + 1,
                OriginalPageIndex = i,
                Thumbnail = null // Placeholder - will load actual image lazily
            });
        }
        
        // Add all placeholders at once (single UI update)
        Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var placeholder in placeholders)
            {
                tab.PageThumbnails.Add(placeholder);
            }
        });
        
        System.Diagnostics.Debug.WriteLine($"[PERF] Created {tab.TotalPages} thumbnail placeholders in {sw.ElapsedMilliseconds}ms");

        // Phase 2: Load visible thumbnails first (first 10), then rest in background
        int visibleCount = Math.Min(10, tab.TotalPages);
        
        await Task.Run(() =>
        {
            // Load first batch (visible thumbnails) immediately
            for (int i = 0; i < visibleCount; i++)
            {
                var thumbnail = tab.PdfService.GetPageThumbnail(i);
                var pageIdx = i;
                
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (pageIdx < tab.PageThumbnails.Count)
                    {
                        tab.PageThumbnails[pageIdx].Thumbnail = thumbnail;
                    }
                });
            }
        });
        
        System.Diagnostics.Debug.WriteLine($"[PERF] Loaded {visibleCount} visible thumbnails in {sw.ElapsedMilliseconds}ms");

        // Phase 3: Load remaining thumbnails in background (non-blocking, with cancellation)
        if (tab.TotalPages > visibleCount)
        {
            var cancellationToken = tab.GetLoadingCancellationToken();
            
            _ = Task.Run(async () =>
            {
                for (int i = visibleCount; i < tab.TotalPages; i++)
                {
                    // Check if cancelled (e.g., document closed or tab switched)
                    if (cancellationToken.IsCancellationRequested)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PERF] Thumbnail loading cancelled at page {i}");
                        return;
                    }
                    
                    var thumbnail = tab.PdfService.GetPageThumbnail(i);
                    var pageIdx = i;
                    
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (pageIdx < tab.PageThumbnails.Count)
                        {
                            tab.PageThumbnails[pageIdx].Thumbnail = thumbnail;
                        }
                    });
                    
                    // Small delay to avoid overwhelming UI thread
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }
                
                System.Diagnostics.Debug.WriteLine($"[PERF] Loaded all {tab.TotalPages} thumbnails in {sw.ElapsedMilliseconds}ms");
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Load pages for a specific document tab with lazy loading.
    /// Only loads the current page and a few pages around it.
    /// </summary>
    private async Task LoadAllPagesForTab(DocumentTab tab)
    {
        tab.PageImages.Clear();
        
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Phase 1: Create all placeholder page images immediately
        var placeholders = new List<PageImage>();
        for (int i = 0; i < tab.TotalPages; i++)
        {
            placeholders.Add(new PageImage(i, null)); // Placeholder
        }
        
        // Add all placeholders at once (single UI update)
        Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var placeholder in placeholders)
            {
                tab.PageImages.Add(placeholder);
            }
        });
        
        System.Diagnostics.Debug.WriteLine($"[PERF] Created {tab.TotalPages} page placeholders in {sw.ElapsedMilliseconds}ms");

        // Phase 2: Load only the current page and nearby pages
        await LoadVisiblePagesAsync(tab, tab.CurrentPageIndex, windowSize: 2);
        
        System.Diagnostics.Debug.WriteLine($"[PERF] Initial page load completed in {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Load pages around the specified center page (lazy loading).
    /// </summary>
    private async Task LoadVisiblePagesAsync(DocumentTab tab, int centerPage, int windowSize = 2)
    {
        float scale = (float)tab.ZoomScale;
        
        int startPage = Math.Max(0, centerPage - windowSize);
        int endPage = Math.Min(tab.TotalPages - 1, centerPage + windowSize);
        
        await Task.Run(() =>
        {
            for (int i = startPage; i <= endPage; i++)
            {
                // Skip if already loaded
                if (i < tab.PageImages.Count && tab.PageImages[i].Image != null)
                    continue;
                
                // Get the ORIGINAL page index from the thumbnail at this position
                int originalPageIndex = i;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (i < tab.PageThumbnails.Count)
                    {
                        originalPageIndex = tab.PageThumbnails[i].OriginalPageIndex;
                    }
                });
                
                int rotation = tab.GetPageRotation(i);
                var image = tab.PdfService.GetPageImage(originalPageIndex, scale, rotation);
                var pageIdx = i;
                
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (pageIdx < tab.PageImages.Count)
                    {
                        tab.PageImages[pageIdx].UpdateImage(image);
                        
                        // Update current page image if this is the current page
                        if (pageIdx == tab.CurrentPageIndex)
                        {
                            tab.CurrentPageImage = image;
                        }
                    }
                });
            }
        });
    }

    /// <summary>
    /// Preload pages around the current page (called when scrolling or changing pages).
    /// </summary>
    public async Task PreloadNearbyPagesAsync(int centerPage)
    {
        if (ActiveDocument == null) return;
        await LoadVisiblePagesAsync(ActiveDocument, centerPage, windowSize: 3);
    }

    private void LoadCurrentPage()
    {
        if (!IsFileLoaded) return;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        var pageImages = ActiveDocument?.PageImages ?? PageImages;
        var pageThumbnails = ActiveDocument?.PageThumbnails ?? PageThumbnails;

        float scale = (float)ZoomScale;
        int rotation = GetPageRotation(CurrentPageIndex);
        
        // Get the ORIGINAL page index from the thumbnail at current position
        // This correctly handles reordering and duplications
        int originalPageIndex = CurrentPageIndex;
        if (CurrentPageIndex >= 0 && CurrentPageIndex < pageThumbnails.Count)
        {
            originalPageIndex = pageThumbnails[CurrentPageIndex].OriginalPageIndex;
        }
        
        // Use ORIGINAL page index to render from PDF
        CurrentPageImage = pdfService.GetPageImage(originalPageIndex, scale, rotation);

        // Also update the page in continuous scroll view
        if (CurrentPageIndex >= 0 && CurrentPageIndex < pageImages.Count)
        {
            pageImages[CurrentPageIndex].UpdateImage(CurrentPageImage);
        }
        
        // Preload nearby pages in background (non-blocking)
        _ = PreloadNearbyPagesAsync(CurrentPageIndex);
    }

    /// <summary>
    /// Load all pages for continuous scroll view with lazy loading.
    /// Creates placeholders first, then loads visible pages.
    /// </summary>
    private async Task LoadAllPagesAsync()
    {
        PageImages.Clear();
        
        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        float scale = (float)ZoomScale;

        StatusMessage = "Loading pages...";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Phase 1: Create placeholders for all pages
        var placeholders = new List<PageImage>();
        for (int i = 0; i < TotalPages; i++)
        {
            placeholders.Add(new PageImage(i, null));
        }
        
        // Batch add all placeholders
        foreach (var placeholder in placeholders)
        {
            PageImages.Add(placeholder);
        }
        
        System.Diagnostics.Debug.WriteLine($"[PERF] Created {TotalPages} page placeholders in {sw.ElapsedMilliseconds}ms");

        // Phase 2: Load first page immediately
        if (TotalPages > 0)
        {
            await Task.Run(() =>
            {
                int rotation = GetPageRotation(0);
                var image = pdfService.GetPageImage(0, scale, rotation);
                
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (PageImages.Count > 0)
                    {
                        PageImages[0].UpdateImage(image);
                        CurrentPageImage = image;
                    }
                });
            });
        }

        // Phase 3: Preload nearby pages in background
        if (ActiveDocument != null)
        {
            _ = LoadVisiblePagesAsync(ActiveDocument, 0, windowSize: 2);
        }

        sw.Stop();
        StatusMessage = $"Ready - {TotalPages} pages";
        System.Diagnostics.Debug.WriteLine($"[PERF] Initial load completed in {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Reload pages after zoom change - only reloads visible pages, clears cache.
    /// </summary>
    private async Task ReloadAllPagesAsync()
    {
        if (!IsFileLoaded) return;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        float scale = (float)ZoomScale;
        
        // Clear page cache since zoom changed (thumbnail cache remains valid)
        if (pdfService is PdfService ps)
        {
            ps.ClearPageCache();
        }

        // Clear all loaded images to force reload
        for (int i = 0; i < PageImages.Count; i++)
        {
            PageImages[i].UpdateImage(null);
        }

        // Reload only current page and nearby pages
        int currentPage = CurrentPageIndex;
        int windowSize = 2;
        int startPage = Math.Max(0, currentPage - windowSize);
        int endPage = Math.Min(PageImages.Count - 1, currentPage + windowSize);

        await Task.Run(() =>
        {
            for (int i = startPage; i <= endPage; i++)
            {
                int pageIndex = i;
                int rotation = GetPageRotation(pageIndex);
                var image = pdfService.GetPageImage(pageIndex, scale, rotation);
                
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (pageIndex < PageImages.Count)
                    {
                        PageImages[pageIndex].UpdateImage(image);
                    }
                });
            }
        });

        if (CurrentPageIndex < PageImages.Count)
        {
            CurrentPageImage = PageImages[CurrentPageIndex].Image;
        }
        
        System.Diagnostics.Debug.WriteLine($"[PERF] Reloaded {endPage - startPage + 1} pages after zoom change");
    }

    private async Task LoadThumbnailsAsync()
    {
        PageThumbnails.Clear();
        HasPageOrderChanged = false;

        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Phase 1: Create all placeholders
        var placeholders = new List<PageThumbnail>();
        for (int i = 0; i < TotalPages; i++)
        {
            placeholders.Add(new PageThumbnail
            {
                PageNumber = i + 1,
                OriginalPageIndex = i,
                Thumbnail = null
            });
        }
        
        // Batch add all placeholders
        foreach (var placeholder in placeholders)
        {
            PageThumbnails.Add(placeholder);
        }

        // Phase 2: Load visible thumbnails first (first 10)
        int visibleCount = Math.Min(10, TotalPages);
        
        await Task.Run(() =>
        {
            for (int i = 0; i < visibleCount; i++)
            {
                var thumbnail = pdfService.GetPageThumbnail(i);
                var pageIdx = i;
                
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (pageIdx < PageThumbnails.Count)
                    {
                        PageThumbnails[pageIdx].Thumbnail = thumbnail;
                    }
                });
            }
        });

        // Phase 3: Load remaining thumbnails in background
        if (TotalPages > visibleCount)
        {
            _ = Task.Run(async () =>
            {
                for (int i = visibleCount; i < TotalPages; i++)
                {
                    var thumbnail = pdfService.GetPageThumbnail(i);
                    var pageIdx = i;
                    
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (pageIdx < PageThumbnails.Count)
                        {
                            PageThumbnails[pageIdx].Thumbnail = thumbnail;
                        }
                    });
                    
                    await Task.Delay(10); // Prevent UI thread blocking
                }
            });
        }
        
        System.Diagnostics.Debug.WriteLine($"[PERF] Thumbnail loading started in {sw.ElapsedMilliseconds}ms");
    }

    #endregion

    #region Save Commands

    [RelayCommand]
    private async Task Save()
    {
        if (!IsFileLoaded || string.IsNullOrEmpty(FilePath)) return;

        var logPath = Path.Combine(Path.GetDirectoryName(FilePath) ?? Path.GetTempPath(), "OpenJPDF_Save_Log.txt");

        void Log(string message)
        {
            var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            System.Diagnostics.Debug.WriteLine(logLine);
            try { File.AppendAllText(logPath, logLine + Environment.NewLine); } catch { }
        }

        try
        {
            Log($"=== SAVE START === FilePath: {FilePath}");
            StatusMessage = "Saving...";

            // Remember current page to restore after save
            int savedPageIndex = CurrentPageIndex;

            var pdfService = ActiveDocument?.PdfService ?? _pdfService;
            Log($"PdfService: {(ActiveDocument != null ? "ActiveDocument.PdfService" : "_pdfService")}");
            Log($"Current PageCount: {pdfService.PageCount}");

            Log("Step 1: Apply content modifications");
            ApplyContentModificationsToService(pdfService);
            Log("Content modifications applied");

            Log("Step 2: SyncAnnotationsToService");
            SyncAnnotationsToService();
            Log($"Annotations synced: {Annotations.Count}");

            Log("Step 3: SaveAsync");
            bool success = await pdfService.SaveAsync(FilePath);
            Log($"SaveAsync result: {success}");

            if (success)
            {
                // Apply header/footer if configured
                if (HasHeaderFooter && HeaderFooterConfig != null)
                {
                    Log("Step 3: Apply header/footer");
                    StatusMessage = "Applying header/footer...";
                    string tempFile = Path.GetTempFileName();
                    try
                    {
                        bool hfSuccess = await pdfService.ApplyHeaderFooterAsync(FilePath, tempFile, HeaderFooterConfig, Path.GetFileName(FilePath));
                        Log($"ApplyHeaderFooterAsync result: {hfSuccess}");
                        if (hfSuccess)
                        {
                            File.Delete(FilePath);
                            File.Move(tempFile, FilePath);
                            Log("Header/Footer applied and file replaced");
                        }
                        else
                        {
                            if (File.Exists(tempFile)) File.Delete(tempFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Header/Footer ERROR: {ex.Message}\n{ex.StackTrace}");
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                    }
                }

                Log("Step 4: LoadPdfAsync (reload)");
                bool loadSuccess = await pdfService.LoadPdfAsync(FilePath);
                Log($"LoadPdfAsync result: {loadSuccess}, PageCount: {pdfService.PageCount}");

                StatusMessage = "Saved successfully";

                Log("Step 5: Clear annotations");
                Annotations.Clear();
                SelectedAnnotation = null;
                ClearAnnotationsRequested?.Invoke();
                ClearPageRotations();

                if (ActiveDocument != null)
                {
                    Log("Step 6: Reload ActiveDocument");
                    ActiveDocument.HasUnsavedChanges = false;
                    ActiveDocument.ClearPageRotations();
                    ActiveDocument.TotalPages = pdfService.PageCount;
                    Log($"ActiveDocument.TotalPages set to: {ActiveDocument.TotalPages}");

                    Log("Step 7: LoadThumbnailsForTab");
                    await LoadThumbnailsForTab(ActiveDocument);
                    Log($"Thumbnails loaded: {ActiveDocument.PageThumbnails.Count}");

                    Log("Step 8: LoadAllPagesForTab");
                    await LoadAllPagesForTab(ActiveDocument);
                    Log($"Pages loaded: {ActiveDocument.PageImages.Count}");

                    // Sync collections to MainViewModel
                    Log("Step 8.1: Sync collections");
                    PageThumbnails = ActiveDocument.PageThumbnails;
                    PageImages = ActiveDocument.PageImages;
                    TotalPages = ActiveDocument.TotalPages;
                    Log($"MainViewModel PageImages.Count: {PageImages.Count}");
                }
                else
                {
                    Log("Step 6: Reload (no ActiveDocument)");
                    TotalPages = pdfService.PageCount;

                    Log("Step 7: LoadThumbnailsAsync");
                    await LoadThumbnailsAsync();
                    Log($"Thumbnails loaded: {PageThumbnails.Count}");

                    Log("Step 8: LoadAllPagesAsync");
                    await LoadAllPagesAsync();
                    Log($"Pages loaded: {PageImages.Count}");
                }

                // Restore to saved page (clamped to valid range)
                Log($"Step 9: Restore page index (saved: {savedPageIndex}, total: {TotalPages})");
                CurrentPageIndex = Math.Clamp(savedPageIndex, 0, Math.Max(0, TotalPages - 1));
                if (ActiveDocument != null)
                {
                    ActiveDocument.CurrentPageIndex = CurrentPageIndex;
                }

                Log("Step 10: LoadCurrentPage");
                LoadCurrentPage();
                Log($"CurrentPageIndex: {CurrentPageIndex}, CurrentPageImage is null: {CurrentPageImage == null}");

                Log("=== SAVE COMPLETE ===");
            }
            else
            {
                Log("=== SAVE FAILED (SaveAsync returned false) ===");
                StatusMessage = "Save failed";
                MessageBox.Show("Failed to save the PDF file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Log($"=== SAVE EXCEPTION ===\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            StatusMessage = $"Save error: {ex.Message}";
            MessageBox.Show($"Error saving PDF:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SyncAnnotationsToService()
    {
        var pdfService = ActiveDocument?.PdfService ?? _pdfService;
        
        pdfService.ClearAnnotations();
        
        System.Diagnostics.Debug.WriteLine($"SyncAnnotationsToService: {Annotations.Count} annotations to sync");
        
        foreach (var ann in Annotations)
        {
            if (ann is TextAnnotationItem textItem)
            {
                var textAnn = textItem.ToAnnotation();
                System.Diagnostics.Debug.WriteLine($"  Text: '{textAnn.Text}' at ({textAnn.X}, {textAnn.Y}), Size: {textAnn.FontSize}, W:{textAnn.Width}, H:{textAnn.Height}");
                pdfService.AddText(textAnn);
            }
            else if (ann is ImageAnnotationItem imgItem)
            {
                var imgAnn = imgItem.ToAnnotation();
                System.Diagnostics.Debug.WriteLine($"  Image: '{imgAnn.ImagePath}' at ({imgAnn.X}, {imgAnn.Y})");
                pdfService.AddImage(imgAnn);
            }
            else if (ann is ShapeAnnotationItem shapeItem)
            {
                var shapeAnn = shapeItem.ToAnnotation();
                System.Diagnostics.Debug.WriteLine($"  Shape: {shapeAnn.ShapeType} at ({shapeAnn.X}, {shapeAnn.Y})");
                pdfService.AddShape(shapeAnn);
            }
        }
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        if (!IsFileLoaded) return;

        var dialog = new SaveFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf",
            Title = "Save PDF As",
            FileName = Path.GetFileName(FilePath) ?? "document.pdf"
        };

        if (dialog.ShowDialog() == true)
        {
            var pdfService = ActiveDocument?.PdfService ?? _pdfService;

            // Remember current page to restore after save
            int savedPageIndex = CurrentPageIndex;

            StatusMessage = "Saving...";
            ApplyContentModificationsToService(pdfService);
            SyncAnnotationsToService();
            bool success = await pdfService.SaveAsync(dialog.FileName);

            if (success)
            {
                // Apply header/footer if configured
                if (HasHeaderFooter && HeaderFooterConfig != null)
                {
                    StatusMessage = "Applying header/footer...";
                    string tempFile = Path.GetTempFileName();
                    try
                    {
                        bool hfSuccess = await pdfService.ApplyHeaderFooterAsync(dialog.FileName, tempFile, HeaderFooterConfig, Path.GetFileName(dialog.FileName));
                        if (hfSuccess)
                        {
                            // Replace saved file with header/footer applied version
                            File.Delete(dialog.FileName);
                            File.Move(tempFile, dialog.FileName);
                            System.Diagnostics.Debug.WriteLine("Header/Footer applied successfully during SaveAs");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to apply header/footer during SaveAs");
                            if (File.Exists(tempFile)) File.Delete(tempFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error applying header/footer: {ex.Message}");
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                    }
                }
                
                FilePath = dialog.FileName;

                // Reload the document after SaveAs to refresh file handles
                await pdfService.LoadPdfAsync(dialog.FileName);

                Annotations.Clear();
                SelectedAnnotation = null;
                ClearAnnotationsRequested?.Invoke();
                ClearPageRotations();

                // Reload thumbnails and all pages for continuous scroll
                if (ActiveDocument != null)
                {
                    ActiveDocument.FilePath = dialog.FileName;
                    ActiveDocument.FileName = Path.GetFileName(dialog.FileName);
                    ActiveDocument.TotalPages = pdfService.PageCount;
                    ActiveDocument.HasUnsavedChanges = false;
                    ActiveDocument.ClearPageRotations();
                    await LoadThumbnailsForTab(ActiveDocument);
                    await LoadAllPagesForTab(ActiveDocument);

                    // Sync collections to MainViewModel
                    PageThumbnails = ActiveDocument.PageThumbnails;
                    PageImages = ActiveDocument.PageImages;
                    TotalPages = ActiveDocument.TotalPages;
                }
                else
                {
                    TotalPages = pdfService.PageCount;
                    await LoadThumbnailsAsync();
                    await LoadAllPagesAsync();
                }

                // Restore to saved page (clamped to valid range)
                CurrentPageIndex = Math.Clamp(savedPageIndex, 0, Math.Max(0, TotalPages - 1));
                if (ActiveDocument != null)
                {
                    ActiveDocument.CurrentPageIndex = CurrentPageIndex;
                }
                LoadCurrentPage();

                StatusMessage = $"Saved: {Path.GetFileName(dialog.FileName)}";
            }
            else
            {
                StatusMessage = "Save failed";
                MessageBox.Show("Failed to save the PDF file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Navigation Commands

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPageIndex < TotalPages - 1)
        {
            CurrentPageIndex++;
        }
    }

    #endregion

    #region Zoom Commands

    [RelayCommand]
    private void ZoomIn()
    {
        if (!IsFileLoaded) return;
        
        // Get current zoom percentage
        if (int.TryParse(ZoomLevel.TrimEnd('%'), out int currentPercent))
        {
            // Increment by 10%, cap at 500%
            int newPercent = Math.Min(currentPercent + 10, 500);
            ZoomLevel = $"{newPercent}%";
        }
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (!IsFileLoaded) return;
        
        // Get current zoom percentage
        if (int.TryParse(ZoomLevel.TrimEnd('%'), out int currentPercent))
        {
            // Decrement by 10%, floor at 10%
            int newPercent = Math.Max(currentPercent - 10, 10);
            ZoomLevel = $"{newPercent}%";
        }
    }

    partial void OnZoomLevelChanged(string value)
    {
        if (int.TryParse(value.TrimEnd('%'), out int percent))
        {
            // Clamp to valid range
            percent = Math.Clamp(percent, 10, 500);
            
            ZoomScale = percent / 100.0;
            
            // Update ZoomLevel to show actual clamped value if different
            if (value != $"{percent}%")
            {
                ZoomLevel = $"{percent}%";
                return; // Prevent double update
            }
            
            // Skip reload during document sync or if no file loaded
            if (_isSyncingDocument || !IsFileLoaded) return;
            
            LoadCurrentPage();
            RefreshAnnotationsRequested?.Invoke();
            RefreshHeaderFooterPreview?.Invoke();
        }
    }

    #endregion
}
