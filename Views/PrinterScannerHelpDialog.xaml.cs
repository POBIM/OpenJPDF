// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Diagnostics;
using System.Printing;
using System.Windows;
using OpenJPDF.Services;

namespace OpenJPDF.Views;

public partial class PrinterScannerHelpDialog : Window
{
    private readonly ScannerService _scannerService = new();

    public PrinterScannerHelpDialog()
    {
        InitializeComponent();
        LoadDevices();
    }

    private void LoadDevices()
    {
        LoadScanners();
        LoadPrinters();
    }

    private void LoadScanners()
    {
        try
        {
            var scanners = _scannerService.GetAvailableScanners();
            
            if (scanners.Count == 0)
            {
                ScannerStatusText.Text = "❌ No scanners detected";
                ScannerStatusText.Foreground = System.Windows.Media.Brushes.Red;
                ScannersList.ItemsSource = null;
            }
            else
            {
                ScannerStatusText.Text = $"✅ Found {scanners.Count} scanner(s)";
                ScannerStatusText.Foreground = System.Windows.Media.Brushes.Green;
                ScannersList.ItemsSource = scanners;
            }
        }
        catch (Exception ex)
        {
            ScannerStatusText.Text = $"❌ Error detecting scanners: {ex.Message}";
            ScannerStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private void LoadPrinters()
    {
        try
        {
            var printers = new List<PrinterInfo>();
            
            using var printServer = new LocalPrintServer();
            var defaultPrinter = printServer.DefaultPrintQueue?.Name ?? "";
            var printQueues = printServer.GetPrintQueues();
            
            foreach (var queue in printQueues)
            {
                try
                {
                    var status = GetPrinterStatus(queue);
                    printers.Add(new PrinterInfo
                    {
                        Name = queue.Name,
                        Status = status,
                        IsDefault = queue.Name == defaultPrinter
                    });
                }
                catch
                {
                    printers.Add(new PrinterInfo
                    {
                        Name = queue.Name,
                        Status = "Unknown",
                        IsDefault = queue.Name == defaultPrinter
                    });
                }
            }

            if (printers.Count == 0)
            {
                PrinterStatusText.Text = "❌ No printers detected";
                PrinterStatusText.Foreground = System.Windows.Media.Brushes.Red;
                PrintersList.ItemsSource = null;
            }
            else
            {
                PrinterStatusText.Text = $"✅ Found {printers.Count} printer(s)";
                PrinterStatusText.Foreground = System.Windows.Media.Brushes.Green;
                // Sort: default printer first
                PrintersList.ItemsSource = printers.OrderByDescending(p => p.IsDefault).ToList();
            }
        }
        catch (Exception ex)
        {
            PrinterStatusText.Text = $"❌ Error detecting printers: {ex.Message}";
            PrinterStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private static string GetPrinterStatus(PrintQueue queue)
    {
        try
        {
            queue.Refresh();
            
            if (queue.IsOffline)
                return "Offline";
            if (queue.IsPaused)
                return "Paused";
            if (queue.HasPaperProblem)
                return "Paper Problem";
            if (queue.IsOutOfPaper)
                return "Out of Paper";
            if (queue.IsPaperJammed)
                return "Paper Jam";
            if (queue.IsTonerLow)
                return "Toner Low";
            if (queue.HasToner)
                return "Ready";
            if (queue.IsBusy)
                return "Busy";
            
            return "Ready";
        }
        catch
        {
            return "Unknown";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadDevices();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OpenWindowsSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Open Windows Settings > Devices > Printers & scanners
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:printers",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not open Windows Settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private class PrinterInfo
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsDefault { get; set; }
    }
}
