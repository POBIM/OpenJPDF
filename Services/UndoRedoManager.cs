// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using OpenJPDF.Models;

namespace OpenJPDF.Services;

/// <summary>
/// Interface for undoable actions
/// </summary>
public interface IUndoableAction
{
    /// <summary>
    /// Description of the action for UI display
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Execute the action (or redo)
    /// </summary>
    void Execute();
    
    /// <summary>
    /// Undo the action
    /// </summary>
    void Undo();
}

/// <summary>
/// Action for adding an annotation
/// </summary>
public class AddAnnotationAction : IUndoableAction
{
    private readonly ObservableCollection<AnnotationItem> _collection;
    private readonly AnnotationItem _annotation;
    
    public string Description => $"Add {_annotation.DisplayName}";
    
    public AddAnnotationAction(ObservableCollection<AnnotationItem> collection, AnnotationItem annotation)
    {
        _collection = collection;
        _annotation = annotation;
    }
    
    public void Execute()
    {
        if (!_collection.Contains(_annotation))
            _collection.Add(_annotation);
    }
    
    public void Undo()
    {
        _collection.Remove(_annotation);
    }
}

/// <summary>
/// Action for deleting an annotation
/// </summary>
public class DeleteAnnotationAction : IUndoableAction
{
    private readonly ObservableCollection<AnnotationItem> _collection;
    private readonly AnnotationItem _annotation;
    private int _originalIndex;
    
    public string Description => $"Delete {_annotation.DisplayName}";
    
    public DeleteAnnotationAction(ObservableCollection<AnnotationItem> collection, AnnotationItem annotation)
    {
        _collection = collection;
        _annotation = annotation;
        _originalIndex = _collection.IndexOf(annotation);
    }
    
    public void Execute()
    {
        _originalIndex = _collection.IndexOf(_annotation);
        _collection.Remove(_annotation);
    }
    
    public void Undo()
    {
        if (_originalIndex >= 0 && _originalIndex <= _collection.Count)
            _collection.Insert(_originalIndex, _annotation);
        else
            _collection.Add(_annotation);
    }
}

/// <summary>
/// Action for moving an annotation (position change)
/// </summary>
public class MoveAnnotationAction : IUndoableAction
{
    private readonly AnnotationItem _annotation;
    private readonly double _oldX, _oldY;
    private readonly double _newX, _newY;
    
    public string Description => $"Move {_annotation.DisplayName}";
    
    public MoveAnnotationAction(AnnotationItem annotation, double oldX, double oldY, double newX, double newY)
    {
        _annotation = annotation;
        _oldX = oldX;
        _oldY = oldY;
        _newX = newX;
        _newY = newY;
    }
    
    public void Execute()
    {
        _annotation.X = _newX;
        _annotation.Y = _newY;
    }
    
    public void Undo()
    {
        _annotation.X = _oldX;
        _annotation.Y = _oldY;
    }
}

/// <summary>
/// Action for resizing an annotation
/// </summary>
public class ResizeAnnotationAction : IUndoableAction
{
    private readonly AnnotationItem _annotation;
    private readonly double _oldX, _oldY, _oldWidth, _oldHeight;
    private readonly double _newX, _newY, _newWidth, _newHeight;
    
    public string Description => $"Resize {_annotation.DisplayName}";
    
    public ResizeAnnotationAction(
        AnnotationItem annotation,
        double oldX, double oldY, double oldWidth, double oldHeight,
        double newX, double newY, double newWidth, double newHeight)
    {
        _annotation = annotation;
        _oldX = oldX;
        _oldY = oldY;
        _oldWidth = oldWidth;
        _oldHeight = oldHeight;
        _newX = newX;
        _newY = newY;
        _newWidth = newWidth;
        _newHeight = newHeight;
    }
    
    public void Execute()
    {
        _annotation.X = _newX;
        _annotation.Y = _newY;
        SetSize(_newWidth, _newHeight);
    }
    
    public void Undo()
    {
        _annotation.X = _oldX;
        _annotation.Y = _oldY;
        SetSize(_oldWidth, _oldHeight);
    }
    
    private void SetSize(double width, double height)
    {
        if (_annotation is ImageAnnotationItem img)
        {
            img.Width = width;
            img.Height = height;
        }
        else if (_annotation is ShapeAnnotationItem shape)
        {
            shape.Width = width;
            shape.Height = height;
        }
        else if (_annotation is TextAnnotationItem text)
        {
            text.Width = width;
            text.Height = height;
        }
    }
}

/// <summary>
/// Action for rotating an annotation
/// </summary>
public class RotateAnnotationAction : IUndoableAction
{
    private readonly AnnotationItem _annotation;
    private readonly double _oldRotation;
    private readonly double _newRotation;
    
    public string Description => $"Rotate {_annotation.DisplayName}";
    
    public RotateAnnotationAction(AnnotationItem annotation, double oldRotation, double newRotation)
    {
        _annotation = annotation;
        _oldRotation = oldRotation;
        _newRotation = newRotation;
    }
    
    public void Execute()
    {
        _annotation.Rotation = _newRotation;
    }
    
    public void Undo()
    {
        _annotation.Rotation = _oldRotation;
    }
}

/// <summary>
/// Action for modifying annotation properties
/// </summary>
public class ModifyAnnotationAction : IUndoableAction
{
    private readonly AnnotationItem _annotation;
    private readonly Dictionary<string, object?> _oldValues;
    private readonly Dictionary<string, object?> _newValues;
    private readonly string _propertyName;
    
    public string Description => $"Modify {_propertyName} of {_annotation.DisplayName}";
    
    public ModifyAnnotationAction(AnnotationItem annotation, string propertyName, object? oldValue, object? newValue)
    {
        _annotation = annotation;
        _propertyName = propertyName;
        _oldValues = new Dictionary<string, object?> { { propertyName, oldValue } };
        _newValues = new Dictionary<string, object?> { { propertyName, newValue } };
    }
    
    public ModifyAnnotationAction(AnnotationItem annotation, Dictionary<string, object?> oldValues, Dictionary<string, object?> newValues)
    {
        _annotation = annotation;
        _oldValues = oldValues;
        _newValues = newValues;
        _propertyName = string.Join(", ", newValues.Keys);
    }
    
    public void Execute()
    {
        ApplyValues(_newValues);
    }
    
    public void Undo()
    {
        ApplyValues(_oldValues);
    }
    
    private void ApplyValues(Dictionary<string, object?> values)
    {
        var type = _annotation.GetType();
        foreach (var kvp in values)
        {
            var prop = type.GetProperty(kvp.Key);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(_annotation, kvp.Value);
            }
        }
    }
}

/// <summary>
/// Manages undo/redo stack for annotation operations
/// </summary>
public class UndoRedoManager
{
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();
    private const int MaxStackSize = 100;
    
    /// <summary>
    /// Event raised when undo/redo state changes
    /// </summary>
    public event Action? StateChanged;
    
    /// <summary>
    /// Whether undo is available
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;
    
    /// <summary>
    /// Whether redo is available
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;
    
    /// <summary>
    /// Description of next undo action
    /// </summary>
    public string? UndoDescription => _undoStack.TryPeek(out var action) ? action.Description : null;
    
    /// <summary>
    /// Description of next redo action
    /// </summary>
    public string? RedoDescription => _redoStack.TryPeek(out var action) ? action.Description : null;
    
    /// <summary>
    /// Execute an action and add it to the undo stack
    /// </summary>
    public void ExecuteAction(IUndoableAction action)
    {
        action.Execute();
        _undoStack.Push(action);
        _redoStack.Clear(); // Clear redo stack when new action is performed
        
        // Limit stack size
        TrimStack(_undoStack);
        
        StateChanged?.Invoke();
    }
    
    /// <summary>
    /// Add an action to the undo stack without executing it
    /// (for actions that have already been performed)
    /// </summary>
    public void RecordAction(IUndoableAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
        TrimStack(_undoStack);
        StateChanged?.Invoke();
    }
    
    /// <summary>
    /// Undo the last action
    /// </summary>
    public void Undo()
    {
        if (!CanUndo) return;
        
        var action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);
        
        StateChanged?.Invoke();
    }
    
    /// <summary>
    /// Redo the last undone action
    /// </summary>
    public void Redo()
    {
        if (!CanRedo) return;
        
        var action = _redoStack.Pop();
        action.Execute();
        _undoStack.Push(action);
        
        StateChanged?.Invoke();
    }
    
    /// <summary>
    /// Clear all undo/redo history
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }
    
    private void TrimStack(Stack<IUndoableAction> stack)
    {
        while (stack.Count > MaxStackSize)
        {
            // Remove oldest item (bottom of stack)
            var temp = stack.ToArray();
            stack.Clear();
            for (int i = 0; i < temp.Length - 1; i++)
            {
                stack.Push(temp[temp.Length - 1 - i]);
            }
        }
    }
}
