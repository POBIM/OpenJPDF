// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenJPDF.Models;

namespace OpenJPDF.Views;

public partial class HeaderFooterDialog : Window
{
    public HeaderFooterConfig? Config { get; private set; }
    public int TotalPages { get; set; } = 1;
    
    /// <summary>
    /// Collection of custom text boxes for editing
    /// </summary>
    private readonly ObservableCollection<CustomTextBox> _customTextBoxes = new();
    
    /// <summary>
    /// Collection of custom image boxes for editing
    /// </summary>
    private readonly ObservableCollection<CustomImageBox> _customImageBoxes = new();

    public HeaderFooterDialog()
    {
        InitializeComponent();
        CustomTextBoxesList.ItemsSource = _customTextBoxes;
        CustomImageBoxesList.ItemsSource = _customImageBoxes;
    }

    public HeaderFooterDialog(int totalPages) : this()
    {
        TotalPages = totalPages;
        EndPageBox.Text = totalPages.ToString();
    }

    public HeaderFooterDialog(HeaderFooterConfig existingConfig, int totalPages) : this(totalPages)
    {
        LoadConfig(existingConfig);
    }

    private void LoadConfig(HeaderFooterConfig config)
    {
        // Header
        HeaderEnabledCheck.IsChecked = config.HeaderEnabled;
        HeaderLeftTextBox.Text = config.HeaderLeft.Text;
        HeaderCenterTextBox.Text = config.HeaderCenter.Text;
        HeaderRightTextBox.Text = config.HeaderRight.Text;
        HeaderLeftImagePath.Text = config.HeaderLeft.ImagePath ?? "";
        HeaderCenterImagePath.Text = config.HeaderCenter.ImagePath ?? "";
        HeaderRightImagePath.Text = config.HeaderRight.ImagePath ?? "";
        HeaderLeftImage.IsChecked = config.HeaderLeft.IsImage;
        HeaderLeftText.IsChecked = !config.HeaderLeft.IsImage;
        HeaderCenterImage.IsChecked = config.HeaderCenter.IsImage;
        HeaderCenterText.IsChecked = !config.HeaderCenter.IsImage;
        HeaderRightImage.IsChecked = config.HeaderRight.IsImage;
        HeaderRightText.IsChecked = !config.HeaderRight.IsImage;
        HeaderBold.IsChecked = config.HeaderLeft.IsBold || config.HeaderCenter.IsBold || config.HeaderRight.IsBold;
        HeaderItalic.IsChecked = config.HeaderLeft.IsItalic || config.HeaderCenter.IsItalic || config.HeaderRight.IsItalic;

        // Footer
        FooterEnabledCheck.IsChecked = config.FooterEnabled;
        FooterLeftTextBox.Text = config.FooterLeft.Text;
        FooterCenterTextBox.Text = config.FooterCenter.Text;
        FooterRightTextBox.Text = config.FooterRight.Text;
        FooterLeftImagePath.Text = config.FooterLeft.ImagePath ?? "";
        FooterCenterImagePath.Text = config.FooterCenter.ImagePath ?? "";
        FooterRightImagePath.Text = config.FooterRight.ImagePath ?? "";
        FooterLeftImage.IsChecked = config.FooterLeft.IsImage;
        FooterLeftText.IsChecked = !config.FooterLeft.IsImage;
        FooterCenterImage.IsChecked = config.FooterCenter.IsImage;
        FooterCenterText.IsChecked = !config.FooterCenter.IsImage;
        FooterRightImage.IsChecked = config.FooterRight.IsImage;
        FooterRightText.IsChecked = !config.FooterRight.IsImage;
        FooterBold.IsChecked = config.FooterLeft.IsBold || config.FooterCenter.IsBold || config.FooterRight.IsBold;
        FooterItalic.IsChecked = config.FooterLeft.IsItalic || config.FooterCenter.IsItalic || config.FooterRight.IsItalic;

        // Page range
        ApplyAllPages.IsChecked = config.ApplyToAllPages;
        ApplyPageRange.IsChecked = !config.ApplyToAllPages;
        StartPageBox.Text = config.StartPage.ToString();
        EndPageBox.Text = config.EndPage.ToString();
        SkipFirstPage.IsChecked = config.SkipFirstPage;
        
        // Custom text boxes
        _customTextBoxes.Clear();
        foreach (var textBox in config.CustomTextBoxes)
        {
            _customTextBoxes.Add(new CustomTextBox
            {
                Label = textBox.Label,
                Text = textBox.Text,
                OffsetX = textBox.OffsetX,
                OffsetY = textBox.OffsetY,
                FontFamily = textBox.FontFamily,
                FontSize = textBox.FontSize,
                Color = textBox.Color,
                IsBold = textBox.IsBold,
                IsItalic = textBox.IsItalic,
                ShowBorder = textBox.ShowBorder,
                BoxWidth = textBox.BoxWidth,
                BoxHeight = textBox.BoxHeight,
                Rotation = textBox.Rotation,
                PageScope = textBox.PageScope,
                StartPage = textBox.StartPage,
                EndPage = textBox.EndPage
            });
        }
        
        // Custom image boxes
        _customImageBoxes.Clear();
        foreach (var imageBox in config.CustomImageBoxes)
        {
            _customImageBoxes.Add(new CustomImageBox
            {
                Label = imageBox.Label,
                ImagePath = imageBox.ImagePath,
                OffsetX = imageBox.OffsetX,
                OffsetY = imageBox.OffsetY,
                Width = imageBox.Width,
                Height = imageBox.Height,
                Rotation = imageBox.Rotation,
                Opacity = imageBox.Opacity,
                PageScope = imageBox.PageScope,
                StartPage = imageBox.StartPage,
                EndPage = imageBox.EndPage
            });
        }
    }

    private HeaderFooterConfig BuildConfig()
    {
        var config = new HeaderFooterConfig
        {
            // Header
            HeaderEnabled = HeaderEnabledCheck.IsChecked == true,
            HeaderLeft = new HeaderFooterElement
            {
                Position = HorizontalPosition.Left,
                IsImage = HeaderLeftImage.IsChecked == true,
                Text = HeaderLeftTextBox.Text,
                ImagePath = HeaderLeftImagePath.Text,
                FontFamily = GetSelectedFont(HeaderFontFamily),
                FontSize = GetSelectedFontSize(HeaderFontSize),
                IsBold = HeaderBold.IsChecked == true,
                IsItalic = HeaderItalic.IsChecked == true
            },
            HeaderCenter = new HeaderFooterElement
            {
                Position = HorizontalPosition.Center,
                IsImage = HeaderCenterImage.IsChecked == true,
                Text = HeaderCenterTextBox.Text,
                ImagePath = HeaderCenterImagePath.Text,
                FontFamily = GetSelectedFont(HeaderFontFamily),
                FontSize = GetSelectedFontSize(HeaderFontSize),
                IsBold = HeaderBold.IsChecked == true,
                IsItalic = HeaderItalic.IsChecked == true
            },
            HeaderRight = new HeaderFooterElement
            {
                Position = HorizontalPosition.Right,
                IsImage = HeaderRightImage.IsChecked == true,
                Text = HeaderRightTextBox.Text,
                ImagePath = HeaderRightImagePath.Text,
                FontFamily = GetSelectedFont(HeaderFontFamily),
                FontSize = GetSelectedFontSize(HeaderFontSize),
                IsBold = HeaderBold.IsChecked == true,
                IsItalic = HeaderItalic.IsChecked == true
            },

            // Footer
            FooterEnabled = FooterEnabledCheck.IsChecked == true,
            FooterLeft = new HeaderFooterElement
            {
                Position = HorizontalPosition.Left,
                IsImage = FooterLeftImage.IsChecked == true,
                Text = FooterLeftTextBox.Text,
                ImagePath = FooterLeftImagePath.Text,
                FontFamily = GetSelectedFont(FooterFontFamily),
                FontSize = GetSelectedFontSize(FooterFontSize),
                IsBold = FooterBold.IsChecked == true,
                IsItalic = FooterItalic.IsChecked == true
            },
            FooterCenter = new HeaderFooterElement
            {
                Position = HorizontalPosition.Center,
                IsImage = FooterCenterImage.IsChecked == true,
                Text = FooterCenterTextBox.Text,
                ImagePath = FooterCenterImagePath.Text,
                FontFamily = GetSelectedFont(FooterFontFamily),
                FontSize = GetSelectedFontSize(FooterFontSize),
                IsBold = FooterBold.IsChecked == true,
                IsItalic = FooterItalic.IsChecked == true
            },
            FooterRight = new HeaderFooterElement
            {
                Position = HorizontalPosition.Right,
                IsImage = FooterRightImage.IsChecked == true,
                Text = FooterRightTextBox.Text,
                ImagePath = FooterRightImagePath.Text,
                FontFamily = GetSelectedFont(FooterFontFamily),
                FontSize = GetSelectedFontSize(FooterFontSize),
                IsBold = FooterBold.IsChecked == true,
                IsItalic = FooterItalic.IsChecked == true
            },

            // Page range
            ApplyToAllPages = ApplyAllPages.IsChecked == true,
            StartPage = int.TryParse(StartPageBox.Text, out int start) ? start : 1,
            EndPage = int.TryParse(EndPageBox.Text, out int end) ? end : TotalPages,
            SkipFirstPage = SkipFirstPage.IsChecked == true
        };

        // Custom text boxes
        foreach (var textBox in _customTextBoxes)
        {
            config.CustomTextBoxes.Add(new CustomTextBox
            {
                Label = textBox.Label,
                Text = textBox.Text,
                OffsetX = textBox.OffsetX,
                OffsetY = textBox.OffsetY,
                FontFamily = textBox.FontFamily,
                FontSize = textBox.FontSize,
                Color = textBox.Color,
                IsBold = textBox.IsBold,
                IsItalic = textBox.IsItalic,
                ShowBorder = textBox.ShowBorder,
                BoxWidth = textBox.BoxWidth,
                BoxHeight = textBox.BoxHeight,
                Rotation = textBox.Rotation,
                PageScope = textBox.PageScope,
                StartPage = textBox.StartPage,
                EndPage = textBox.EndPage
            });
        }
        
        // Custom image boxes
        foreach (var imageBox in _customImageBoxes)
        {
            config.CustomImageBoxes.Add(new CustomImageBox
            {
                Label = imageBox.Label,
                ImagePath = imageBox.ImagePath,
                OffsetX = imageBox.OffsetX,
                OffsetY = imageBox.OffsetY,
                Width = imageBox.Width,
                Height = imageBox.Height,
                Rotation = imageBox.Rotation,
                Opacity = imageBox.Opacity,
                PageScope = imageBox.PageScope,
                StartPage = imageBox.StartPage,
                EndPage = imageBox.EndPage
            });
        }

        return config;
    }

    private static string GetSelectedFont(System.Windows.Controls.ComboBox comboBox)
    {
        if (comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            return item.Content?.ToString() ?? "Arial";
        }
        return "Arial";
    }

    private static float GetSelectedFontSize(System.Windows.Controls.ComboBox comboBox)
    {
        if (comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            if (float.TryParse(item.Content?.ToString(), out float size))
            {
                return size;
            }
        }
        return 10f;
    }

    private string? SelectImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            Title = "Select Image for Header/Footer"
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FileName;
        }
        return null;
    }

    // Header image selection handlers
    private void SelectHeaderLeftImage_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectImage();
        if (path != null)
        {
            HeaderLeftImagePath.Text = path;
            HeaderLeftImage.IsChecked = true;
        }
    }

    private void SelectHeaderCenterImage_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectImage();
        if (path != null)
        {
            HeaderCenterImagePath.Text = path;
            HeaderCenterImage.IsChecked = true;
        }
    }

    private void SelectHeaderRightImage_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectImage();
        if (path != null)
        {
            HeaderRightImagePath.Text = path;
            HeaderRightImage.IsChecked = true;
        }
    }

    // Footer image selection handlers
    private void SelectFooterLeftImage_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectImage();
        if (path != null)
        {
            FooterLeftImagePath.Text = path;
            FooterLeftImage.IsChecked = true;
        }
    }

    private void SelectFooterCenterImage_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectImage();
        if (path != null)
        {
            FooterCenterImagePath.Text = path;
            FooterCenterImage.IsChecked = true;
        }
    }

    private void SelectFooterRightImage_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectImage();
        if (path != null)
        {
            FooterRightImagePath.Text = path;
            FooterRightImage.IsChecked = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Config = BuildConfig();
        DialogResult = true;
        Close();
    }

    #region Custom Text Box Handlers

    private void AddTextBox_Click(object sender, RoutedEventArgs e)
    {
        int count = _customTextBoxes.Count + 1;
        _customTextBoxes.Add(new CustomTextBox
        {
            Label = $"Text Box {count}",
            Text = "",
            OffsetX = 50f + (count - 1) * 20f,  // Stagger position
            OffsetY = 100f + (count - 1) * 30f,
            FontSize = 10f,
            FontFamily = "Arial",
            ShowBorder = true,
            BoxWidth = 150f,
            BoxHeight = 20f
        });
    }

    private void RemoveTextBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is CustomTextBox textBox)
        {
            _customTextBoxes.Remove(textBox);
        }
    }

    #endregion
    
    #region Custom Image Box Handlers

    private void AddImageBox_Click(object sender, RoutedEventArgs e)
    {
        int count = _customImageBoxes.Count + 1;
        _customImageBoxes.Add(new CustomImageBox
        {
            Label = $"Image Box {count}",
            ImagePath = "",
            OffsetX = 50f + (count - 1) * 20f,  // Stagger position
            OffsetY = 100f + (count - 1) * 30f,
            Width = 100f,
            Height = 100f,
            Rotation = 0f,
            Opacity = 1.0f,
            PageScope = PageScope.AllPages,
            StartPage = 1,
            EndPage = TotalPages
        });
    }

    private void RemoveImageBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is CustomImageBox imageBox)
        {
            _customImageBoxes.Remove(imageBox);
        }
    }

    private void SelectCustomImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is CustomImageBox imageBox)
        {
            var path = SelectImage();
            if (path != null)
            {
                imageBox.ImagePath = path;
                // Force UI refresh by re-adding to collection
                int index = _customImageBoxes.IndexOf(imageBox);
                if (index >= 0)
                {
                    _customImageBoxes.RemoveAt(index);
                    _customImageBoxes.Insert(index, imageBox);
                }
            }
        }
    }

    #endregion
    
    #region Conversion Handlers
    
    /// <summary>
    /// Convert a CustomTextBox to CustomImageBox (preserves position)
    /// </summary>
    private void ConvertToImageBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is CustomTextBox textBox)
        {
            // Create new ImageBox with same position
            var imageBox = new CustomImageBox
            {
                Label = $"Image (from {textBox.Label})",
                ImagePath = "", // Will need to select image
                OffsetX = textBox.OffsetX,
                OffsetY = textBox.OffsetY,
                Width = textBox.BoxWidth > 0 ? textBox.BoxWidth : 100f,
                Height = textBox.BoxHeight > 0 ? textBox.BoxHeight : 100f,
                Rotation = textBox.Rotation,
                Opacity = 1.0f,
                PageScope = textBox.PageScope,
                StartPage = textBox.StartPage,
                EndPage = textBox.EndPage
            };
            
            // Prompt user to select image
            var path = SelectImage();
            if (path != null)
            {
                imageBox.ImagePath = path;
                
                // Remove the text box and add the image box
                _customTextBoxes.Remove(textBox);
                _customImageBoxes.Add(imageBox);
            }
        }
    }
    
    /// <summary>
    /// Convert a CustomImageBox to CustomTextBox (preserves position)
    /// </summary>
    private void ConvertToTextBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is CustomImageBox imageBox)
        {
            // Create new TextBox with same position
            var textBox = new CustomTextBox
            {
                Label = $"Text (from {imageBox.Label})",
                Text = "", // Empty text, user will enter
                OffsetX = imageBox.OffsetX,
                OffsetY = imageBox.OffsetY,
                FontFamily = "Arial",
                FontSize = 12f,
                Color = "#000000",
                IsBold = false,
                IsItalic = false,
                ShowBorder = true,
                BoxWidth = imageBox.Width,
                BoxHeight = imageBox.Height,
                Rotation = imageBox.Rotation,
                PageScope = imageBox.PageScope,
                StartPage = imageBox.StartPage,
                EndPage = imageBox.EndPage
            };
            
            // Remove the image box and add the text box
            _customImageBoxes.Remove(imageBox);
            _customTextBoxes.Add(textBox);
        }
    }
    
    #endregion
}
