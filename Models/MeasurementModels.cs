// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenJPDF.Models;

/// <summary>
/// Unit types for measurement
/// </summary>
public enum MeasurementUnit
{
    Millimeters,
    Centimeters,
    Meters,
    Inches,
    Feet,
    Points,
    Pixels
}

/// <summary>
/// Calibration data for accurate measurements
/// Stores the relationship between screen/PDF units and real-world units
/// </summary>
public partial class MeasurementCalibration : ObservableObject
{
    /// <summary>
    /// The reference line length in screen pixels
    /// </summary>
    [ObservableProperty]
    private double referencePixelLength;
    
    /// <summary>
    /// The actual real-world length
    /// </summary>
    [ObservableProperty]
    private double referenceRealLength = 1.0;
    
    /// <summary>
    /// The unit of measurement
    /// </summary>
    [ObservableProperty]
    private MeasurementUnit unit = MeasurementUnit.Centimeters;
    
    /// <summary>
    /// Whether calibration has been set
    /// </summary>
    [ObservableProperty]
    private bool isCalibrated;
    
    /// <summary>
    /// Scale factor: real units per pixel
    /// </summary>
    public double ScaleFactor => ReferencePixelLength > 0 ? ReferenceRealLength / ReferencePixelLength : 1.0;
    
    /// <summary>
    /// Convert pixel distance to real-world distance
    /// </summary>
    public double PixelsToReal(double pixels)
    {
        return pixels * ScaleFactor;
    }
    
    /// <summary>
    /// Convert real-world distance to pixels
    /// </summary>
    public double RealToPixels(double realDistance)
    {
        return ScaleFactor > 0 ? realDistance / ScaleFactor : realDistance;
    }
    
    /// <summary>
    /// Convert pixel area to real-world area
    /// </summary>
    public double PixelAreaToReal(double pixelArea)
    {
        return pixelArea * ScaleFactor * ScaleFactor;
    }
    
    /// <summary>
    /// Get unit abbreviation
    /// </summary>
    public string GetUnitAbbreviation()
    {
        return Unit switch
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
    
    /// <summary>
    /// Get area unit abbreviation (squared)
    /// </summary>
    public string GetAreaUnitAbbreviation()
    {
        return Unit switch
        {
            MeasurementUnit.Millimeters => "mm²",
            MeasurementUnit.Centimeters => "cm²",
            MeasurementUnit.Meters => "m²",
            MeasurementUnit.Inches => "in²",
            MeasurementUnit.Feet => "ft²",
            MeasurementUnit.Points => "pt²",
            MeasurementUnit.Pixels => "px²",
            _ => ""
        };
    }
    
    /// <summary>
    /// Format a distance value with unit
    /// </summary>
    public string FormatDistance(double pixelDistance)
    {
        double realDistance = PixelsToReal(pixelDistance);
        return $"{realDistance:F2} {GetUnitAbbreviation()}";
    }
    
    /// <summary>
    /// Format an area value with unit
    /// </summary>
    public string FormatArea(double pixelArea)
    {
        double realArea = PixelAreaToReal(pixelArea);
        return $"{realArea:F2} {GetAreaUnitAbbreviation()}";
    }
}

/// <summary>
/// A point in measurement coordinates
/// </summary>
public struct MeasurementPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    
    public MeasurementPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
    
    public double DistanceTo(MeasurementPoint other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Base class for measurement annotations
/// </summary>
public abstract partial class MeasurementAnnotation : ObservableObject
{
    [ObservableProperty]
    private int pageNumber;
    
    [ObservableProperty]
    private string label = "";
    
    [ObservableProperty]
    private string color = "#FF0000"; // Red by default
    
    [ObservableProperty]
    private double strokeWidth = 2.0;
    
    [ObservableProperty]
    private bool showLabel = true;
    
    /// <summary>
    /// Get the pixel length/perimeter of this measurement
    /// </summary>
    public abstract double GetPixelLength();
    
    /// <summary>
    /// Get the pixel area of this measurement (0 for lines)
    /// </summary>
    public abstract double GetPixelArea();
    
    /// <summary>
    /// Get display name for sidebar
    /// </summary>
    public abstract string DisplayName { get; }
    
    /// <summary>
    /// Icon for sidebar
    /// </summary>
    public abstract string IconEmoji { get; }
}

/// <summary>
/// Line measurement - measures distance between two points
/// </summary>
public partial class LineMeasurement : MeasurementAnnotation
{
    [ObservableProperty]
    private double startX;
    
    [ObservableProperty]
    private double startY;
    
    [ObservableProperty]
    private double endX;
    
    [ObservableProperty]
    private double endY;
    
    public override double GetPixelLength()
    {
        double dx = EndX - StartX;
        double dy = EndY - StartY;
        return Math.Sqrt(dx * dx + dy * dy);
    }
    
    public override double GetPixelArea() => 0;
    
    public override string DisplayName => string.IsNullOrEmpty(Label) ? "Distance" : Label;
    public override string IconEmoji => "📏";
    
    /// <summary>
    /// Get the midpoint for label placement
    /// </summary>
    public MeasurementPoint GetMidpoint()
    {
        return new MeasurementPoint((StartX + EndX) / 2, (StartY + EndY) / 2);
    }
    
    /// <summary>
    /// Get the angle of the line in degrees
    /// </summary>
    public double GetAngle()
    {
        return Math.Atan2(EndY - StartY, EndX - StartX) * 180 / Math.PI;
    }
}

/// <summary>
/// Area measurement - measures area of a polygon
/// </summary>
public partial class AreaMeasurement : MeasurementAnnotation
{
    [ObservableProperty]
    private ObservableCollection<MeasurementPoint> points = new();
    
    [ObservableProperty]
    private bool isClosed;
    
    public override double GetPixelLength()
    {
        if (Points.Count < 2) return 0;
        
        double length = 0;
        for (int i = 0; i < Points.Count - 1; i++)
        {
            length += Points[i].DistanceTo(Points[i + 1]);
        }
        
        // Add closing segment if closed
        if (IsClosed && Points.Count > 2)
        {
            length += Points[Points.Count - 1].DistanceTo(Points[0]);
        }
        
        return length;
    }
    
    public override double GetPixelArea()
    {
        if (Points.Count < 3 || !IsClosed) return 0;
        
        // Shoelace formula for polygon area
        double area = 0;
        int n = Points.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += Points[i].X * Points[j].Y;
            area -= Points[j].X * Points[i].Y;
        }
        
        return Math.Abs(area) / 2;
    }
    
    public override string DisplayName => string.IsNullOrEmpty(Label) ? "Area" : Label;
    public override string IconEmoji => "📐";
    
    /// <summary>
    /// Get the centroid for label placement
    /// </summary>
    public MeasurementPoint GetCentroid()
    {
        if (Points.Count == 0) return new MeasurementPoint(0, 0);
        
        double cx = 0, cy = 0;
        foreach (var p in Points)
        {
            cx += p.X;
            cy += p.Y;
        }
        return new MeasurementPoint(cx / Points.Count, cy / Points.Count);
    }
    
    /// <summary>
    /// Add a point to the polygon
    /// </summary>
    public void AddPoint(double x, double y)
    {
        Points.Add(new MeasurementPoint(x, y));
    }
    
    /// <summary>
    /// Close the polygon
    /// </summary>
    public void Close()
    {
        if (Points.Count >= 3)
        {
            IsClosed = true;
        }
    }
}
