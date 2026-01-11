// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingImage = System.Drawing.Image;
using DrawingImaging = System.Drawing.Imaging;

namespace OpenJPDF.Services;

/// <summary>
/// Service for scanning documents using Windows Image Acquisition (WIA) API.
/// Works with scanners, printers with scan capability, and other WIA-compatible devices.
/// </summary>
public class ScannerService : IDisposable
{
    private bool _disposed;

    // WIA Device Type Constants
    private const int WIA_DEVICE_TYPE_SCANNER = 1;

    // WIA Property Constants
    private const string WIA_DEVICE_PROPERTY_ID = "DeviceID";
    private const string WIA_ITEM_PROPERTY_ID = "Item Name";

    // WIA Intent Constants  
    private const int WIA_INTENT_IMAGE_TYPE_COLOR = 1;
    private const int WIA_INTENT_IMAGE_TYPE_GRAYSCALE = 2;
    private const int WIA_INTENT_IMAGE_TYPE_TEXT = 4;

    // WIA Format Constants
    private static readonly string WIA_FORMAT_BMP = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
    private static readonly string WIA_FORMAT_PNG = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";
    private static readonly string WIA_FORMAT_JPEG = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";

    // WIA Property IDs for Scanner
    private const int WIA_HORIZONTAL_RESOLUTION = 6147;
    private const int WIA_VERTICAL_RESOLUTION = 6148;
    private const int WIA_HORIZONTAL_START = 6149;
    private const int WIA_VERTICAL_START = 6150;
    private const int WIA_HORIZONTAL_EXTENT = 6151;
    private const int WIA_VERTICAL_EXTENT = 6152;
    private const int WIA_COLOR_MODE = 6146;

    /// <summary>
    /// Represents a scanning device
    /// </summary>
    public class ScannerDevice
    {
        public string DeviceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Description { get; set; } = "";

        public override string ToString() => Name;
    }

    /// <summary>
    /// Scan settings
    /// </summary>
    public class ScanSettings
    {
        public int Dpi { get; set; } = 200;
        public bool ColorMode { get; set; } = true; // true = Color, false = Grayscale
        public bool ShowUI { get; set; } = false;
    }

    /// <summary>
    /// Get list of available scanner devices
    /// </summary>
    public List<ScannerDevice> GetAvailableScanners()
    {
        var scanners = new List<ScannerDevice>();

        try
        {
            // Create WIA DeviceManager
            Type? deviceManagerType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (deviceManagerType == null)
            {
                System.Diagnostics.Debug.WriteLine("WIA not available on this system");
                return scanners;
            }

            dynamic deviceManager = Activator.CreateInstance(deviceManagerType)!;

            // Enumerate devices
            foreach (dynamic deviceInfo in deviceManager.DeviceInfos)
            {
                // Check if it's a scanner
                if (deviceInfo.Type == WIA_DEVICE_TYPE_SCANNER)
                {
                    var scanner = new ScannerDevice
                    {
                        DeviceId = deviceInfo.DeviceID,
                        Name = GetPropertyValue(deviceInfo.Properties, "Name") ?? "Unknown Scanner",
                        Manufacturer = GetPropertyValue(deviceInfo.Properties, "Manufacturer") ?? "",
                        Description = GetPropertyValue(deviceInfo.Properties, "Description") ?? ""
                    };
                    scanners.Add(scanner);

                    System.Diagnostics.Debug.WriteLine($"Found scanner: {scanner.Name} ({scanner.DeviceId})");
                }
            }
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"WIA COM error: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating scanners: {ex.Message}");
        }

        return scanners;
    }

    /// <summary>
    /// Scan a document from the specified device
    /// </summary>
    /// <param name="device">Scanner device to use (null to show device selection dialog)</param>
    /// <param name="settings">Scan settings</param>
    /// <returns>Scanned image as BitmapSource, or null if cancelled/failed</returns>
    public BitmapSource? ScanDocument(ScannerDevice? device = null, ScanSettings? settings = null)
    {
        settings ??= new ScanSettings();

        try
        {
            // Create WIA CommonDialog
            Type? commonDialogType = Type.GetTypeFromProgID("WIA.CommonDialog");
            if (commonDialogType == null)
            {
                System.Diagnostics.Debug.WriteLine("WIA CommonDialog not available");
                return null;
            }

            dynamic commonDialog = Activator.CreateInstance(commonDialogType)!;

            dynamic? wiaImage = null;

            if (settings.ShowUI || device == null)
            {
                // Show scanner selection and scanning UI
                wiaImage = commonDialog.ShowAcquireImage(
                    WIA_DEVICE_TYPE_SCANNER,        // DeviceType
                    settings.ColorMode ? WIA_INTENT_IMAGE_TYPE_COLOR : WIA_INTENT_IMAGE_TYPE_GRAYSCALE, // Intent
                    0,                               // Bias
                    WIA_FORMAT_PNG,                  // FormatID
                    false,                           // AlwaysSelectDevice
                    true,                            // UseCommonUI
                    false                            // CancelError
                );
            }
            else
            {
                // Connect to specific device
                Type? deviceManagerType = Type.GetTypeFromProgID("WIA.DeviceManager");
                if (deviceManagerType == null) return null;

                dynamic deviceManager = Activator.CreateInstance(deviceManagerType)!;
                dynamic? wiaDevice = null;

                // Find and connect to the specified device
                foreach (dynamic deviceInfo in deviceManager.DeviceInfos)
                {
                    if (deviceInfo.DeviceID == device.DeviceId)
                    {
                        wiaDevice = deviceInfo.Connect();
                        break;
                    }
                }

                if (wiaDevice == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not connect to device: {device.DeviceId}");
                    return null;
                }

                // Get the first scan item
                dynamic? scanItem = null;
                foreach (dynamic item in wiaDevice.Items)
                {
                    scanItem = item;
                    break;
                }

                if (scanItem == null)
                {
                    System.Diagnostics.Debug.WriteLine("No scan items available on device");
                    return null;
                }

                // Set scan properties
                SetScanProperties(scanItem.Properties, settings);

                // Perform scan
                wiaImage = commonDialog.ShowTransfer(scanItem, WIA_FORMAT_PNG, false);
            }

            if (wiaImage == null)
            {
                System.Diagnostics.Debug.WriteLine("Scan was cancelled or failed");
                return null;
            }

            // Convert WIA image to BitmapSource
            return ConvertWiaImageToBitmapSource(wiaImage);
        }
        catch (COMException ex)
        {
            // Common error codes:
            // 0x80210006 = WIA_ERROR_OFFLINE (device offline)
            // 0x80210016 = WIA_ERROR_BUSY (device busy)
            // 0x80210001 = WIA_ERROR_GENERAL (general error)
            System.Diagnostics.Debug.WriteLine($"WIA scan error: {ex.Message} (0x{ex.ErrorCode:X8})");
            
            if (ex.ErrorCode == unchecked((int)0x80210006))
            {
                throw new InvalidOperationException("Scanner is offline or not connected.", ex);
            }
            else if (ex.ErrorCode == unchecked((int)0x80210016))
            {
                throw new InvalidOperationException("Scanner is busy. Please wait and try again.", ex);
            }
            
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scan error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Scan a document and return as file path
    /// </summary>
    public string? ScanToFile(ScannerDevice? device = null, ScanSettings? settings = null, string? outputPath = null)
    {
        var image = ScanDocument(device, settings);
        if (image == null) return null;

        // Generate output path if not specified
        if (string.IsNullOrEmpty(outputPath))
        {
            outputPath = Path.Combine(Path.GetTempPath(), $"scan_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        }

        // Save to file
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = new FileStream(outputPath, FileMode.Create);
        encoder.Save(stream);

        System.Diagnostics.Debug.WriteLine($"Scan saved to: {outputPath}");
        return outputPath;
    }

    /// <summary>
    /// Set scan properties on the WIA item
    /// </summary>
    private void SetScanProperties(dynamic properties, ScanSettings settings)
    {
        try
        {
            // Set resolution
            SetPropertyValue(properties, WIA_HORIZONTAL_RESOLUTION, settings.Dpi);
            SetPropertyValue(properties, WIA_VERTICAL_RESOLUTION, settings.Dpi);

            // Set color mode
            // 1 = Color, 2 = Grayscale, 4 = Black and White
            int colorMode = settings.ColorMode ? 1 : 2;
            SetPropertyValue(properties, WIA_COLOR_MODE, colorMode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not set scan properties: {ex.Message}");
            // Continue with default properties
        }
    }

    /// <summary>
    /// Get a property value from WIA properties collection
    /// </summary>
    private static string? GetPropertyValue(dynamic properties, string propertyName)
    {
        try
        {
            foreach (dynamic prop in properties)
            {
                if (prop.Name == propertyName)
                {
                    return prop.Value?.ToString();
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Set a property value in WIA properties collection
    /// </summary>
    private static void SetPropertyValue(dynamic properties, int propertyId, object value)
    {
        try
        {
            foreach (dynamic prop in properties)
            {
                if (prop.PropertyID == propertyId)
                {
                    prop.Value = value;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not set property {propertyId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert WIA Image to WPF BitmapSource
    /// </summary>
    private BitmapSource? ConvertWiaImageToBitmapSource(dynamic wiaImage)
    {
        string? tempFile = null;
        try
        {
            // Method 1: Try to save to temp file first (most reliable)
            tempFile = Path.Combine(Path.GetTempPath(), $"wia_scan_{Guid.NewGuid()}.png");
            
            dynamic imageFile = wiaImage;
            imageFile.SaveFile(tempFile);
            
            System.Diagnostics.Debug.WriteLine($"WIA image saved to temp file: {tempFile}");
            
            // Load from temp file
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(tempFile);
            bitmap.EndInit();
            bitmap.Freeze();
            
            System.Diagnostics.Debug.WriteLine($"Successfully loaded scanned image: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
            
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error with SaveFile method: {ex.Message}");
            
            // Method 2: Try binary data approach as fallback
            try
            {
                dynamic imageFile = wiaImage;
                dynamic fileData = imageFile.FileData;
                byte[] imageData = (byte[])fileData.get_BinaryData();
                
                System.Diagnostics.Debug.WriteLine($"Got binary data: {imageData.Length} bytes");

                using var stream = new MemoryStream(imageData);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded from binary: {bitmap.PixelWidth}x{bitmap.PixelHeight}");

                return bitmap;
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"Error with binary data method: {ex2.Message}");
                return null;
            }
        }
        finally
        {
            // Clean up temp file after loading (with delay to ensure file is released)
            if (tempFile != null && File.Exists(tempFile))
            {
                try
                {
                    Task.Delay(100).ContinueWith(_ =>
                    {
                        try { File.Delete(tempFile); }
                        catch { /* ignore cleanup errors */ }
                    });
                }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
