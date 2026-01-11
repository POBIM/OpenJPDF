// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Windows;

namespace OpenJPDF.Views;

public partial class ImageSizeDialog : Window
{
    public double ImageX { get; private set; } = 50;
    public double ImageY { get; private set; } = 50;
    public double ImageWidth { get; private set; } = 200;
    public double ImageHeight { get; private set; } = 200;

    public ImageSizeDialog()
    {
        InitializeComponent();
        XInput.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(XInput.Text, out double x))
            ImageX = x;
        if (double.TryParse(YInput.Text, out double y))
            ImageY = y;
        if (double.TryParse(WidthInput.Text, out double width))
            ImageWidth = width;
        if (double.TryParse(HeightInput.Text, out double height))
            ImageHeight = height;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
