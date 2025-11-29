/*
 * ImageOcclusionEditorWinUI3 - A WinUI 3 application for creating image occlusion cards
 * Copyright (C) 2025 Shuai Zhang
 *
 * This file contains code derived from ImageOcclusionEditor by SuperMemo Community,
 * which is licensed under the MIT License.
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Threading.Tasks;
using ImageOcclusionEditorWinUI3.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ImageOcclusionEditorWinUI3
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly string _occlusionFilePath;
        private readonly string _backgroundFilePath;
        private readonly SvgEditorBridge _svgEditorBridge;

        public MainWindow(string backgroundFilePath, string occlusionFilePath)
        {
            InitializeComponent();

            _backgroundFilePath = backgroundFilePath;
            _occlusionFilePath = occlusionFilePath;

            _svgEditorBridge = new SvgEditorBridge(webView);

            _svgEditorBridge.Ready += OnSvgEditorReady;
            _svgEditorBridge.SaveRequested += OnSaveRequested;
            _svgEditorBridge.SaveAndExitRequested += OnSaveAndExitRequested;
            _svgEditorBridge.CancelRequested += OnCancelRequested;

            Closed += OnWindowClosed;
        }

        private async void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ImageOcclusionEditor",
                    "WebView2UserData");

                var (width, height) = OcclusionFileService.GetImageDimensions(_backgroundFilePath);
                Uri targetUri = SvgEditorNavigationBuilder.Build(_backgroundFilePath, width, height);

                await _svgEditorBridge.InitializeAsync(userDataFolder, targetUri);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"Error initializing WebView2: {ex.Message}");
            }
        }

        private async void OnSvgEditorReady(object? sender, EventArgs e)
        {
            try
            {
                Uri backgroundUri = new(Path.GetFullPath(_backgroundFilePath));
                await _svgEditorBridge.SetBackgroundAsync(backgroundUri);

                string svg = OcclusionFileService.GetSvgOverlay(_occlusionFilePath);
                if (!string.IsNullOrWhiteSpace(svg))
                {
                    await _svgEditorBridge.SetSvgContentAsync(svg);
                }

                await _svgEditorBridge.InjectKeyboardShortcutsAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"Error preparing SVG editor: {ex.Message}");
            }
        }

        private void OnCancelRequested(object? sender, EventArgs e)
        {
            Close();
        }

        private async void OnSaveRequested(object? sender, EventArgs e)
        {
            await HandleSaveAsync(closeAfterSave: false, errorPrefix: "save failed");
        }

        private async void OnSaveAndExitRequested(object? sender, EventArgs e)
        {
            await HandleSaveAsync(closeAfterSave: true, errorPrefix: "save and exit failed");
        }

        private async Task HandleSaveAsync(bool closeAfterSave, string errorPrefix)
        {
            try
            {
                await SaveOcclusionAsync();

                if (closeAfterSave)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"{errorPrefix}: {ex.Message}");
            }
        }

        private async Task SaveOcclusionAsync()
        {
            string svg = await _svgEditorBridge.GetSvgContentAsync();

            if (string.IsNullOrEmpty(svg))
            {
                throw new InvalidOperationException("Failed to get SVG data from browser.");
            }

            await OcclusionFileService.SaveOcclusionAsync(_occlusionFilePath, svg);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            await HandleSaveAsync(closeAfterSave: false, errorPrefix: "save failed");
        }

        private async void BtnSaveExit_Click(object sender, RoutedEventArgs e)
        {
            await HandleSaveAsync(closeAfterSave: true, errorPrefix: "save and exit failed");
        }

        private async Task ShowErrorAsync(string message)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };

                await dialog.ShowAsync();
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"Error: {message}");
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _svgEditorBridge.Dispose();
        }
    }
}
