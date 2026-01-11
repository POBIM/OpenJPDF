# Tesseract OCR Trained Data Files

This folder contains the trained data files required for OCR functionality.

## Required Files

Download these files from the official Tesseract repository:
https://github.com/tesseract-ocr/tessdata

### Required for OpenJPDF:
- `eng.traineddata` (~23 MB) - English language
- `tha.traineddata` (~1.1 MB) - Thai language

### Download Links (Direct):
- [eng.traineddata](https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata)
- [tha.traineddata](https://github.com/tesseract-ocr/tessdata/raw/main/tha.traineddata)

## Installation

1. Download the `.traineddata` files listed above
2. Place them in this `tessdata` folder
3. Rebuild the application

## For Developers

The OpenJPDF.csproj is configured to copy these files to the output directory:

```xml
<ItemGroup>
  <None Update="tessdata\**">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

## Notes

- Files are not included in the repository due to size
- OCR will not work until these files are downloaded
- The application will show a message if tessdata is missing
