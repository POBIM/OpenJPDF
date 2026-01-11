// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using OpenJPDF.Models;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;

namespace OpenJPDF.Views;

/// <summary>
/// Item for the selected pages list with order tracking
/// </summary>
public class ExtractPageItem : INotifyPropertyChanged
{
    private int _order;

    public int PageNumber { get; set; }
    public int OriginalIndex { get; set; }
    public BitmapSource? Thumbnail { get; set; }

    public int Order
    {
        get => _order;
        set
        {
            _order = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Order)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ExtractPagesDialog : Window
{
    private readonly ObservableCollection<PageThumbnail> _availablePages = new();
    private readonly ObservableCollection<ExtractPageItem> _selectedPages = new();

    private Point _dragStartPoint;
    private bool _isDragging;

    /// <summary>
    /// The selected page indices in the user-specified order (0-based)
    /// </summary>
    public int[] SelectedPages { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// Constructor with PageThumbnails from the main view
    /// </summary>
    public ExtractPagesDialog(IEnumerable<PageThumbnail> pageThumbnails, IEnumerable<PageThumbnail>? preSelectedPages = null)
    {
        InitializeComponent();

        // Populate available pages
        foreach (var page in pageThumbnails)
        {
            _availablePages.Add(page);
        }

        AvailablePagesListBox.ItemsSource = _availablePages;
        SelectedPagesListBox.ItemsSource = _selectedPages;

        // If pre-selected pages provided, add them to selected list
        if (preSelectedPages != null)
        {
            foreach (var page in preSelectedPages)
            {
                AddPageToSelected(page);
            }
        }

        UpdateStatus();
    }

    /// <summary>
    /// Legacy constructor for backward compatibility
    /// </summary>
    public ExtractPagesDialog(int totalPages) : this(CreateDummyThumbnails(totalPages))
    {
    }

    private static IEnumerable<PageThumbnail> CreateDummyThumbnails(int totalPages)
    {
        for (int i = 0; i < totalPages; i++)
        {
            yield return new PageThumbnail { PageNumber = i + 1, OriginalPageIndex = i };
        }
    }

    private void AddPageToSelected(PageThumbnail page)
    {
        // Check if already added
        if (_selectedPages.Any(p => p.OriginalIndex == page.OriginalPageIndex))
            return;

        var item = new ExtractPageItem
        {
            PageNumber = page.PageNumber,
            OriginalIndex = page.OriginalPageIndex,
            Thumbnail = page.Thumbnail,
            Order = _selectedPages.Count + 1
        };

        _selectedPages.Add(item);
        UpdateStatus();
    }

    private void UpdateOrderNumbers()
    {
        for (int i = 0; i < _selectedPages.Count; i++)
        {
            _selectedPages[i].Order = i + 1;
        }
    }

    private void UpdateStatus()
    {
        StatusText.Text = $"{_selectedPages.Count} page(s) selected";
    }

    #region Add/Remove Buttons

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = AvailablePagesListBox.SelectedItems.Cast<PageThumbnail>().ToList();
        foreach (var page in selectedItems)
        {
            AddPageToSelected(page);
        }
    }

    private void AddAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var page in _availablePages)
        {
            AddPageToSelected(page);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = SelectedPagesListBox.SelectedItems.Cast<ExtractPageItem>().ToList();
        foreach (var item in selectedItems)
        {
            _selectedPages.Remove(item);
        }
        UpdateOrderNumbers();
        UpdateStatus();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _selectedPages.Clear();
        UpdateStatus();
    }

    #endregion

    #region Reorder Buttons

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = SelectedPagesListBox.SelectedItems.Cast<ExtractPageItem>()
            .OrderBy(item => _selectedPages.IndexOf(item))
            .ToList();

        foreach (var item in selectedItems)
        {
            int index = _selectedPages.IndexOf(item);
            if (index > 0)
            {
                _selectedPages.Move(index, index - 1);
            }
        }
        UpdateOrderNumbers();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = SelectedPagesListBox.SelectedItems.Cast<ExtractPageItem>()
            .OrderByDescending(item => _selectedPages.IndexOf(item))
            .ToList();

        foreach (var item in selectedItems)
        {
            int index = _selectedPages.IndexOf(item);
            if (index < _selectedPages.Count - 1)
            {
                _selectedPages.Move(index, index + 1);
            }
        }
        UpdateOrderNumbers();
    }

    private void MoveToTop_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = SelectedPagesListBox.SelectedItems.Cast<ExtractPageItem>()
            .OrderBy(item => _selectedPages.IndexOf(item))
            .ToList();

        int targetIndex = 0;
        foreach (var item in selectedItems)
        {
            int currentIndex = _selectedPages.IndexOf(item);
            if (currentIndex != targetIndex)
            {
                _selectedPages.Move(currentIndex, targetIndex);
            }
            targetIndex++;
        }
        UpdateOrderNumbers();
    }

    private void MoveToBottom_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = SelectedPagesListBox.SelectedItems.Cast<ExtractPageItem>()
            .OrderByDescending(item => _selectedPages.IndexOf(item))
            .ToList();

        int targetIndex = _selectedPages.Count - 1;
        foreach (var item in selectedItems)
        {
            int currentIndex = _selectedPages.IndexOf(item);
            if (currentIndex != targetIndex)
            {
                _selectedPages.Move(currentIndex, targetIndex);
            }
            targetIndex--;
        }
        UpdateOrderNumbers();
    }

    #endregion

    #region Drag and Drop Reorder

    private void SelectedPages_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void SelectedPages_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        Point position = e.GetPosition(null);
        Vector diff = _dragStartPoint - position;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var listBox = sender as System.Windows.Controls.ListBox;
            var item = GetItemAtPosition(listBox, e.GetPosition(listBox));

            if (item != null)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(listBox!, item, DragDropEffects.Move);
                _isDragging = false;
            }
        }
    }

    private void SelectedPages_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ExtractPageItem)) is not ExtractPageItem droppedItem)
            return;

        var listBox = sender as System.Windows.Controls.ListBox;
        var targetItem = GetItemAtPosition(listBox, e.GetPosition(listBox));

        int oldIndex = _selectedPages.IndexOf(droppedItem);
        int newIndex = targetItem != null ? _selectedPages.IndexOf(targetItem) : _selectedPages.Count - 1;

        if (oldIndex != newIndex && oldIndex >= 0 && newIndex >= 0)
        {
            _selectedPages.Move(oldIndex, newIndex);
            UpdateOrderNumbers();
        }
    }

    private ExtractPageItem? GetItemAtPosition(System.Windows.Controls.ListBox? listBox, Point position)
    {
        if (listBox == null) return null;

        var element = listBox.InputHitTest(position) as DependencyObject;
        while (element != null && element != listBox)
        {
            if (element is System.Windows.Controls.ListBoxItem listBoxItem)
            {
                return listBoxItem.Content as ExtractPageItem;
            }
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    #endregion

    #region Dialog Buttons

    private void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPages.Count == 0)
        {
            MessageBox.Show("Please select at least one page to extract.", "Warning",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Return pages in user-specified order (0-based indices)
        SelectedPages = _selectedPages.Select(p => p.OriginalIndex).ToArray();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #endregion
}
