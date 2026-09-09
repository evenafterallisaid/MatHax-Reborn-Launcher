using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MatHax.Reborn.Launcher.Infrastructure;
using MatHax.Reborn.Launcher.Models;
using MatHax.Reborn.Launcher.Services;

namespace MatHax.Reborn.Launcher;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly LauncherSettings settings;
    private readonly ClientReleaseService releases;
    private readonly MinecraftService minecraft;
    private readonly OfficialLauncherInstaller officialInstaller = new();
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        AppPaths.EnsureCreated();

        settings = LauncherSettings.Load(AppPaths.Settings);
        MemorySlider.Value = Math.Clamp(settings.MemoryGb, 2, 16);

        releases = new ClientReleaseService();
        minecraft = new MinecraftService(releases);
        minecraft.ProgressChanged += OnProgressChanged;

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => cancellation.Cancel();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            ClientRelease? release = await minecraft.CheckLatestReleaseAsync(cancellation.Token);
            VersionText.Text = release is null
                ? $"Minecraft {MinecraftService.GameVersion} • offline"
                : $"Minecraft {MinecraftService.GameVersion} • {release.Tag}";

            await minecraft.TrySilentLoginAsync(cancellation.Token);
            RefreshAccount();
            if (release is null) SetStatus("Ready. A cached client will be used if GitHub stays unavailable.", 0);
            else SetStatus("Ready to launch.", 0);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            if (minecraft.Session is null) await minecraft.SignInAsync(cancellation.Token);
            else await minecraft.SignOutAsync(cancellation.Token);
            RefreshAccount();
        }, "Microsoft sign-in failed");
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            await minecraft.LaunchAsync((int)MemorySlider.Value, cancellation.Token);
            RefreshAccount();
        }, "MatHax Reborn could not be launched");
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            SetStatus("Downloading the latest MatHax client…", null);
            string jar = await minecraft.ResolveClientJarAsync(cancellation.Token);
            SetStatus("Creating the official launcher profile…", null);
            string gameDirectory = await officialInstaller.InstallAsync(jar, cancellation.Token);
            SetStatus("Installed. Select “MatHax Reborn” in the official Minecraft Launcher.", 100);
            MessageBox.Show(
                this,
                $"The MatHax Reborn Fabric profile is installed.\n\nGame directory:\n{gameDirectory}\n\nIf the official launcher was open, restart it before selecting the profile.",
                "Installation complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }, "The official launcher profile could not be installed");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo(AppPaths.Instance) { UseShellExecute = true });
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MemoryText is null) return;
        int value = (int)Math.Round(e.NewValue);
        MemoryText.Text = $"{value} GB";
        if (settings is null) return;
        settings.MemoryGb = value;
        settings.Save(AppPaths.Settings);
    }

    private void RefreshAccount()
    {
        string? username = minecraft.Session?.Username ?? minecraft.StoredAccountName;
        bool signedIn = minecraft.Session is not null;
        AccountText.Text = username ?? "Not signed in";
        AccountButton.Content = signedIn ? "Sign out" : "Sign in with Microsoft";
    }

    private async Task RunBusyAsync(Func<Task> action, string errorTitle)
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operation cancelled.", 0);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, 0);
            MessageBox.Show(this, exception.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
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
        LaunchButton.IsEnabled = !value;
        InstallButton.IsEnabled = !value;
        AccountButton.IsEnabled = !value;
        MemorySlider.IsEnabled = !value;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
