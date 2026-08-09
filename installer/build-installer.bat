@echo off
echo ========================================
echo   OpenJPDF Installer Builder
echo ========================================
echo.

:: Check for Inno Setup
set ISCC=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set ISCC="C:\Program Files\Inno Setup 6\ISCC.exe"
) else (
    echo ERROR: Inno Setup 6 not found!
    echo.
    echo Please download and install from:
    echo https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo Found Inno Setup: %ISCC%
echo.

:: Build Release
echo Step 1: Building Release...
cd /d "%~dp0.."
for /f %%v in ('powershell -NoProfile -Command "([xml](Get-Content OpenJPDF.csproj)).Project.PropertyGroup.Version"') do set APP_VERSION=%%v
if not defined APP_VERSION (
    echo ERROR: Could not read application version from OpenJPDF.csproj
    exit /b 1
)

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
    echo ERROR: Build failed!
    exit /b 1
)
echo Build complete!
echo.

:: Build Installer
echo Step 2: Creating installer...
cd /d "%~dp0"
%ISCC% /DMyAppVersion=%APP_VERSION% /DPublishDir=..\publish setup.iss
if errorlevel 1 (
    echo ERROR: Installer creation failed!
    exit /b 1
)

echo.
echo ========================================
echo   SUCCESS!
echo ========================================
echo.
echo Installer created at:
echo %~dp0output\OpenJPDF-Setup-%APP_VERSION%.exe
echo.
