// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenJPDF.Models;
using OpenJPDF.Services;
using OpenJPDF.ViewModels;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfPoint = System.Windows.Point;
using WpfBrush = System.Windows.Media.Brush;
using WpfCursor = System.Windows.Input.Cursor;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPanel = System.Windows.Controls.Panel;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace OpenJPDF.Views;

/// <summary>
/// Enum representing the 8 resize handle positions plus rotation handle
/// </summary>
public enum ResizeHandlePosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    Rotation
}

/// <summary>
/// Manages resize handles for annotation elements on a canvas
/// </summary>
public class ResizeHandlesManager
{
    private readonly Canvas _canvas;
    private readonly MainViewModel _viewModel;
    private readonly List<WpfRectangle> _handles = new();
    private readonly Action _refreshCallback;
    
    private FrameworkElement? _targetElement;
    private AnnotationItem? _targetAnnotation;
    private ResizeHandlePosition _activeHandle;
    private bool _isResizing;
    private WpfPoint _resizeStartPoint;
    private double _originalX, _originalY, _originalWidth, _originalHeight;
    
    // Rotation handle state
    private Ellipse? _rotationHandle;
    private Line? _rotationLine;
    private bool _isRotating;
    private double _originalRotation;
    private WpfPoint _rotationCenter;
    private const double RotationHandleDistance = 30; // Distance above element
    private const double RotationHandleSize = 12;
    
    private const double HandleSize = 8;
    private const double HalfHandleSize = HandleSize / 2;
    
    private static readonly WpfBrush HandleFillBrush = new SolidColorBrush(WpfColor.FromRgb(33, 150, 243)); // Blue
    private static readonly WpfBrush HandleStrokeBrush = WpfBrushes.White;
    private static readonly WpfBrush CropHandleFillBrush = new SolidColorBrush(WpfColor.FromRgb(255, 152, 0)); // Orange
    private static readonly WpfBrush CropOverlayBrush = new SolidColorBrush(WpfColor.FromArgb(128, 0, 0, 0)); // Semi-transparent black
    
    // Crop mode state
    private bool _isCropMode;
    private ImageAnnotationItem? _cropTargetAnnotation;
    private WpfRectangle? _cropOverlayTop;
    private WpfRectangle? _cropOverlayBottom;
    private WpfRectangle? _cropOverlayLeft;
    private WpfRectangle? _cropOverlayRight;
    private WpfRectangle? _cropSelectionBorder;
    private StackPanel? _cropButtonPanel;
    private double _cropLeft, _cropTop, _cropWidth, _cropHeight;
    private double _elementLeft, _elementTop, _elementWidth, _elementHeight;
    
    public ResizeHandlesManager(Canvas canvas, MainViewModel viewModel, Action refreshCallback)
    {
        _canvas = canvas;
        _viewModel = viewModel;
        _refreshCallback = refreshCallback;
    }
    
    /// <summary>
    /// Show resize handles around a selected element
    /// </summary>
    public void ShowHandles(FrameworkElement element, AnnotationItem annotation)
    {
        HideHandles();
        
        _targetElement = element;
        _targetAnnotation = annotation;
        
        double left = Canvas.GetLeft(element);
        double top = Canvas.GetTop(element);
        double width = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
        double height = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
        
        // For text annotations, show only rotation handle (no resize)
        if (annotation is TextAnnotationItem textAnn)
        {
            if (double.IsNaN(width) || width <= 0) width = textAnn.Width * _viewModel.ZoomScale;
            if (double.IsNaN(height) || height <= 0) height = textAnn.Height * _viewModel.ZoomScale;
            if (width <= 0) width = 100;
            if (height <= 0) height = 30;
            
            // Only create rotation handle for text (no resize handles)
            CreateRotationHandle(left, top, width, height);
            return;
        }
        
        // Only show resize handles for resizable annotations (Image and Shape)
        if (annotation is not ImageAnnotationItem && annotation is not ShapeAnnotationItem)
        {
            return;
        }
        
        if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0)
        {
            // Try to get size from annotation
            if (annotation is ImageAnnotationItem imgAnn)
            {
                width = imgAnn.Width * _viewModel.ZoomScale;
                height = imgAnn.Height * _viewModel.ZoomScale;
            }
            else if (annotation is ShapeAnnotationItem shapeAnn)
            {
                width = shapeAnn.Width * _viewModel.ZoomScale;
                height = shapeAnn.Height * _viewModel.ZoomScale;
            }
            else
            {
                return;
            }
        }
        
        // Create 8 resize handles
        CreateHandle(ResizeHandlePosition.TopLeft, left - HalfHandleSize, top - HalfHandleSize, WpfCursors.SizeNWSE);
        CreateHandle(ResizeHandlePosition.TopCenter, left + width / 2 - HalfHandleSize, top - HalfHandleSize, WpfCursors.SizeNS);
        CreateHandle(ResizeHandlePosition.TopRight, left + width - HalfHandleSize, top - HalfHandleSize, WpfCursors.SizeNESW);
        CreateHandle(ResizeHandlePosition.MiddleLeft, left - HalfHandleSize, top + height / 2 - HalfHandleSize, WpfCursors.SizeWE);
        CreateHandle(ResizeHandlePosition.MiddleRight, left + width - HalfHandleSize, top + height / 2 - HalfHandleSize, WpfCursors.SizeWE);
        CreateHandle(ResizeHandlePosition.BottomLeft, left - HalfHandleSize, top + height - HalfHandleSize, WpfCursors.SizeNESW);
        CreateHandle(ResizeHandlePosition.BottomCenter, left + width / 2 - HalfHandleSize, top + height - HalfHandleSize, WpfCursors.SizeNS);
        CreateHandle(ResizeHandlePosition.BottomRight, left + width - HalfHandleSize, top + height - HalfHandleSize, WpfCursors.SizeNWSE);
        
        // Create rotation handle (circle above top-center)
        CreateRotationHandle(left, top, width, height);
    }
    
    /// <summary>
    /// Hide all resize handles
    /// </summary>
    public void HideHandles()
    {
        CancelCropMode();
        foreach (var handle in _handles)
        {
            _canvas.Children.Remove(handle);
        }
        _handles.Clear();
        
        // Remove rotation handle
        if (_rotationHandle != null)
        {
            _canvas.Children.Remove(_rotationHandle);
            _rotationHandle = null;
        }
        if (_rotationLine != null)
        {
            _canvas.Children.Remove(_rotationLine);
            _rotationLine = null;
        }
        
        _targetElement = null;
        _targetAnnotation = null;
    }
    
    /// <summary>
    /// Check if currently in crop mode
    /// </summary>
    public bool IsCropMode => _isCropMode;
    
    /// <summary>
    /// Enter crop mode for an image annotation
    /// </summary>
    public void EnterCropMode(FrameworkElement element, ImageAnnotationItem imageAnnotation)
    {
        if (_isCropMode) CancelCropMode();
        
        _isCropMode = true;
        _cropTargetAnnotation = imageAnnotation;
        _targetElement = element;
        _targetAnnotation = imageAnnotation;
        
        // Store element bounds
        _elementLeft = Canvas.GetLeft(element);
        _elementTop = Canvas.GetTop(element);
        _elementWidth = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
        _elementHeight = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
        
        if (double.IsNaN(_elementWidth) || double.IsNaN(_elementHeight))
        {
            _elementWidth = imageAnnotation.Width * _viewModel.ZoomScale;
            _elementHeight = imageAnnotation.Height * _viewModel.ZoomScale;
        }
        
        // Initialize crop area to full image
        _cropLeft = _elementLeft;
        _cropTop = _elementTop;
        _cropWidth = _elementWidth;
        _cropHeight = _elementHeight;
        
        // Create crop overlay visuals (darken outside crop area)
        CreateCropOverlays();
        
        // Create Apply/Cancel buttons
        CreateCropButtons();
        
        // Change handle colors to orange for crop mode
        foreach (var handle in _handles)
        {
            handle.Fill = CropHandleFillBrush;
        }
        
        UpdateCropOverlays();
        UpdateCropButtonPosition();
        _viewModel.StatusMessage = "Crop mode: Drag handles to adjust. Click Apply or press Enter to confirm.";
    }
    
    /// <summary>
    /// Cancel crop mode without applying changes
    /// </summary>
    public void CancelCropMode()
    {
        if (!_isCropMode) return;
        
        _isCropMode = false;
        _cropTargetAnnotation = null;
        
        // Remove crop overlays
        RemoveCropOverlays();
        
        // Restore handle colors to blue
        foreach (var handle in _handles)
        {
            handle.Fill = HandleFillBrush;
        }
        
        _viewModel.StatusMessage = "Crop cancelled.";
    }
    
    private void CreateCropOverlays()
    {
        RemoveCropOverlays();
        
        _cropOverlayTop = new WpfRectangle { Fill = CropOverlayBrush, IsHitTestVisible = false };
        _cropOverlayBottom = new WpfRectangle { Fill = CropOverlayBrush, IsHitTestVisible = false };
        _cropOverlayLeft = new WpfRectangle { Fill = CropOverlayBrush, IsHitTestVisible = false };
        _cropOverlayRight = new WpfRectangle { Fill = CropOverlayBrush, IsHitTestVisible = false };
        
        _cropSelectionBorder = new WpfRectangle
        {
            Fill = WpfBrushes.Transparent,
            Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 152, 0)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            IsHitTestVisible = false
        };
        
        WpfPanel.SetZIndex(_cropOverlayTop, 998);
        WpfPanel.SetZIndex(_cropOverlayBottom, 998);
        WpfPanel.SetZIndex(_cropOverlayLeft, 998);
        WpfPanel.SetZIndex(_cropOverlayRight, 998);
        WpfPanel.SetZIndex(_cropSelectionBorder, 999);
        
        _canvas.Children.Add(_cropOverlayTop);
        _canvas.Children.Add(_cropOverlayBottom);
        _canvas.Children.Add(_cropOverlayLeft);
        _canvas.Children.Add(_cropOverlayRight);
        _canvas.Children.Add(_cropSelectionBorder);
    }
    
    private void RemoveCropOverlays()
    {
        if (_cropOverlayTop != null) { _canvas.Children.Remove(_cropOverlayTop); _cropOverlayTop = null; }
        if (_cropOverlayBottom != null) { _canvas.Children.Remove(_cropOverlayBottom); _cropOverlayBottom = null; }
        if (_cropOverlayLeft != null) { _canvas.Children.Remove(_cropOverlayLeft); _cropOverlayLeft = null; }
        if (_cropOverlayRight != null) { _canvas.Children.Remove(_cropOverlayRight); _cropOverlayRight = null; }
        if (_cropSelectionBorder != null) { _canvas.Children.Remove(_cropSelectionBorder); _cropSelectionBorder = null; }
        if (_cropButtonPanel != null) { _canvas.Children.Remove(_cropButtonPanel); _cropButtonPanel = null; }
    }
    
    private void CreateCropButtons()
    {
        RemoveCropButtons();
        
        _cropButtonPanel = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Background = new SolidColorBrush(WpfColor.FromArgb(220, 30, 30, 30)),
            Margin = new Thickness(0)
        };
        
        var applyButton = new WpfButton
        {
            Content = "Apply",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(4),
            Background = new SolidColorBrush(WpfColor.FromRgb(255, 152, 0)),
            Foreground = WpfBrushes.White,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand
        };
        applyButton.Click += (s, e) => ApplyCrop();
        
        var cancelButton = new WpfButton
        {
            Content = "Cancel",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(4),
            Background = new SolidColorBrush(WpfColor.FromRgb(80, 80, 80)),
            Foreground = WpfBrushes.White,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand
        };
        cancelButton.Click += (s, e) => 
        {
            CancelCropMode();
            _refreshCallback?.Invoke();
        };
        
        _cropButtonPanel.Children.Add(applyButton);
        _cropButtonPanel.Children.Add(cancelButton);
        
        WpfPanel.SetZIndex(_cropButtonPanel, 1001);
        _canvas.Children.Add(_cropButtonPanel);
    }
    
    private void RemoveCropButtons()
    {
        if (_cropButtonPanel != null)
        {
            _canvas.Children.Remove(_cropButtonPanel);
            _cropButtonPanel = null;
        }
    }
    
    private void UpdateCropButtonPosition()
    {
        if (_cropButtonPanel == null) return;
        
        // Position buttons below the crop area
        double buttonX = _cropLeft + (_cropWidth / 2) - 60; // Center horizontally
        double buttonY = _cropTop + _cropHeight + 8; // Below crop area
        
        Canvas.SetLeft(_cropButtonPanel, buttonX);
        Canvas.SetTop(_cropButtonPanel, buttonY);
    }
    
    private void UpdateCropOverlays()
    {
        if (_cropOverlayTop == null || _cropOverlayBottom == null || 
            _cropOverlayLeft == null || _cropOverlayRight == null ||
            _cropSelectionBorder == null) return;
        
        // Top overlay: from element top to crop top
        Canvas.SetLeft(_cropOverlayTop, _elementLeft);
        Canvas.SetTop(_cropOverlayTop, _elementTop);
        _cropOverlayTop.Width = Math.Max(0, _elementWidth);
        _cropOverlayTop.Height = Math.Max(0, _cropTop - _elementTop);
        
        // Bottom overlay: from crop bottom to element bottom
        double cropBottom = _cropTop + _cropHeight;
        double elementBottom = _elementTop + _elementHeight;
        Canvas.SetLeft(_cropOverlayBottom, _elementLeft);
        Canvas.SetTop(_cropOverlayBottom, cropBottom);
        _cropOverlayBottom.Width = Math.Max(0, _elementWidth);
        _cropOverlayBottom.Height = Math.Max(0, elementBottom - cropBottom);
        
        // Left overlay: from element left to crop left (within crop vertical bounds)
        Canvas.SetLeft(_cropOverlayLeft, _elementLeft);
        Canvas.SetTop(_cropOverlayLeft, _cropTop);
        _cropOverlayLeft.Width = Math.Max(0, _cropLeft - _elementLeft);
        _cropOverlayLeft.Height = Math.Max(0, _cropHeight);
        
        // Right overlay: from crop right to element right (within crop vertical bounds)
        double cropRight = _cropLeft + _cropWidth;
        double elementRight = _elementLeft + _elementWidth;
        Canvas.SetLeft(_cropOverlayRight, cropRight);
        Canvas.SetTop(_cropOverlayRight, _cropTop);
        _cropOverlayRight.Width = Math.Max(0, elementRight - cropRight);
        _cropOverlayRight.Height = Math.Max(0, _cropHeight);
        
        // Crop selection border
        Canvas.SetLeft(_cropSelectionBorder, _cropLeft);
        Canvas.SetTop(_cropSelectionBorder, _cropTop);
        _cropSelectionBorder.Width = Math.Max(0, _cropWidth);
        _cropSelectionBorder.Height = Math.Max(0, _cropHeight);
    }
    
    private void CreateHandle(ResizeHandlePosition position, double x, double y, WpfCursor cursor)
    {
        var handle = new WpfRectangle
        {
            Width = HandleSize,
            Height = HandleSize,
            Fill = HandleFillBrush,
            Stroke = HandleStrokeBrush,
            StrokeThickness = 1,
            Cursor = cursor,
            Tag = position
        };
        
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
        WpfPanel.SetZIndex(handle, 1000); // Ensure handles are on top
        
        handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
        handle.MouseMove += Handle_MouseMove;
        handle.MouseLeftButtonUp += Handle_MouseLeftButtonUp;
        
        _canvas.Children.Add(handle);
        _handles.Add(handle);
    }
    
    /// <summary>
    /// Create rotation handle (circle) above the element with connecting line
    /// </summary>
    private void CreateRotationHandle(double left, double top, double width, double height)
    {
        // Remove existing rotation handle if any
        if (_rotationHandle != null)
        {
            _canvas.Children.Remove(_rotationHandle);
            _rotationHandle = null;
        }
        if (_rotationLine != null)
        {
            _canvas.Children.Remove(_rotationLine);
            _rotationLine = null;
        }
        
        double centerX = left + width / 2;
        double handleY = top - RotationHandleDistance;
        
        // Create connecting line from top-center to rotation handle
        _rotationLine = new Line
        {
            X1 = centerX,
            Y1 = top,
            X2 = centerX,
            Y2 = handleY + RotationHandleSize / 2,
            Stroke = HandleFillBrush,
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        WpfPanel.SetZIndex(_rotationLine, 999);
        _canvas.Children.Add(_rotationLine);
        
        // Create rotation handle (circle)
        _rotationHandle = new Ellipse
        {
            Width = RotationHandleSize,
            Height = RotationHandleSize,
            Fill = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80)), // Green for rotation
            Stroke = HandleStrokeBrush,
            StrokeThickness = 1.5,
            Cursor = WpfCursors.Hand,
            Tag = ResizeHandlePosition.Rotation
        };
        
        Canvas.SetLeft(_rotationHandle, centerX - RotationHandleSize / 2);
        Canvas.SetTop(_rotationHandle, handleY);
        WpfPanel.SetZIndex(_rotationHandle, 1001);
        
        _rotationHandle.MouseLeftButtonDown += RotationHandle_MouseLeftButtonDown;
        _rotationHandle.MouseMove += RotationHandle_MouseMove;
        _rotationHandle.MouseLeftButtonUp += RotationHandle_MouseLeftButtonUp;
        
        _canvas.Children.Add(_rotationHandle);
    }
    
    private void RotationHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_targetElement == null || _targetAnnotation == null) return;
        
        _isRotating = true;
        _originalRotation = _targetAnnotation.Rotation;
        
        // Calculate center of the element
        double left = Canvas.GetLeft(_targetElement);
        double top = Canvas.GetTop(_targetElement);
        double width = _targetElement.ActualWidth > 0 ? _targetElement.ActualWidth : _targetElement.Width;
        double height = _targetElement.ActualHeight > 0 ? _targetElement.ActualHeight : _targetElement.Height;
        
        if (double.IsNaN(width) || double.IsNaN(height))
        {
            if (_targetAnnotation is ImageAnnotationItem imgAnn)
            {
                width = imgAnn.Width * _viewModel.ZoomScale;
                height = imgAnn.Height * _viewModel.ZoomScale;
            }
            else if (_targetAnnotation is ShapeAnnotationItem shapeAnn)
            {
                width = shapeAnn.Width * _viewModel.ZoomScale;
                height = shapeAnn.Height * _viewModel.ZoomScale;
            }
            else if (_targetAnnotation is TextAnnotationItem textAnn)
            {
                width = textAnn.Width * _viewModel.ZoomScale;
                height = textAnn.Height * _viewModel.ZoomScale;
            }
        }
        
        _rotationCenter = new WpfPoint(left + width / 2, top + height / 2);
        
        if (sender is Ellipse handle)
        {
            handle.CaptureMouse();
        }
        e.Handled = true;
    }
    
    private void RotationHandle_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isRotating || _targetElement == null || _targetAnnotation == null) return;
        
        var currentPoint = e.GetPosition(_canvas);
        
        // Calculate angle from center to current mouse position
        double deltaX = currentPoint.X - _rotationCenter.X;
        double deltaY = currentPoint.Y - _rotationCenter.Y;
        double angle = Math.Atan2(deltaY, deltaX) * 180 / Math.PI;
        
        // Adjust angle (0 degrees = up, clockwise positive)
        angle = angle + 90;
        
        // Snap to 15-degree increments if Shift is held
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            angle = Math.Round(angle / 15) * 15;
        }
        
        // Normalize to 0-360
        while (angle < 0) angle += 360;
        while (angle >= 360) angle -= 360;
        
        // Apply rotation transform to the element
        _targetElement.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        _targetElement.RenderTransform = new RotateTransform(angle);
        
        // Update rotation handles position based on rotated element
        UpdateRotationHandlePosition(angle);
        
        _viewModel.StatusMessage = $"Rotation: {angle:F0}° (Hold Shift for 15° snap)";
        e.Handled = true;
    }
    
    private void RotationHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRotating || _targetElement == null || _targetAnnotation == null) return;
        
        if (sender is Ellipse handle)
        {
            handle.ReleaseMouseCapture();
        }
        
        // Get final rotation angle from the transform
        double finalRotation = 0;
        if (_targetElement.RenderTransform is RotateTransform rotateTransform)
        {
            finalRotation = rotateTransform.Angle;
        }
        
        // Normalize to 0-360
        while (finalRotation < 0) finalRotation += 360;
        while (finalRotation >= 360) finalRotation -= 360;
        
        // Record rotation action for undo if rotation changed
        if (Math.Abs(finalRotation - _originalRotation) > 0.1)
        {
            var rotateAction = new RotateAnnotationAction(
                _targetAnnotation,
                _originalRotation,
                finalRotation);
            _viewModel.RecordUndoableAction(rotateAction);
        }
        
        // Update annotation model
        _targetAnnotation.Rotation = finalRotation;
        
        _isRotating = false;
        _viewModel.StatusMessage = $"Rotated to {finalRotation:F0}°";
        
        // Refresh to ensure proper rendering
        _refreshCallback?.Invoke();
        
        e.Handled = true;
    }
    
    /// <summary>
    /// Update rotation handle and line position during rotation
    /// </summary>
    private void UpdateRotationHandlePosition(double angle)
    {
        if (_rotationHandle == null || _rotationLine == null || _targetElement == null) return;
        
        double left = Canvas.GetLeft(_targetElement);
        double top = Canvas.GetTop(_targetElement);
        double width = _targetElement.ActualWidth > 0 ? _targetElement.ActualWidth : _targetElement.Width;
        double height = _targetElement.ActualHeight > 0 ? _targetElement.ActualHeight : _targetElement.Height;
        
        if (double.IsNaN(width) || double.IsNaN(height))
        {
            if (_targetAnnotation is ImageAnnotationItem imgAnn)
            {
                width = imgAnn.Width * _viewModel.ZoomScale;
                height = imgAnn.Height * _viewModel.ZoomScale;
            }
            else if (_targetAnnotation is ShapeAnnotationItem shapeAnn)
            {
                width = shapeAnn.Width * _viewModel.ZoomScale;
                height = shapeAnn.Height * _viewModel.ZoomScale;
            }
        }
        
        double centerX = left + width / 2;
        double centerY = top + height / 2;
        
        // Calculate rotated position of top-center point
        double angleRad = angle * Math.PI / 180;
        double topCenterOffsetX = 0;
        double topCenterOffsetY = -height / 2;
        
        double rotatedTopX = centerX + topCenterOffsetX * Math.Cos(angleRad) - topCenterOffsetY * Math.Sin(angleRad);
        double rotatedTopY = centerY + topCenterOffsetX * Math.Sin(angleRad) + topCenterOffsetY * Math.Cos(angleRad);
        
        // Calculate handle position (above the rotated top-center)
        double handleOffsetX = 0;
        double handleOffsetY = -(height / 2 + RotationHandleDistance);
        
        double rotatedHandleX = centerX + handleOffsetX * Math.Cos(angleRad) - handleOffsetY * Math.Sin(angleRad);
        double rotatedHandleY = centerY + handleOffsetX * Math.Sin(angleRad) + handleOffsetY * Math.Cos(angleRad);
        
        // Update line
        _rotationLine.X1 = rotatedTopX;
        _rotationLine.Y1 = rotatedTopY;
        _rotationLine.X2 = rotatedHandleX;
        _rotationLine.Y2 = rotatedHandleY;
        
        // Update handle position
        Canvas.SetLeft(_rotationHandle, rotatedHandleX - RotationHandleSize / 2);
        Canvas.SetTop(_rotationHandle, rotatedHandleY - RotationHandleSize / 2);
    }
    
    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfRectangle handle && handle.Tag is ResizeHandlePosition position)
        {
            _activeHandle = position;
            _isResizing = true;
            _resizeStartPoint = e.GetPosition(_canvas);
            
            // Store original bounds - for crop mode, store crop bounds; for resize, store element bounds
            if (_isCropMode)
            {
                _originalX = _cropLeft;
                _originalY = _cropTop;
                _originalWidth = _cropWidth;
                _originalHeight = _cropHeight;
            }
            else if (_targetElement != null)
            {
                _originalX = Canvas.GetLeft(_targetElement);
                _originalY = Canvas.GetTop(_targetElement);
                _originalWidth = _targetElement.ActualWidth > 0 ? _targetElement.ActualWidth : _targetElement.Width;
                _originalHeight = _targetElement.ActualHeight > 0 ? _targetElement.ActualHeight : _targetElement.Height;
                
                if (double.IsNaN(_originalWidth) || double.IsNaN(_originalHeight))
                {
                    if (_targetAnnotation is ImageAnnotationItem imgAnn)
                    {
                        _originalWidth = imgAnn.Width * _viewModel.ZoomScale;
                        _originalHeight = imgAnn.Height * _viewModel.ZoomScale;
                    }
                    else if (_targetAnnotation is ShapeAnnotationItem shapeAnn)
                    {
                        _originalWidth = shapeAnn.Width * _viewModel.ZoomScale;
                        _originalHeight = shapeAnn.Height * _viewModel.ZoomScale;
                    }
                }
            }
            
            handle.CaptureMouse();
            e.Handled = true;
        }
    }
    
    private void Handle_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isResizing || _targetElement == null || _targetAnnotation == null) return;
        
        var currentPoint = e.GetPosition(_canvas);
        var deltaX = currentPoint.X - _resizeStartPoint.X;
        var deltaY = currentPoint.Y - _resizeStartPoint.Y;
        
        // Handle crop mode separately
        if (_isCropMode)
        {
            HandleCropMouseMove(deltaX, deltaY);
            e.Handled = true;
            return;
        }
        
        double newLeft = _originalX;
        double newTop = _originalY;
        double newWidth = _originalWidth;
        double newHeight = _originalHeight;
        
        // Check if Shift is held for aspect ratio lock
        bool lockAspectRatio = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        double aspectRatio = _originalWidth / _originalHeight;
        
        // Calculate new bounds based on which handle is being dragged
        switch (_activeHandle)
        {
            case ResizeHandlePosition.TopLeft:
                newLeft = _originalX + deltaX;
                newTop = _originalY + deltaY;
                newWidth = _originalWidth - deltaX;
                newHeight = _originalHeight - deltaY;
                if (lockAspectRatio)
                {
                    if (Math.Abs(deltaX) > Math.Abs(deltaY))
                    {
                        newHeight = newWidth / aspectRatio;
                        newTop = _originalY + _originalHeight - newHeight;
                    }
                    else
                    {
                        newWidth = newHeight * aspectRatio;
                        newLeft = _originalX + _originalWidth - newWidth;
                    }
                }
                break;
                
            case ResizeHandlePosition.TopCenter:
                newTop = _originalY + deltaY;
                newHeight = _originalHeight - deltaY;
                if (lockAspectRatio)
                {
                    newWidth = newHeight * aspectRatio;
                    newLeft = _originalX + (_originalWidth - newWidth) / 2;
                }
                break;
                
            case ResizeHandlePosition.TopRight:
                newTop = _originalY + deltaY;
                newWidth = _originalWidth + deltaX;
                newHeight = _originalHeight - deltaY;
                if (lockAspectRatio)
                {
                    if (Math.Abs(deltaX) > Math.Abs(deltaY))
                    {
                        newHeight = newWidth / aspectRatio;
                        newTop = _originalY + _originalHeight - newHeight;
                    }
                    else
                    {
                        newWidth = newHeight * aspectRatio;
                    }
                }
                break;
                
            case ResizeHandlePosition.MiddleLeft:
                newLeft = _originalX + deltaX;
                newWidth = _originalWidth - deltaX;
                if (lockAspectRatio)
                {
                    newHeight = newWidth / aspectRatio;
                    newTop = _originalY + (_originalHeight - newHeight) / 2;
                }
                break;
                
            case ResizeHandlePosition.MiddleRight:
                newWidth = _originalWidth + deltaX;
                if (lockAspectRatio)
                {
                    newHeight = newWidth / aspectRatio;
                    newTop = _originalY + (_originalHeight - newHeight) / 2;
                }
                break;
                
            case ResizeHandlePosition.BottomLeft:
                newLeft = _originalX + deltaX;
                newWidth = _originalWidth - deltaX;
                newHeight = _originalHeight + deltaY;
                if (lockAspectRatio)
                {
                    if (Math.Abs(deltaX) > Math.Abs(deltaY))
                    {
                        newHeight = newWidth / aspectRatio;
                    }
                    else
                    {
                        newWidth = newHeight * aspectRatio;
                        newLeft = _originalX + _originalWidth - newWidth;
                    }
                }
                break;
                
            case ResizeHandlePosition.BottomCenter:
                newHeight = _originalHeight + deltaY;
                if (lockAspectRatio)
                {
                    newWidth = newHeight * aspectRatio;
                    newLeft = _originalX + (_originalWidth - newWidth) / 2;
                }
                break;
                
            case ResizeHandlePosition.BottomRight:
                newWidth = _originalWidth + deltaX;
                newHeight = _originalHeight + deltaY;
                if (lockAspectRatio)
                {
                    if (Math.Abs(deltaX) > Math.Abs(deltaY))
                    {
                        newHeight = newWidth / aspectRatio;
                    }
                    else
                    {
                        newWidth = newHeight * aspectRatio;
                    }
                }
                break;
        }
        
        // Minimum size constraints
        const double MinSize = 20;
        if (newWidth < MinSize) { newWidth = MinSize; newLeft = _originalX; }
        if (newHeight < MinSize) { newHeight = MinSize; newTop = _originalY; }
        
        // Update element position and size
        Canvas.SetLeft(_targetElement, newLeft);
        Canvas.SetTop(_targetElement, newTop);
        _targetElement.Width = newWidth;
        _targetElement.Height = newHeight;
        
        // Update child image if this is an image annotation with Border wrapper
        if (_targetElement is Border border && border.Child is System.Windows.Controls.Image img)
        {
            img.Width = newWidth;
            img.Height = newHeight;
        }
        
        // Update handle positions
        UpdateHandlePositions(newLeft, newTop, newWidth, newHeight);
        
        e.Handled = true;
    }
    
    private void Handle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing || _targetElement == null || _targetAnnotation == null) return;
        
        if (sender is WpfRectangle handle)
        {
            handle.ReleaseMouseCapture();
        }
        
        // Handle crop mode separately - don't auto-apply, let user click Apply button
        if (_isCropMode && _cropTargetAnnotation != null)
        {
            _isResizing = false;
            UpdateCropButtonPosition();
            e.Handled = true;
            return;
        }
        
        // Get final bounds
        double finalX = Canvas.GetLeft(_targetElement) / _viewModel.ZoomScale;
        double finalY = Canvas.GetTop(_targetElement) / _viewModel.ZoomScale;
        double finalWidth = _targetElement.Width / _viewModel.ZoomScale;
        double finalHeight = _targetElement.Height / _viewModel.ZoomScale;
        
        // Record resize action for undo
        double origX = _originalX / _viewModel.ZoomScale;
        double origY = _originalY / _viewModel.ZoomScale;
        double origW = _originalWidth / _viewModel.ZoomScale;
        double origH = _originalHeight / _viewModel.ZoomScale;
        
        if (Math.Abs(finalX - origX) > 0.1 || Math.Abs(finalY - origY) > 0.1 ||
            Math.Abs(finalWidth - origW) > 0.1 || Math.Abs(finalHeight - origH) > 0.1)
        {
            var resizeAction = new ResizeAnnotationAction(
                _targetAnnotation,
                origX, origY, origW, origH,
                finalX, finalY, finalWidth, finalHeight);
            _viewModel.RecordUndoableAction(resizeAction);
        }
        
        // Update annotation model
        _targetAnnotation.X = finalX;
        _targetAnnotation.Y = finalY;
        
        if (_targetAnnotation is ImageAnnotationItem imgAnn)
        {
            imgAnn.Width = finalWidth;
            imgAnn.Height = finalHeight;
        }
        else if (_targetAnnotation is ShapeAnnotationItem shapeAnn)
        {
            shapeAnn.Width = finalWidth;
            shapeAnn.Height = finalHeight;
        }
        
        _isResizing = false;
        _viewModel.StatusMessage = $"Resized to {finalWidth:F0} x {finalHeight:F0}";
        
        // Refresh to ensure proper rendering
        _refreshCallback?.Invoke();
        
        e.Handled = true;
    }
    
    private void HandleCropMouseMove(double deltaX, double deltaY)
    {
        double newCropLeft = _cropLeft;
        double newCropTop = _cropTop;
        double newCropWidth = _cropWidth;
        double newCropHeight = _cropHeight;
        
        // Calculate new crop bounds based on which handle is being dragged
        switch (_activeHandle)
        {
            case ResizeHandlePosition.TopLeft:
                newCropLeft = Math.Max(_elementLeft, _originalX + deltaX);
                newCropTop = Math.Max(_elementTop, _originalY + deltaY);
                newCropWidth = Math.Max(20, _originalWidth - (newCropLeft - _originalX));
                newCropHeight = Math.Max(20, _originalHeight - (newCropTop - _originalY));
                break;
                
            case ResizeHandlePosition.TopCenter:
                newCropTop = Math.Max(_elementTop, _originalY + deltaY);
                newCropHeight = Math.Max(20, _originalHeight - (newCropTop - _originalY));
                break;
                
            case ResizeHandlePosition.TopRight:
                newCropTop = Math.Max(_elementTop, _originalY + deltaY);
                newCropWidth = Math.Min(_elementLeft + _elementWidth - _cropLeft, Math.Max(20, _originalWidth + deltaX));
                newCropHeight = Math.Max(20, _originalHeight - (newCropTop - _originalY));
                break;
                
            case ResizeHandlePosition.MiddleLeft:
                newCropLeft = Math.Max(_elementLeft, _originalX + deltaX);
                newCropWidth = Math.Max(20, _originalWidth - (newCropLeft - _originalX));
                break;
                
            case ResizeHandlePosition.MiddleRight:
                newCropWidth = Math.Min(_elementLeft + _elementWidth - _cropLeft, Math.Max(20, _originalWidth + deltaX));
                break;
                
            case ResizeHandlePosition.BottomLeft:
                newCropLeft = Math.Max(_elementLeft, _originalX + deltaX);
                newCropWidth = Math.Max(20, _originalWidth - (newCropLeft - _originalX));
                newCropHeight = Math.Min(_elementTop + _elementHeight - _cropTop, Math.Max(20, _originalHeight + deltaY));
                break;
                
            case ResizeHandlePosition.BottomCenter:
                newCropHeight = Math.Min(_elementTop + _elementHeight - _cropTop, Math.Max(20, _originalHeight + deltaY));
                break;
                
            case ResizeHandlePosition.BottomRight:
                newCropWidth = Math.Min(_elementLeft + _elementWidth - _cropLeft, Math.Max(20, _originalWidth + deltaX));
                newCropHeight = Math.Min(_elementTop + _elementHeight - _cropTop, Math.Max(20, _originalHeight + deltaY));
                break;
        }
        
        // Clamp to element bounds
        newCropLeft = Math.Max(_elementLeft, Math.Min(newCropLeft, _elementLeft + _elementWidth - 20));
        newCropTop = Math.Max(_elementTop, Math.Min(newCropTop, _elementTop + _elementHeight - 20));
        
        if (newCropLeft + newCropWidth > _elementLeft + _elementWidth)
            newCropWidth = _elementLeft + _elementWidth - newCropLeft;
        if (newCropTop + newCropHeight > _elementTop + _elementHeight)
            newCropHeight = _elementTop + _elementHeight - newCropTop;
        
        _cropLeft = newCropLeft;
        _cropTop = newCropTop;
        _cropWidth = newCropWidth;
        _cropHeight = newCropHeight;
        
        // Update visuals
        UpdateCropOverlays();
        UpdateHandlePositions(_cropLeft, _cropTop, _cropWidth, _cropHeight);
        UpdateCropButtonPosition();
    }
    
    private void ApplyCrop()
    {
        if (_cropTargetAnnotation == null) return;
        
        // Calculate crop rectangle relative to image element (in display/screen coordinates)
        // TryCropImageAnnotation expects display coordinates (scaled by ZoomScale)
        double relativeLeft = _cropLeft - _elementLeft;
        double relativeTop = _cropTop - _elementTop;
        double cropW = _cropWidth;
        double cropH = _cropHeight;
        
        // Check if any actual cropping occurred (compare in screen coordinates)
        double tolerance = 2.0;
        bool noCrop = Math.Abs(relativeLeft) < tolerance && 
                      Math.Abs(relativeTop) < tolerance &&
                      Math.Abs(cropW - _elementWidth) < tolerance &&
                      Math.Abs(cropH - _elementHeight) < tolerance;
        
        if (noCrop)
        {
            _viewModel.StatusMessage = "No crop applied (area unchanged).";
            CancelCropMode();
            return;
        }
        
        // Call ViewModel to apply the crop (pass display coordinates)
        bool success = _viewModel.TryCropImageAnnotation(
            _cropTargetAnnotation,
            relativeLeft,
            relativeTop,
            cropW,
            cropH);
        
        if (success)
        {
            double unscaledW = cropW / _viewModel.ZoomScale;
            double unscaledH = cropH / _viewModel.ZoomScale;
            _viewModel.StatusMessage = $"Image cropped to {unscaledW:F0} x {unscaledH:F0}";
        }
        
        // Exit crop mode and refresh
        _isCropMode = false;
        _cropTargetAnnotation = null;
        RemoveCropOverlays();
        
        // Restore handle colors
        foreach (var handle in _handles)
        {
            handle.Fill = HandleFillBrush;
        }
        
        _refreshCallback?.Invoke();
    }
    
    private void UpdateHandlePositions(double left, double top, double width, double height)
    {
        foreach (var handle in _handles)
        {
            if (handle.Tag is ResizeHandlePosition position)
            {
                switch (position)
                {
                    case ResizeHandlePosition.TopLeft:
                        Canvas.SetLeft(handle, left - HalfHandleSize);
                        Canvas.SetTop(handle, top - HalfHandleSize);
                        break;
                    case ResizeHandlePosition.TopCenter:
                        Canvas.SetLeft(handle, left + width / 2 - HalfHandleSize);
                        Canvas.SetTop(handle, top - HalfHandleSize);
                        break;
                    case ResizeHandlePosition.TopRight:
                        Canvas.SetLeft(handle, left + width - HalfHandleSize);
                        Canvas.SetTop(handle, top - HalfHandleSize);
                        break;
                    case ResizeHandlePosition.MiddleLeft:
                        Canvas.SetLeft(handle, left - HalfHandleSize);
                        Canvas.SetTop(handle, top + height / 2 - HalfHandleSize);
                        break;
                    case ResizeHandlePosition.MiddleRight:
                        Canvas.SetLeft(handle, left + width - HalfHandleSize);
                        Canvas.SetTop(handle, top + height / 2 - HalfHandleSize);
                        break;
                    case ResizeHandlePosition.BottomLeft:
                        Canvas.SetLeft(handle, left - HalfHandleSize);
                        Canvas.SetTop(handle, top + height - HalfHandleSize);
                        break;
                    case ResizeHandlePosition.BottomCenter:
                        Canvas.SetLeft(handle, left + width / 2 - HalfHandleSize);
                        Canvas.SetTop(handle, top + height - HalfHandleSize);
                        break;
                    case ResizeHandlePosition.BottomRight:
                        Canvas.SetLeft(handle, left + width - HalfHandleSize);
                        Canvas.SetTop(handle, top + height - HalfHandleSize);
                        break;
                }
            }
        }
        
        // Update rotation handle position (without rotation transformation)
        if (_rotationHandle != null && _rotationLine != null)
        {
            double centerX = left + width / 2;
            double handleY = top - RotationHandleDistance;
            
            _rotationLine.X1 = centerX;
            _rotationLine.Y1 = top;
            _rotationLine.X2 = centerX;
            _rotationLine.Y2 = handleY + RotationHandleSize / 2;
            
            Canvas.SetLeft(_rotationHandle, centerX - RotationHandleSize / 2);
            Canvas.SetTop(_rotationHandle, handleY);
        }
    }
}
