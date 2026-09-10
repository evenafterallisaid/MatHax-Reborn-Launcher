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
    private readonly LauncherUpdateService updater = new();
    private LauncherUpdate? availableUpdate;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        AppPaths.EnsureCreated();

        settings = LauncherSettings.Load(AppPaths.Settings);
        MemorySlider.Value = Math.Clamp(settings.MemoryGb, 2, 16);
        CompatibilityToggle.IsChecked = settings.WideServerSupport;
        RefreshCompatibilityText();

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

            try
            {
                availableUpdate = await updater.CheckAsync(cancellation.Token);
                if (availableUpdate is not null)
                {
                    UpdateButton.Content = $"Update {availableUpdate.Tag}";
                    UpdateButton.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                // An update check should never prevent the game from launching.
            }
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
            await minecraft.LaunchAsync(
                (int)MemorySlider.Value,
                CompatibilityToggle.IsChecked == true,
                cancellation.Token);
            RefreshAccount();
        }, "MatHax Reborn could not be launched");
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            SetStatus("Downloading the latest MatHax client…", null);
            string jar = await minecraft.ResolveClientJarAsync(cancellation.Token);
            ProtocolCompatibilityMod? protocolMod = CompatibilityToggle.IsChecked == true
                ? await minecraft.ResolveProtocolCompatibilityAsync(cancellation.Token)
                : null;
            SetStatus("Creating the official launcher profile…", null);
            string gameDirectory = await officialInstaller.InstallAsync(
                jar,
                protocolMod?.FilePath,
                cancellation.Token);
            SetStatus("Installed. Select “MatHax Reborn” in the official Minecraft Launcher.", 100);
            MessageBox.Show(
                this,
                $"The MatHax Reborn Fabric profile is installed.\n\nGame directory:\n{gameDirectory}\n\nIf the official launcher was open, restart it before selecting the profile.",
                "Installation complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }, "The official launcher profile could not be installed");
    }

    private void ModsButton_Click(object sender, RoutedEventArgs e)
    {
        ModsWindow window = new() { Owner = this };
        window.ShowDialog();
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            await minecraft.RepairAsync(
                CompatibilityToggle.IsChecked == true,
                cancellation.Token);
            MessageBox.Show(
                this,
                "Minecraft, Fabric, MatHax, and enabled compatibility files were checked and repaired.",
                "Repair complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }, "MatHax Reborn could not be repaired");
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is null) return;
        await RunBusyAsync(async () =>
        {
            SetStatus($"Downloading launcher {availableUpdate.Tag}…", 0);
            await updater.StageAndRestartAsync(
                availableUpdate,
                new Progress<double>(value => SetStatus($"Downloading launcher {availableUpdate.Tag}…", value)),
                cancellation.Token);
            SetStatus("Applying update and restarting…", 100);
            Close();
        }, "The launcher could not be updated");
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

    private void CompatibilityToggle_Click(object sender, RoutedEventArgs e)
    {
        if (settings is null) return;
        settings.WideServerSupport = CompatibilityToggle.IsChecked == true;
        settings.Save(AppPaths.Settings);
        RefreshCompatibilityText();
        SetStatus(
            settings.WideServerSupport
                ? "Wide server support enabled. ViaFabricPlus will be installed at launch."
                : "Native 26.2 networking selected.",
            0);
    }

    private void RefreshCompatibilityText()
    {
        CompatibilityText.Text = CompatibilityToggle.IsChecked == true
            ? "ViaFabricPlus • Auto-detects Classic through 26.2"
            : "Native Minecraft 26.2 protocol";
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
        ModsButton.IsEnabled = !value;
        RepairButton.IsEnabled = !value;
        AccountButton.IsEnabled = !value;
        MemorySlider.IsEnabled = !value;
        CompatibilityToggle.IsEnabled = !value;
        UpdateButton.IsEnabled = !value;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ShellClip is not null) ShellClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
