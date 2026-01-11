// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenJPDF.Models;

public partial class PageThumbnail : ObservableObject
{
    [ObservableProperty]
    private int pageNumber;

    [ObservableProperty]
    private int originalPageIndex;

    [ObservableProperty]
    private BitmapSource? thumbnail;

    [ObservableProperty]
    private int rotationAngle;

    [ObservableProperty]
    private bool isDragOver;

    [ObservableProperty]
    private bool isDragging;

    /// <summary>
    /// Indicates if this page is selected (for multi-select operations)
    /// </summary>
    [ObservableProperty]
    private bool isSelected;

    /// <summary>
    /// Updates the thumbnail image
    /// </summary>
    public void UpdateThumbnail(BitmapSource? newThumbnail)
    {
        Thumbnail = newThumbnail;
    }

    /// <summary>
    /// Updates the rotation angle (0, 90, 180, 270)
    /// </summary>
    public void UpdateRotation(int angle)
    {
        RotationAngle = angle % 360;
    }
}
