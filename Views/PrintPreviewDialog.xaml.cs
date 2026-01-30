// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenJPDF.Services;
using WpfImage = System.Windows.Controls.Image;
using WpfMessageBox = System.Windows.MessageBox;
using WpfPrintDialog = System.Windows.Controls.PrintDialog;

namespace OpenJPDF.Views;

/// <summary>
/// Professional Print Preview Dialog with paper settings
/// </summary>
public partial class PrintPreviewDialog : Window
{
    private readonly IPdfService _pdfService;
    private readonly int _totalPages;
    private readonly int _currentPageIndex;
    
    private int _previewPageIndex;
    private double _previewZoom = 1.0;
    private List<int> _pagesToPrint = new();
    
    // Paper sizes in points (1 inch = 72 points)
    private static readonly Dictionary<string, (double Width, double Height)> PaperSizes = new()
    {
        { "A4", (595, 842) },      // 210 × 297 mm
        { "Letter", (612, 792) },  // 8.5 × 11 in
        { "Legal", (612, 1008) },  // 8.5 × 14 in
        { "A3", (842, 1191) },     // 297 × 420 mm
        { "A5", (420, 595) },      // 148 × 210 mm
        { "B5", (499, 709) },      // 176 × 250 mm
    };
    
    public bool PrintRequested { get; private set; }
    
    public PrintPreviewDialog(IPdfService pdfService, int totalPages, int currentPageIndex)
    {
        InitializeComponent();
        
        _pdfService = pdfService;
        _totalPages = totalPages;
        _currentPageIndex = currentPageIndex;
        _previewPageIndex = currentPageIndex;
        
        LoadPrinters();
        InitializeDefaults();
        UpdatePagesToPrint();
        UpdatePreview();
    }
    
    private void LoadPrinters()
    {
        try
        {
            using var printServer = new LocalPrintServer();
            var defaultPrinter = printServer.DefaultPrintQueue?.Name ?? "";
            var printQueues = printServer.GetPrintQueues();
            
            int defaultIndex = 0;
            int index = 0;
            
            foreach (var queue in printQueues)
            {
                PrinterComboBox.Items.Add(queue.Name);
                if (queue.Name == defaultPrinter)
                {
                    defaultIndex = index;
                }
                index++;
            }
            
            if (PrinterComboBox.Items.Count > 0)
            {
                PrinterComboBox.SelectedIndex = defaultIndex;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading printers: {ex.Message}");
            PrinterComboBox.Items.Add("No printers found");
            PrinterComboBox.SelectedIndex = 0;
            PrintButton.IsEnabled = false;
        }
    }
    
    private void InitializeDefaults()
    {
        PaperSizeComboBox.SelectedIndex = 0; // A4
        OrientationComboBox.SelectedIndex = 0; // Portrait
        
        // Update current page label with actual page number
        CurrentPageLabel.Text = $"Current Page ({_currentPageIndex + 1})";
        
        // Set hint text
        PageRangeHint.Text = $"e.g., 1-5, 8, 11-{_totalPages}";
        
        // Default to empty custom range - will be filled when selected
        CustomRangeTextBox.Text = "";
        CustomRangeTextBox.IsEnabled = false;
    }
    
    private void PrinterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Could update available paper sizes based on printer capabilities
    }
    
    private void Settings_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }
    
    private void PageRange_Changed(object sender, RoutedEventArgs e)
    {
        if (CustomRangeTextBox == null) return;
        
        bool isCustom = CustomRangeRadio?.IsChecked == true;
        CustomRangeTextBox.IsEnabled = isCustom;
        
        if (isCustom)
        {
            // If empty, populate with all pages as default
            if (string.IsNullOrWhiteSpace(CustomRangeTextBox.Text))
            {
                CustomRangeTextBox.Text = $"1-{_totalPages}";
            }
            CustomRangeTextBox.Focus();
            CustomRangeTextBox.SelectAll();
        }
        
        UpdatePagesToPrint();
        UpdatePreview();
    }
    
    private void CustomRangeTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Auto-select the custom range radio when clicking on text box
        if (CustomRangeRadio != null && CustomRangeRadio.IsChecked != true)
        {
            CustomRangeRadio.IsChecked = true;
        }
    }
    
    private void CustomRange_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CustomRangeRadio?.IsChecked == true)
        {
            UpdatePagesToPrint();
            UpdatePreview();
        }
    }
    
    private void UpdatePagesToPrint()
    {
        _pagesToPrint.Clear();
        
        if (AllPagesRadio?.IsChecked == true)
        {
            for (int i = 0; i < _totalPages; i++)
            {
                _pagesToPrint.Add(i);
            }
            if (PageRangeHint != null)
            {
                PageRangeHint.Text = $"Total: {_totalPages} pages";
                PageRangeHint.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128));
            }
        }
        else if (CurrentPageRadio?.IsChecked == true)
        {
            _pagesToPrint.Add(_currentPageIndex);
            if (PageRangeHint != null)
            {
                PageRangeHint.Text = $"Page {_currentPageIndex + 1} selected";
                PageRangeHint.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128));
            }
        }
        else if (CustomRangeRadio?.IsChecked == true)
        {
            _pagesToPrint = ParsePageRange(CustomRangeTextBox?.Text ?? "");
            
            if (PageRangeHint != null)
            {
                if (_pagesToPrint.Count == 0)
                {
                    PageRangeHint.Text = $"⚠ Invalid range. Use: 1-{_totalPages}";
                    PageRangeHint.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 150, 50));
                }
                else
                {
                    PageRangeHint.Text = $"✓ {_pagesToPrint.Count} page(s) selected";
                    PageRangeHint.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 200, 100));
                }
            }
        }
        
        // Fallback to first page if no pages selected
        if (_pagesToPrint.Count == 0)
        {
            _pagesToPrint.Add(0);
        }
        
        // Ensure preview page is in range
        if (!_pagesToPrint.Contains(_previewPageIndex))
        {
            _previewPageIndex = _pagesToPrint.FirstOrDefault();
        }
        
        UpdatePrintSummary();
    }
    
    private List<int> ParsePageRange(string rangeText)
    {
        var pages = new List<int>();
        if (string.IsNullOrWhiteSpace(rangeText)) return pages;
        
        try
        {
            var parts = rangeText.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains('-'))
                {
                    var range = trimmed.Split('-');
                    if (range.Length == 2 && 
                        int.TryParse(range[0].Trim(), out int start) && 
                        int.TryParse(range[1].Trim(), out int end))
                    {
                        for (int i = Math.Max(1, start); i <= Math.Min(_totalPages, end); i++)
                        {
                            if (!pages.Contains(i - 1))
                                pages.Add(i - 1); // Convert to 0-based
                        }
                    }
                }
                else if (int.TryParse(trimmed, out int page))
                {
                    if (page >= 1 && page <= _totalPages && !pages.Contains(page - 1))
                    {
                        pages.Add(page - 1); // Convert to 0-based
                    }
                }
            }
        }
        catch { }
        
        pages.Sort();
        return pages;
    }
    
    private void UpdatePrintSummary()
    {
        if (PrintSummary == null) return;
        
        int copies = 1;
        int.TryParse(CopiesTextBox?.Text, out copies);
        copies = Math.Max(1, copies);
        
        int pageCount = _pagesToPrint.Count;
        int totalSheets = pageCount * copies;
        
        string pagesText = pageCount == 1 ? "1 page" : $"{pageCount} pages";
        string sheetsText = totalSheets == 1 ? "1 sheet" : $"{totalSheets} sheets";
        
        PrintSummary.Text = $"Print {pagesText} ({sheetsText})";
    }
    
    private void UpdatePreview()
    {
        if (PreviewImage == null || PaperBorder == null) return;
        
        try
        {
            // Get paper size
            string paperTag = (PaperSizeComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "A4";
            var (paperWidth, paperHeight) = PaperSizes.GetValueOrDefault(paperTag, PaperSizes["A4"]);
            
            // Check orientation
            bool isLandscape = (OrientationComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Landscape";
            if (isLandscape)
            {
                (paperWidth, paperHeight) = (paperHeight, paperWidth);
            }
            
            // Get margin
            double margin = 15;
            if (MarginsComboBox?.SelectedItem is ComboBoxItem marginItem)
            {
                double.TryParse(marginItem.Tag?.ToString(), out margin);
            }
            
            // Scale paper size for display (screen pixels)
            double displayScale = _previewZoom * 0.8; // Base scale factor
            double displayWidth = paperWidth * displayScale;
            double displayHeight = paperHeight * displayScale;
            
            PaperBorder.Width = displayWidth;
            PaperBorder.Height = displayHeight;
            
            // Update margin border
            MarginBorder.Margin = new Thickness(margin * displayScale / 2);
            PreviewImage.Margin = new Thickness(margin * displayScale / 2);
            
            // Render page
            var pageImage = _pdfService.GetPageImage(_previewPageIndex, 1.5f);
            
            if (pageImage != null)
            {
                // Apply grayscale if selected
                if ((ColorModeComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Grayscale")
                {
                    PreviewImage.Source = ConvertToGrayscale(pageImage);
                }
                else
                {
                    PreviewImage.Source = pageImage;
                }
            }
            
            // Update page indicator
            int displayIndex = _pagesToPrint.IndexOf(_previewPageIndex) + 1;
            PageIndicator.Text = $"Page {displayIndex} of {_pagesToPrint.Count}";
            
            // Update zoom indicator
            ZoomIndicator.Text = $"{(int)(_previewZoom * 100)}%";
            
            // Update navigation buttons
            PrevPageButton.IsEnabled = displayIndex > 1;
            NextPageButton.IsEnabled = displayIndex < _pagesToPrint.Count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating preview: {ex.Message}");
        }
    }
    
    private BitmapSource ConvertToGrayscale(BitmapSource source)
    {
        var grayscale = new FormatConvertedBitmap();
        grayscale.BeginInit();
        grayscale.Source = source;
        grayscale.DestinationFormat = PixelFormats.Gray8;
        grayscale.EndInit();
        grayscale.Freeze();
        return grayscale;
    }
    
    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        int currentIndex = _pagesToPrint.IndexOf(_previewPageIndex);
        if (currentIndex > 0)
        {
            _previewPageIndex = _pagesToPrint[currentIndex - 1];
            UpdatePreview();
        }
    }
    
    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        int currentIndex = _pagesToPrint.IndexOf(_previewPageIndex);
        if (currentIndex < _pagesToPrint.Count - 1)
        {
            _previewPageIndex = _pagesToPrint[currentIndex + 1];
            UpdatePreview();
        }
    }
    
    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (_previewZoom > 0.25)
        {
            _previewZoom -= 0.25;
            UpdatePreview();
        }
    }
    
    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (_previewZoom < 3.0)
        {
            _previewZoom += 0.25;
            UpdatePreview();
        }
    }
    
    private void CopiesTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }
    
    private void DecreaseCopies_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(CopiesTextBox.Text, out int copies) && copies > 1)
        {
            CopiesTextBox.Text = (copies - 1).ToString();
            UpdatePrintSummary();
        }
    }
    
    private void IncreaseCopies_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(CopiesTextBox.Text, out int copies) && copies < 999)
        {
            CopiesTextBox.Text = (copies + 1).ToString();
            UpdatePrintSummary();
        }
    }
    
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        PrintRequested = true;
        ExecutePrint();
    }
    
    private void ExecutePrint()
    {
        try
        {
            var printDialog = new WpfPrintDialog();
            
            // Set selected printer
            if (PrinterComboBox.SelectedItem != null)
            {
                try
                {
                    using var printServer = new LocalPrintServer();
                    var queue = printServer.GetPrintQueue(PrinterComboBox.SelectedItem.ToString()!);
                    printDialog.PrintQueue = queue;
                }
                catch { }
            }
            
            // Get settings
            string paperTag = (PaperSizeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "A4";
            var (paperWidth, paperHeight) = PaperSizes.GetValueOrDefault(paperTag, PaperSizes["A4"]);
            bool isLandscape = (OrientationComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Landscape";
            
            if (isLandscape)
            {
                (paperWidth, paperHeight) = (paperHeight, paperWidth);
            }
            
            double margin = 15;
            if (MarginsComboBox.SelectedItem is ComboBoxItem marginItem)
            {
                double.TryParse(marginItem.Tag?.ToString(), out margin);
            }
            
            int copies = 1;
            int.TryParse(CopiesTextBox.Text, out copies);
            copies = Math.Max(1, copies);
            
            string scaleTag = (ScaleComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Fit";
            bool isGrayscale = (ColorModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Grayscale";
            
            // Create FixedDocument
            var fixedDocument = new FixedDocument();
            
            // Convert points to WPF units (96 DPI)
            double wpfWidth = paperWidth * 96 / 72;
            double wpfHeight = paperHeight * 96 / 72;
            double wpfMargin = margin * 96 / 72;
            
            for (int copy = 0; copy < copies; copy++)
            {
                foreach (int pageIndex in _pagesToPrint)
                {
                    var pageContent = new PageContent();
                    var fixedPage = new FixedPage
                    {
                        Width = wpfWidth,
                        Height = wpfHeight
                    };
                    
                    // Render page at high quality
                    var pageImage = _pdfService.GetPageImage(pageIndex, 2.0f);
                    
                    if (pageImage != null)
                    {
                        BitmapSource finalImage = isGrayscale ? ConvertToGrayscale(pageImage) : pageImage;
                        
                        var image = new WpfImage
                        {
                            Source = finalImage,
                            Stretch = Stretch.Uniform
                        };
                        
                        // Calculate size based on scale
                        double contentWidth = wpfWidth - (wpfMargin * 2);
                        double contentHeight = wpfHeight - (wpfMargin * 2);
                        
                        double imageWidth = finalImage.PixelWidth;
                        double imageHeight = finalImage.PixelHeight;
                        
                        double scale;
                        if (scaleTag == "Fit")
                        {
                            double scaleX = contentWidth / imageWidth;
                            double scaleY = contentHeight / imageHeight;
                            scale = Math.Min(scaleX, scaleY);
                        }
                        else
                        {
                            scale = int.Parse(scaleTag) / 100.0 * (96.0 / 72.0); // Convert to screen scale
                        }
                        
                        double scaledWidth = imageWidth * scale;
                        double scaledHeight = imageHeight * scale;
                        
                        // Center on page
                        double offsetX = wpfMargin + (contentWidth - scaledWidth) / 2;
                        double offsetY = wpfMargin + (contentHeight - scaledHeight) / 2;
                        
                        // Ensure image fits within margins
                        scaledWidth = Math.Min(scaledWidth, contentWidth);
                        scaledHeight = Math.Min(scaledHeight, contentHeight);
                        
                        image.Width = scaledWidth;
                        image.Height = scaledHeight;
                        
                        FixedPage.SetLeft(image, Math.Max(wpfMargin, offsetX));
                        FixedPage.SetTop(image, Math.Max(wpfMargin, offsetY));
                        
                        fixedPage.Children.Add(image);
                    }
                    
                    ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
                    fixedDocument.Pages.Add(pageContent);
                }
            }
            
            // Print
            string documentName = $"OpenJPDF - {_pagesToPrint.Count} pages";
            printDialog.PrintDocument(fixedDocument.DocumentPaginator, documentName);
            
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Print failed: {ex.Message}", "Print Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
