// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenJPDF.Models;

/// <summary>
/// Represents a rendered page image for continuous scroll view
/// </summary>
public partial class PageImage : ObservableObject
{
    [ObservableProperty]
    private int pageIndex;

    [ObservableProperty]
    private int pageNumber;

    [ObservableProperty]
    private BitmapSource? image;

    public PageImage(int pageIndex, BitmapSource? image)
    {
        PageIndex = pageIndex;
        PageNumber = pageIndex + 1;
        Image = image;
    }

    public void UpdateImage(BitmapSource? newImage)
    {
        Image = newImage;
    }
}
