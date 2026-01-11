# OpenJPDF Installer

## Requirements

1. **Inno Setup 6** - Download from https://jrsoftware.org/isdl.php
2. **.NET 8 SDK** - For building the application
3. **Tesseract OCR Data Files** - For OCR functionality

## Build Installer

### Option 1: Use batch file (Recommended)
```
Double-click: build-installer.bat
```

### Option 2: Manual steps

1. Build Release:
```bash
cd OpenJPDF
dotnet publish -c Release -r win-x64 --self-contained true
```

2. Compile installer:
```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```

## Output

Installer will be created at:
```
installer\output\OpenJPDF-Setup-1.0.0.exe
```

## Installer Features

- **Self-contained**: No .NET runtime required on target machine
- **User install**: No admin rights required (installs to AppData)
- **Optional**: Desktop shortcut
- **Auto-kill**: Closes running instance before upgrade
- **Launch after install**: Option to start immediately
- **Version detection**: Automatically detects existing installation
- **Smart upgrade**: 
  - Older version → Prompts to upgrade
  - Same version → Prompts to reinstall
  - Newer version → Warns about downgrade
- **Multi-language**: Supports English and Thai

## OCR Setup (Required for text recognition)

Before building the installer, download Tesseract trained data files:

1. Download from https://github.com/tesseract-ocr/tessdata:
   - `eng.traineddata` (~23 MB) - English
   - `tha.traineddata` (~1.1 MB) - Thai

2. Place the `.traineddata` files in:
   ```
   OpenJPDF\tessdata\
   ```

3. The installer will automatically include these files.

**Note**: Without tessdata files, OCR functionality will not work on installed machines.

## Customization

Edit `setup.iss` to change:
- `MyAppVersion` - Version number
- `MyAppPublisher` - Publisher name
- `MyAppURL` - Website URL
- `AppId` - Unique GUID (generate new for forks)
