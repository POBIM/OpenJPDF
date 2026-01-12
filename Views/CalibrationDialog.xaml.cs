// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using OpenJPDF.Models;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace OpenJPDF.Views;

/// <summary>
/// Calibration dialog for setting measurement scale
/// </summary>
public partial class CalibrationDialog : Window
{
    private readonly double _pixelLength;

    /// <summary>
    /// The real-world length entered by user
    /// </summary>
    public double RealLength { get; private set; }

    /// <summary>
    /// The selected measurement unit
    /// </summary>
    public MeasurementUnit SelectedUnit { get; private set; }

    /// <summary>
    /// Whether calibration was applied
    /// </summary>
    public bool IsApplied { get; private set; }

    public CalibrationDialog(double pixelLength)
    {
        InitializeComponent();
        _pixelLength = pixelLength;
        
        // Set initial display
        PixelLengthText.Text = $"{pixelLength:F1} pixels";
        
        // Subscribe to input changes for live preview
        RealLengthInput.TextChanged += UpdateScalePreview;
        UnitComboBox.SelectionChanged += UpdateScalePreview;
        
        // Initial preview update
        UpdateScalePreview(null, null);
    }

    private void UpdateScalePreview(object? sender, EventArgs? e)
    {
        if (double.TryParse(RealLengthInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double realLength) && realLength > 0)
        {
            var selectedItem = UnitComboBox.SelectedItem as WpfComboBoxItem;
            var unit = selectedItem?.Tag is MeasurementUnit u ? u : MeasurementUnit.Centimeters;
            
            double scaleFactor = realLength / _pixelLength;
            string unitAbbrev = GetUnitAbbreviation(unit);
            
            ScalePreviewText.Text = $"1 pixel = {scaleFactor:F4} {unitAbbrev}";
        }
        else
        {
            ScalePreviewText.Text = "Enter a valid length";
        }
    }

    private static string GetUnitAbbreviation(MeasurementUnit unit)
    {
        return unit switch
        {
            MeasurementUnit.Millimeters => "mm",
            MeasurementUnit.Centimeters => "cm",
            MeasurementUnit.Meters => "m",
            MeasurementUnit.Inches => "in",
            MeasurementUnit.Feet => "ft",
            MeasurementUnit.Points => "pt",
            MeasurementUnit.Pixels => "px",
            _ => ""
        };
    }

    private void NumberValidation(object sender, TextCompositionEventArgs e)
    {
        // Allow digits, decimal point, and backspace
        var regex = new Regex(@"^[0-9]*\.?[0-9]*$");
        var textBox = sender as WpfTextBox;
        var newText = textBox?.Text.Insert(textBox.SelectionStart, e.Text) ?? e.Text;
        e.Handled = !regex.IsMatch(newText);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(RealLengthInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double realLength) && realLength > 0)
        {
            RealLength = realLength;
            
            var selectedItem = UnitComboBox.SelectedItem as WpfComboBoxItem;
            SelectedUnit = selectedItem?.Tag is MeasurementUnit u ? u : MeasurementUnit.Centimeters;
            
            IsApplied = true;
            DialogResult = true;
            Close();
        }
        else
        {
            System.Windows.MessageBox.Show("Please enter a valid positive number for the length.", "Invalid Input", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
