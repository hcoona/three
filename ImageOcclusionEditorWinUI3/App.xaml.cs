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
using System.Linq;
using FileSignatures;
using FileSignatures.Formats;
using Hjg.Pngcs.Chunks;
using Microsoft.UI.Xaml;

namespace ImageOcclusionEditorWinUI3
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Get command line arguments
            string[] commandLineArgs = Environment.GetCommandLineArgs();

            // Skip the first argument (executable path)
            var actualArgs = commandLineArgs.Skip(1).ToArray();

            if (actualArgs.Length != 2)
            {
                ShowErrorAndExit("Invalid number of parameters. Usage: ImageOcclusionEditor.exe <background-image-path> <occlusion-image-path>");
                return;
            }

            string backgroundImg = actualArgs[0];
            string occlusionImg = actualArgs[1];

            if (!File.Exists(backgroundImg))
            {
                ShowErrorAndExit($"Background file {backgroundImg} doesn't exist");
                return;
            }

            if (!File.Exists(occlusionImg))
            {
                ShowErrorAndExit($"Occlusion file {occlusionImg} doesn't exist");
                return;
            }

            if (!ValidateImage(backgroundImg))
            {
                ShowErrorAndExit($"Background file {backgroundImg} isn't a known Image Format");
                return;
            }

            if (!ValidateImage(occlusionImg))
            {
                ShowErrorAndExit($"Occlusion file {occlusionImg} isn't a known Image Format");
                return;
            }

            // Register PNG chunk factory
            PngChunk.FactoryRegister(PngChunkSVGI.ID, info => new PngChunkSVGI(info));

            _window = new MainWindow(backgroundImg, occlusionImg);
            _window.Activate();
        }

        private static bool ValidateImage(string filePath)
        {
            try
            {
                using Stream stream = File.OpenRead(filePath);
                var insp = new FileFormatInspector();
                return insp.DetermineFileFormat(stream) is Image;
            }
            catch (Exception ex)
            {
                ShowErrorAndExit($"An exception was thrown while opening file {filePath}.\n\nException message: {ex.Message}");
                return false;
            }
        }

        private static void ShowErrorAndExit(string message)
        {
            // For startup errors (before any Window is created), use Win32 MessageBox
            // This is the recommended approach for WinUI 3 early startup errors
            _ = NativeHelper.MessageBox(
                IntPtr.Zero,
                message,
                "Error - Image Occlusion Editor",
                NativeHelper.MB_OK | NativeHelper.MB_ICONERROR);

            // Exit the application after showing error
            Current.Exit();
        }
    }
}
