# AGENTS.md - OpenJPDF Development Guide

OpenJPDF is a Thai-language PDF editor built with .NET 8.0 and WPF. Licensed under AGPL-3.0.

## Build Commands

```bash
# Restore dependencies (downloads ONNX model on first run)
dotnet restore

# Build in Debug mode
dotnet build

# Build in Release mode
dotnet build --configuration Release

# Run the application
dotnet run

# Publish self-contained executable (Windows x64)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

## Project Structure

```
OpenJPDF/
├── Models/              # Data models (DocumentTab, Annotations, etc.)
├── ViewModels/          # MVVM ViewModels (partial classes for organization)
├── Views/               # XAML views and code-behind
├── Services/            # Business logic (PdfService, OcrService, etc.)
├── Helpers/             # Value converters and utilities
├── Styles/              # XAML resource dictionaries
├── Assets/              # Icons and images
├── Fonts/               # Bundled Thai fonts (TH Sarabun, Noto Sans Thai)
├── tessdata/            # Tesseract OCR language data
├── onnx-models/         # Background removal AI model (auto-downloaded)
└── installer/           # Inno Setup installer scripts
```

## Code Style Guidelines

### File Headers (Required)
Every `.cs` file must start with the SPDX license header:

```csharp
// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.
```

### Namespace & Using Statements

```csharp
// Use file-scoped namespaces
namespace OpenJPDF.Services;

// Use type aliases to avoid conflicts
using IoPath = System.IO.Path;
using IoFile = System.IO.File;
using ITextRectangle = iText.Kernel.Geom.Rectangle;
```

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `PdfService`, `MainViewModel` |
| Interfaces | IPascalCase | `IPdfService` |
| Methods | PascalCase | `LoadPdfAsync()`, `GetPageImage()` |
| Properties | PascalCase | `CurrentPageIndex`, `IsFileLoaded` |
| Private fields | _camelCase | `_pdfService`, `_pageCount` |
| Local variables | camelCase | `pageNumber`, `tempFile` |
| Constants | PascalCase | `ScreenToPdf` |
| Async methods | Suffix with Async | `SaveAsync()`, `LoadPdfAsync()` |

### MVVM with CommunityToolkit.Mvvm

```csharp
// Use [ObservableProperty] attribute for auto-generated properties
[ObservableProperty]
private bool isFileLoaded;

// Use [RelayCommand] for commands
[RelayCommand]
private void OpenFile() { }

[RelayCommand(CanExecute = nameof(CanSave))]
private async Task SaveAsync() { }

// Use partial methods for property change handlers
partial void OnCurrentPageIndexChanged(int value)
{
    // React to property changes
}
```

### Null Handling

```csharp
// Use nullable reference types (Nullable enabled in .csproj)
private string? _currentFilePath;
public BitmapSource? CurrentPageImage { get; set; }

// Prefer pattern matching for null checks
if (annotation is null) return;
if (value is bool boolValue) { }

// Use null-conditional and null-coalescing operators
var result = service?.GetResult() ?? defaultValue;
```

### Error Handling

```csharp
try
{
    // Operation
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Error description: {ex.Message}");
    return false;  // Or appropriate fallback
}
```

### XML Documentation

```csharp
/// <summary>
/// Brief description of what the method does.
/// </summary>
/// <param name="pageNumber">0-based page index</param>
/// <returns>The rendered page image or null if failed</returns>
public BitmapSource? GetPageImage(int pageNumber, float scale = 1.0f)
```

### Partial Classes for Large ViewModels

Split large ViewModels into logical partial classes:

```csharp
// MainViewModel.cs - Core properties, constructor, dispose
// MainViewModel.Documents.cs - Multi-document tab management
// MainViewModel.Pages.cs - Page navigation, thumbnails
// MainViewModel.FileOperations.cs - Open, Save, Load
// MainViewModel.Annotations.cs - Text, Image, Shape annotations
// MainViewModel.PdfTools.cs - Merge, Split, Extract
```

### Resource Management

```csharp
// Implement IDisposable for classes holding resources
public class PdfService : IPdfService, IDisposable
{
    private bool _disposed;
    
    public void Dispose()
    {
        if (!_disposed)
        {
            // Cleanup resources
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
```

### Async Patterns

```csharp
// Wrap CPU-bound work in Task.Run
public async Task<bool> LoadPdfAsync(string filePath)
{
    return await Task.Run(() =>
    {
        // CPU-bound PDF processing
    });
}
```

### WPF Bitmap Handling

```csharp
// Always Freeze bitmaps for thread safety
bitmap.Freeze();

// Use WriteableBitmap for direct pixel manipulation
var writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
writeableBitmap.Lock();
try
{
    // Pixel operations
}
finally
{
    writeableBitmap.Unlock();
}
writeableBitmap.Freeze();
```

## Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| iText7 | 8.0.2 | PDF manipulation (AGPL license) |
| PDFtoImage | 4.1.1 | PDF page rendering |
| CommunityToolkit.Mvvm | 8.2.2 | MVVM framework |
| Tesseract | 5.2.0 | OCR for Thai/English |
| Microsoft.ML.OnnxRuntime | 1.18.0 | AI background removal |
| SixLabors.ImageSharp | 3.1.6 | Image processing |

## CI/CD Notes

- GitHub Actions workflow at `.github/workflows/build-release.yml`
- Builds trigger on push to `main` and on version tags (`v*`)
- Releases created automatically when pushing version tags
- Self-contained Windows x64 executable published

## Important Notes

1. **Thai Font Support**: Bundled fonts in `Fonts/` ensure Thai text renders correctly
2. **ONNX Model**: 168MB model auto-downloads on first build (see .csproj target)
3. **Coordinate Systems**: PDF uses 72 DPI (points), WPF uses 96 DPI (pixels)
   - Conversion factor: `SCREEN_TO_PDF = 72f / 96f` (0.75)
4. **iText7 License**: AGPL requires source disclosure for derivative works
