// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using CommunityToolkit.Mvvm.Input;
using OpenJPDF.Models;
using Cursors = System.Windows.Input.Cursors;

namespace OpenJPDF.ViewModels;

/// <summary>
/// MainViewModel partial class for Measurement Tool functionality
/// </summary>
public partial class MainViewModel
{
    #region Measurement Commands

    /// <summary>
    /// Enter calibration mode - draw reference line to set scale
    /// </summary>
    [RelayCommand]
    private void EnterCalibrateMode()
    {
        if (!IsFileLoaded) return;
        
        CurrentEditMode = EditMode.Calibrate;
        IsEditMode = true;
        EditModeMessage = "Draw a reference line on the document (click and drag)";
        EditCursor = Cursors.Cross;
        StatusMessage = "Calibration Mode: Draw a line with known length";
    }

    /// <summary>
    /// Enter distance measurement mode
    /// </summary>
    [RelayCommand]
    private void EnterMeasureDistanceMode()
    {
        if (!IsFileLoaded) return;
        
        if (!IsCalibrated)
        {
            StatusMessage = "Please calibrate first by drawing a reference line";
            EnterCalibrateMode();
            return;
        }
        
        CurrentEditMode = EditMode.MeasureDistance;
        IsEditMode = true;
        EditModeMessage = "Click and drag to measure distance";
        EditCursor = Cursors.Cross;
        StatusMessage = $"Distance Mode: Calibrated at {MeasurementCalibration.ScaleFactor:F4} {MeasurementCalibration.GetUnitAbbreviation()}/px";
    }

    /// <summary>
    /// Enter area measurement mode
    /// </summary>
    [RelayCommand]
    private void EnterMeasureAreaMode()
    {
        if (!IsFileLoaded) return;
        
        if (!IsCalibrated)
        {
            StatusMessage = "Please calibrate first by drawing a reference line";
            EnterCalibrateMode();
            return;
        }
        
        CurrentEditMode = EditMode.MeasureArea;
        IsEditMode = true;
        EditModeMessage = "Click points to draw polygon, right-click or double-click to close";
        EditCursor = Cursors.Cross;
        StatusMessage = $"Area Mode: Click to add points, right-click or double-click to finish";
    }

    /// <summary>
    /// Enter select measurement mode - for editing and deleting
    /// </summary>
    [RelayCommand]
    private void EnterSelectMeasurementMode()
    {
        if (!IsFileLoaded) return;
        
        CurrentEditMode = EditMode.SelectMeasurement;
        IsEditMode = true;
        EditModeMessage = "Click to select measurement, drag nodes to edit, Delete to remove";
        EditCursor = Cursors.Arrow;
        StatusMessage = "Select Mode: Click measurement to select, drag nodes to move";
    }

    /// <summary>
    /// Exit measurement mode
    /// </summary>
    [RelayCommand]
    private void ExitMeasurementMode()
    {
        if (IsMeasuring)
        {
            CurrentEditMode = EditMode.None;
            IsEditMode = false;
            EditModeMessage = "";
            EditCursor = Cursors.Arrow;
            StatusMessage = "Ready";
        }
    }

    /// <summary>
    /// Clear all measurements
    /// </summary>
    [RelayCommand]
    private void ClearMeasurements()
    {
        Measurements.Clear();
        RefreshMeasurementsRequested?.Invoke();
        StatusMessage = "All measurements cleared";
    }

    /// <summary>
    /// Delete selected measurement
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedMeasurement()
    {
        if (SelectedMeasurement != null)
        {
            Measurements.Remove(SelectedMeasurement);
            SelectedMeasurement = null;
            RefreshMeasurementsRequested?.Invoke();
            StatusMessage = "Measurement deleted";
        }
    }

    /// <summary>
    /// Reset calibration
    /// </summary>
    [RelayCommand]
    private void ResetCalibration()
    {
        MeasurementCalibration = new MeasurementCalibration();
        OnPropertyChanged(nameof(IsCalibrated));
        StatusMessage = "Calibration reset. Draw a new reference line to calibrate.";
    }

    #endregion

    #region Measurement Methods

    /// <summary>
    /// Called when calibration line is drawn - triggers calibration dialog
    /// </summary>
    /// <param name="pixelLength">Length of the reference line in pixels</param>
    public void OnCalibrationLineDrawn(double pixelLength)
    {
        if (pixelLength < 10)
        {
            StatusMessage = "Reference line too short. Please draw a longer line.";
            return;
        }
        
        MeasurementCalibration.ReferencePixelLength = pixelLength;
        ShowCalibrationDialogRequested?.Invoke(pixelLength);
    }

    /// <summary>
    /// Apply calibration from dialog
    /// </summary>
    public void ApplyCalibration(double realLength, MeasurementUnit unit)
    {
        MeasurementCalibration.ReferenceRealLength = realLength;
        MeasurementCalibration.Unit = unit;
        MeasurementCalibration.IsCalibrated = true;
        OnPropertyChanged(nameof(IsCalibrated));
        
        StatusMessage = $"Calibrated: {realLength} {MeasurementCalibration.GetUnitAbbreviation()} = {MeasurementCalibration.ReferencePixelLength:F0} pixels";
        
        // Switch to distance measurement mode after calibration
        EnterMeasureDistanceMode();
    }

    /// <summary>
    /// Add a line measurement
    /// </summary>
    public void AddLineMeasurement(double startX, double startY, double endX, double endY)
    {
        var measurement = new LineMeasurement
        {
            PageNumber = CurrentPageIndex,
            StartX = startX,
            StartY = startY,
            EndX = endX,
            EndY = endY
        };
        
        Measurements.Add(measurement);
        SelectedMeasurement = measurement;
        RefreshMeasurementsRequested?.Invoke();
        
        double distance = measurement.GetPixelLength();
        string formattedDistance = MeasurementCalibration.FormatDistance(distance);
        StatusMessage = $"Distance: {formattedDistance}";
    }

    /// <summary>
    /// Add an area measurement (polygon)
    /// </summary>
    public void AddAreaMeasurement(AreaMeasurement measurement)
    {
        measurement.PageNumber = CurrentPageIndex;
        measurement.Close();
        Measurements.Add(measurement);
        SelectedMeasurement = measurement;
        RefreshMeasurementsRequested?.Invoke();
        
        double area = measurement.GetPixelArea();
        string formattedArea = MeasurementCalibration.FormatArea(area);
        double perimeter = measurement.GetPixelLength();
        string formattedPerimeter = MeasurementCalibration.FormatDistance(perimeter);
        StatusMessage = $"Area: {formattedArea}, Perimeter: {formattedPerimeter}";
    }

    /// <summary>
    /// Get measurements for current page
    /// </summary>
    public IEnumerable<MeasurementAnnotation> GetCurrentPageMeasurements()
    {
        return Measurements.Where(m => m.PageNumber == CurrentPageIndex);
    }

    #endregion
}
