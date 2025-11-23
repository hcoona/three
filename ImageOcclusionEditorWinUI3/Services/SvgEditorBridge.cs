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
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ImageOcclusionEditorWinUI3.Services
{
    internal sealed class SvgEditorBridge : IDisposable
    {
        private const string AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection --allow-file-access-from-files";

        private const string KeyboardShortcutScript = @"
                (function() {
                    if (window.imageOcclusionKeyHandler) {
                        document.removeEventListener('keydown', window.imageOcclusionKeyHandler, true);
                        document.removeEventListener('keyup', window.imageOcclusionKeyHandler, true);
                    }

                    window.imageOcclusionHandlerActive = true;

                    window.imageOcclusionKeyHandler = function(e) {
                        if (!window.imageOcclusionHandlerActive) return;

                        if (e.key === 'Escape') {
                            e.preventDefault();
                            e.stopPropagation();
                            e.stopImmediatePropagation();
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage('cancel');
                            }
                            return false;
                        }
                        else if (e.ctrlKey && e.shiftKey && (e.key === 'S' || e.key === 's')) {
                            e.preventDefault();
                            e.stopPropagation();
                            e.stopImmediatePropagation();
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage('save');
                            }
                            return false;
                        }
                        else if (e.ctrlKey && !e.shiftKey && (e.key === 'S' || e.key === 's')) {
                            e.preventDefault();
                            e.stopPropagation();
                            e.stopImmediatePropagation();
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage('saveExit');
                            }
                            return false;
                        }
                    };

                    document.addEventListener('keydown', window.imageOcclusionKeyHandler, true);
                    window.addEventListener('keydown', window.imageOcclusionKeyHandler, true);

                    window.addEventListener('keyup', function(e) {
                        if (!window.imageOcclusionHandlerActive) return;

                        if (e.key === 'Escape' ||
                            (e.ctrlKey && (e.key === 'S' || e.key === 's'))) {
                            e.preventDefault();
                            e.stopPropagation();
                        }
                    }, true);

                    console.log('Image Occlusion keyboard shortcuts injected successfully');

                    setTimeout(function() {
                        if (window.imageOcclusionHandlerActive && window.chrome && window.chrome.webview) {
                            console.log('Keyboard shortcut handler still active');
                        }
                    }, 5000);
                })();
            ";

        private readonly WebView2 _webView;
        private bool _isReady;

        public SvgEditorBridge(WebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _webView.NavigationCompleted += HandleNavigationCompleted;
            _webView.WebMessageReceived += HandleWebMessageReceived;
        }

        public event EventHandler? Ready;
        public event EventHandler? SaveRequested;
        public event EventHandler? SaveAndExitRequested;
        public event EventHandler? CancelRequested;

        public bool IsReady => _isReady;

        public async Task InitializeAsync(string userDataFolder, Uri targetUri)
        {
            if (string.IsNullOrWhiteSpace(userDataFolder))
            {
                throw new ArgumentException("User data folder cannot be null or whitespace.", nameof(userDataFolder));
            }

            if (targetUri is null)
            {
                throw new ArgumentNullException(nameof(targetUri));
            }

            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = AdditionalBrowserArguments,
                });

            await _webView.EnsureCoreWebView2Async(environment);
            ConfigureWebView();

            _isReady = false;
            _webView.CoreWebView2.Navigate(targetUri.ToString());
        }

        public async Task InjectKeyboardShortcutsAsync()
        {
            EnsureReady();
            await _webView.CoreWebView2.ExecuteScriptAsync(KeyboardShortcutScript);
        }

        public async Task SetBackgroundAsync(Uri backgroundImageUri)
        {
            if (backgroundImageUri is null)
            {
                throw new ArgumentNullException(nameof(backgroundImageUri));
            }

            EnsureReady();
            string script = $"svgEditor.setBackground(\"\", \"{backgroundImageUri.AbsoluteUri}\")";
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        public async Task SetSvgContentAsync(string svg)
        {
            if (svg is null)
            {
                throw new ArgumentNullException(nameof(svg));
            }

            EnsureReady();

            string sanitized = svg.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("'", "\\'");
            string script = $"svgEditor.loadSvgString('{sanitized}')";
            string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);

            if (result == "false")
            {
                throw new InvalidOperationException("Failed to set SVG in browser.");
            }
        }

        public async Task<string> GetSvgContentAsync()
        {
            EnsureReady();
            string result = await _webView.CoreWebView2.ExecuteScriptAsync("svgEditor.svgCanvas.svgCanvasToString()");
            return JsonSerializer.Deserialize(result, ImageOcclusionEditorWinUI3.JsonContext.Default.String) ?? string.Empty;
        }

        public void Dispose()
        {
            _webView.NavigationCompleted -= HandleNavigationCompleted;
            _webView.WebMessageReceived -= HandleWebMessageReceived;
        }

        private void ConfigureWebView()
        {
            var settings = _webView.CoreWebView2.Settings;
            settings.AreHostObjectsAllowed = true;
            settings.IsWebMessageEnabled = true;
            settings.AreDevToolsEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
        }

        private void HandleNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                return;
            }

            if (_isReady)
            {
                return;
            }

            _isReady = true;
            Ready?.Invoke(this, EventArgs.Empty);
        }

        private void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message = e.TryGetWebMessageAsString();

            switch (message?.ToLowerInvariant())
            {
                case "cancel":
                    CancelRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "save":
                    SaveRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "saveexit":
                    SaveAndExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private void EnsureReady()
        {
            if (!_isReady || _webView.CoreWebView2 is null)
            {
                throw new InvalidOperationException("WebView2 is not ready.");
            }
        }
    }
}
