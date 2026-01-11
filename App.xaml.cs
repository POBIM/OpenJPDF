// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using OpenJPDF.ViewModels;
using OpenJPDF.Views;

namespace OpenJPDF;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Flag to indicate if app was opened with a file argument (skip AboutDialog)
    /// </summary>
    public static bool OpenedWithFile { get; private set; }
    
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        
        // Handle command line arguments (e.g., "Open with" from Windows Explorer)
        if (e.Args.Length > 0)
        {
            string filePath = e.Args[0];
            
            // Handle file path with quotes
            filePath = filePath.Trim('"');
            
            if (System.IO.File.Exists(filePath) && 
                filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                // Mark that we're opening with a file (skip AboutDialog)
                OpenedWithFile = true;
                
                // Store the file path to open after window is fully loaded
                string fileToOpen = filePath;
                
                mainWindow.ContentRendered += async (s, args) =>
                {
                    // Use Dispatcher to ensure we're on UI thread and DataContext is ready
                    await mainWindow.Dispatcher.InvokeAsync(async () =>
                    {
                        if (mainWindow.DataContext is MainViewModel vm)
                        {
                            await vm.OpenFileFromPathAsync(fileToOpen);
                        }
                    });
                };
            }
        }
        
        mainWindow.Show();
    }
}
