using System.Globalization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Graphics;
using WinRT.Interop;

namespace Hcoona.CelesphoniaModifier.WinUI;

public sealed partial class MainWindow : Window
{
    private const double InitialHeightDips = 680;
    private const double InitialWidthDips = 720;
    private const string BaselineAffirmation =
        "This is a Celesphonia v1.05 Steam build 13624401 save, the game is closed, "
        + "and no other tool is editing this slot";
    private bool _allowClose;
    private bool _closePending;

    public MainWindow()
        : this(new GoldEditorViewModel(new GoldEditorOperations()))
    {
    }

    internal MainWindow(GoldEditorViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        ResizeInitialWindow();
        ViewModel.OperationCompleted += ViewModel_OperationCompleted;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        AppWindow.Closing += AppWindow_Closing;
        Closed += MainWindow_Closed;
    }

    internal GoldEditorViewModel ViewModel { get; }

    internal static Visibility ToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal static InfoBarSeverity ToInfoBarSeverity(GoldEditorResultSeverity severity)
    {
        return severity switch
        {
            GoldEditorResultSeverity.Informational => InfoBarSeverity.Informational,
            GoldEditorResultSeverity.Success => InfoBarSeverity.Success,
            GoldEditorResultSeverity.Warning => InfoBarSeverity.Warning,
            GoldEditorResultSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
    }

    internal static AutomationLiveSetting ToAutomationLiveSetting(
        GoldEditorAnnouncement announcement)
    {
        return announcement == GoldEditorAnnouncement.Assertive
            ? AutomationLiveSetting.Assertive
            : AutomationLiveSetting.Polite;
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileOpenPicker picker = new(AppWindow.Id)
            {
                CommitButtonText = "Open save slot",
            };
            picker.FileTypeFilter.Add(".rpgsave");

            var result = await picker.PickSingleFileAsync();
            if (result is null)
            {
                ViewModel.PreserveAfterPickerCancellation();
                return;
            }

            await ViewModel.LoadAsync(result.Path);
        }
        catch (Exception)
        {
            ViewModel.ReportPickerFailure();
        }
    }

    private async void ApplyGoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryCreateConfirmation(out GoldEditorConfirmation? confirmation)
            || confirmation is null)
        {
            return;
        }

        bool confirmed = await ShowConfirmationAsync(confirmation);
        if (!confirmed)
        {
            RestoreApplyFocus();
            return;
        }

        await ViewModel.ApplyConfirmedAsync(confirmation.RequestedGold);
    }

    private void CancelOperationButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RequestCancellation();
    }

    private async Task<bool> ShowConfirmationAsync(GoldEditorConfirmation confirmation)
    {
        CheckBox affirmation = new()
        {
            Content = new TextBlock
            {
                Text = BaselineAffirmation,
                TextWrapping = TextWrapping.Wrap,
            },
            IsChecked = false,
        };
        AutomationProperties.SetAutomationId(affirmation, "ConfirmationCheckBox");
        AutomationProperties.SetName(affirmation, BaselineAffirmation);

        TextBox path = CreateReadOnlyDialogValue(
            "ConfirmationSlotPath",
            "Save slot path",
            confirmation.SlotPath);
        TextBox archive = CreateReadOnlyDialogValue(
            "ConfirmationArchivePath",
            "Archive path",
            confirmation.BackupPath);

        StackPanel content = new()
        {
            Spacing = 10,
            MaxWidth = 600,
        };
        content.Children.Add(CreateDialogLabel("Save slot"));
        content.Children.Add(path);
        content.Children.Add(CreateDialogLabel("Current Gold"));
        content.Children.Add(CreateDialogValue(
            "ConfirmationCurrentGold",
            confirmation.CurrentGold.ToString(CultureInfo.InvariantCulture)));
        content.Children.Add(CreateDialogLabel("Requested Gold"));
        content.Children.Add(CreateDialogValue(
            "ConfirmationRequestedGold",
            confirmation.RequestedGoldText));
        content.Children.Add(CreateDialogLabel("Original archive"));
        content.Children.Add(archive);
        content.Children.Add(CreateDialogValue(
            "ConfirmationBaseline",
            "Declared baseline: Celesphonia v1.05 Steam build 13624401."));
        content.Children.Add(CreateDialogValue(
            "ConfirmationVersionWarning",
            "This app cannot verify the installation or save version. Another process can replace "
                + "the path after preview comparison; close the game and every other editor."));
        content.Children.Add(affirmation);

        ContentDialog dialog = new()
        {
            XamlRoot = RootContent.XamlRoot,
            RequestedTheme = RootContent.ActualTheme,
            Title = "Confirm Gold application",
            PrimaryButtonText = "Apply Gold",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            Content = content,
        };
        affirmation.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        affirmation.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static TextBlock CreateDialogLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
    }

    private static TextBlock CreateDialogValue(string automationId, string text)
    {
        TextBlock value = new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(value, automationId);
        AutomationProperties.SetName(value, text);
        return value;
    }

    private static TextBox CreateReadOnlyDialogValue(
        string automationId,
        string automationName,
        string text)
    {
        TextBox value = new()
        {
            IsReadOnly = true,
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(value, automationId);
        AutomationProperties.SetName(value, automationName);
        return value;
    }

    private void ResizeInitialWindow()
    {
        nint windowHandle = WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(windowHandle) / 96.0;
        AppWindow.Resize(
            new SizeInt32(
                checked((int)Math.Round(InitialWidthDips * scale)),
                checked((int)Math.Round(InitialHeightDips * scale))));
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !ViewModel.IsBusy)
        {
            return;
        }

        args.Cancel = true;
        _closePending = true;
        ViewModel.RequestCancellation();
    }

    private void ViewModel_OperationCompleted(object? sender, EventArgs e)
    {
        if (_closePending)
        {
            _allowClose = true;
            Close();
            return;
        }

        if (ViewModel.ShouldRestoreApplyFocus && ApplyGoldButton.IsEnabled)
        {
            RestoreApplyFocus();
        }
    }

    private void RestoreApplyFocus()
    {
        _ = DispatcherQueue.TryEnqueue(
            () => _ = ApplyGoldButton.Focus(FocusState.Programmatic));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GoldEditorViewModel.IsResultOpen)
            || !ViewModel.IsResultOpen)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(
            () =>
            {
                AutomationPeer? peer =
                    FrameworkElementAutomationPeer.FromElement(ResultInfoBar)
                    ?? FrameworkElementAutomationPeer.CreatePeerForElement(ResultInfoBar);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            });
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        ViewModel.OperationCompleted -= ViewModel_OperationCompleted;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        AppWindow.Closing -= AppWindow_Closing;
        Closed -= MainWindow_Closed;
        ViewModel.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
