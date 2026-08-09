// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Windows.Media.Imaging;

namespace OpenJPDF.Helpers;

/// <summary>
/// Converts between PDF points, WPF device-independent pixels (DIPs), and
/// raster bitmap pixels. Editor annotations use unzoomed WPF DIPs.
/// </summary>
public static class PageCoordinateMapper
{
    public const double PointsToDips = 96.0 / 72.0;

    public static double PdfPointsToDips(double points, double zoomScale = 1.0)
        => points * PointsToDips * zoomScale;

    public static double DipsToPdfPoints(double dips, double zoomScale = 1.0)
        => zoomScale > 0 ? dips / (PointsToDips * zoomScale) : 0;

    public static System.Windows.Rect PdfRectangleToDisplayDips(
        double x,
        double y,
        double width,
        double height,
        double pageWidth,
        double pageHeight,
        int clockwiseRotation,
        double zoomScale = 1.0)
    {
        int rotation = NormalizeRotation(clockwiseRotation);
        double displayLeft;
        double displayBottom;
        double displayWidth;
        double displayHeight;
        double displayPageHeight;

        switch (rotation)
        {
            case 90:
                displayLeft = y;
                displayBottom = pageWidth - x - width;
                displayWidth = height;
                displayHeight = width;
                displayPageHeight = pageWidth;
                break;
            case 180:
                displayLeft = pageWidth - x - width;
                displayBottom = pageHeight - y - height;
                displayWidth = width;
                displayHeight = height;
                displayPageHeight = pageHeight;
                break;
            case 270:
                displayLeft = pageHeight - y - height;
                displayBottom = x;
                displayWidth = height;
                displayHeight = width;
                displayPageHeight = pageWidth;
                break;
            default:
                displayLeft = x;
                displayBottom = y;
                displayWidth = width;
                displayHeight = height;
                displayPageHeight = pageHeight;
                break;
        }

        double displayTop = displayPageHeight - displayBottom - displayHeight;
        return new System.Windows.Rect(
            PdfPointsToDips(displayLeft, zoomScale),
            PdfPointsToDips(displayTop, zoomScale),
            PdfPointsToDips(displayWidth, zoomScale),
            PdfPointsToDips(displayHeight, zoomScale));
    }

    public static (double X, double Y) DisplayDipsDeltaToPdfPoints(
        double deltaXDips,
        double deltaYDips,
        int clockwiseRotation,
        double zoomScale = 1.0)
    {
        double displayX = DipsToPdfPoints(deltaXDips, zoomScale);
        double displayY = -DipsToPdfPoints(deltaYDips, zoomScale);

        return NormalizeRotation(clockwiseRotation) switch
        {
            90 => (-displayY, displayX),
            180 => (-displayX, -displayY),
            270 => (displayY, -displayX),
            _ => (displayX, displayY)
        };
    }

    private static int NormalizeRotation(int rotation)
    {
        rotation %= 360;
        return rotation < 0 ? rotation + 360 : rotation;
    }

    public static double BitmapPixelsPerDipX(BitmapSource bitmap)
        => bitmap.Width > 0 ? bitmap.PixelWidth / bitmap.Width : 1.0;

    public static double BitmapPixelsPerDipY(BitmapSource bitmap)
        => bitmap.Height > 0 ? bitmap.PixelHeight / bitmap.Height : 1.0;

    public static System.Drawing.Rectangle DipsToBitmapRectangle(
        BitmapSource bitmap,
        double x,
        double y,
        double width,
        double height)
    {
        double scaleX = BitmapPixelsPerDipX(bitmap);
        double scaleY = BitmapPixelsPerDipY(bitmap);

        int left = Math.Clamp((int)Math.Floor(x * scaleX), 0, bitmap.PixelWidth);
        int top = Math.Clamp((int)Math.Floor(y * scaleY), 0, bitmap.PixelHeight);
        int right = Math.Clamp((int)Math.Ceiling((x + width) * scaleX), left, bitmap.PixelWidth);
        int bottom = Math.Clamp((int)Math.Ceiling((y + height) * scaleY), top, bitmap.PixelHeight);

        return new System.Drawing.Rectangle(left, top, right - left, bottom - top);
    }
}
