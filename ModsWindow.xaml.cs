using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MatHax.Reborn.Launcher.Models;
using MatHax.Reborn.Launcher.Services;

namespace MatHax.Reborn.Launcher;

public partial class ModsWindow : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly CuratedModService mods = new();
    private bool busy;

    public ModsWindow()
    {
        InitializeComponent();
        mods.ProgressChanged += OnProgressChanged;
        Closed += (_, _) => cancellation.Cancel();
        Refresh();
    }

    private async void Sodium_Click(object sender, RoutedEventArgs e) =>
        await ToggleAsync("sodium");

    private async void Lithium_Click(object sender, RoutedEventArgs e) =>
        await ToggleAsync("lithium");

    private async void Iris_Click(object sender, RoutedEventArgs e) =>
        await ToggleAsync("iris");

    private async Task ToggleAsync(string slug)
    {
        if (busy) return;
        CuratedMod mod = mods.Catalog.Single(item => item.Slug == slug);
        SetBusy(true);
        try
        {
            if (mods.IsInstalled(slug)) mods.Uninstall(mod);
            else await mods.InstallAsync(mod, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operation cancelled.", 0);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, 0);
            MessageBox.Show(this, exception.Message, "Mod operation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    private void Refresh()
    {
        RefreshButton("sodium", SodiumButton, SodiumVersion);
        RefreshButton("lithium", LithiumButton, LithiumVersion);
        RefreshButton("iris", IrisButton, IrisVersion);
    }

    private void RefreshButton(string slug, Button button, TextBlock version)
    {
        string? installedVersion = mods.InstalledVersion(slug);
        bool isInstalled = installedVersion is not null;
        button.Content = isInstalled ? "Remove" : "Install";
        version.Text = isInstalled ? $"Installed • {installedVersion}" : "Ready to install";
    }

    private void OnProgressChanged(string message, double? value)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(message, value));
            return;
        }
        SetStatus(message, value);
    }

    private void SetStatus(string message, double? value)
    {
        StatusText.Text = message;
        Progress.IsIndeterminate = value is null;
        if (value is not null) Progress.Value = Math.Clamp(value.Value, 0, 100);
    }

    private void SetBusy(bool value)
    {
        busy = value;
        SodiumButton.IsEnabled = !value;
        LithiumButton.IsEnabled = !value;
        IrisButton.IsEnabled = !value;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ShellClip is not null) ShellClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
