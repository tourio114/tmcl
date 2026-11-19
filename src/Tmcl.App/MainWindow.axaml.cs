using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionMetadata;
using CmlLib.Core.VersionLoader;
using CmlLib.Core.FileExtractors;
using Microsoft.Identity.Client;
using Tmcl.Core;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Msal;

namespace Tmcl.App;

public partial class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private CancellationTokenSource? operation;
    private IVersionMetadata[] versions = [];
    private IPublicClientApplication? msal;
    private string? activeClientId;
    private JELoginHandler? login;
    private MSession? session;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        PathBox.Text = settings.GamePath;
        JavaBox.Text = settings.JavaPath;
        ClientBox.Text = settings.ClientId;
        OfflineNameBox.Text = settings.OfflineName;
        AccountModeBox.SelectedIndex = settings.MicrosoftMode ? 1 : 0;
        ThreadsBox.Value = Math.Clamp(settings.Threads, 1, 32);
        MemoryBox.Value = Math.Clamp(settings.Memory, 512, 65536);
        SourceBox.SelectedIndex = settings.SourceMode switch { "BMCLAPI" => 1, "Official" => 2, _ => 0 };
        SourceBox.SelectionChanged += (_, _) => UpdateSourceStatus();
        UpdateSourceStatus();
        settings.Theme ??= settings.Dark ? "Dark" : "Light";
        ThemeBox.SelectedIndex = settings.Theme switch { "Light" => 0, "System" => 2, _ => 1 };
        ApplyTheme();
        StyleBox.SelectedIndex = settings.InterfaceStyle == "Claude" ? 1 : 0;
        ApplyInterfaceStyle();
        StyleBox.SelectionChanged += StyleChanged;
        ThemeBox.SelectionChanged += ThemeChanged;
        LocalOnlyBox.IsCheckedChanged += async (_, _) => { if (!busy) await RunOperation(RefreshVersions); };
        Opened += async (_, _) => await RunOperation(async token =>
        {
            if (settings.SourceMode == "Auto" && (settings.NetworkDetectedAt == null || DateTimeOffset.UtcNow - settings.NetworkDetectedAt > TimeSpan.FromDays(1)))
                await DetectNetwork(token);
            await RefreshVersions(token);
        });
        Closing += (_, _) => { operation?.Cancel(); try { SaveSettings(); } catch (IOException) { } };
    }

    private void ApplyTheme() => Application.Current!.RequestedThemeVariant = settings.Theme switch
    {
        "Light" => ThemeVariant.Light,
        "System" => ThemeVariant.Default,
        _ => ThemeVariant.Dark
    };
    private void ThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        settings.Theme = ThemeBox.SelectedIndex switch { 0 => "Light", 2 => "System", _ => "Dark" };
        settings.Dark = settings.Theme == "Dark";
        ApplyTheme();
        // Appearance can be changed even while a download is in progress.
        try { settings.Save(); StatusText.Text = "主题已更新并保存。"; }
        catch (Exception ex) { StatusText.Text = "主题已切换，但保存失败：" + ex.Message; }
    }
    private void ApplyInterfaceStyle()
    {
        bool claude = settings.InterfaceStyle == "Claude";
        Appearance.Apply(settings.InterfaceStyle);
        PageTitle.FontSize = claude ? 30 : 28;
        PageTitle.FontWeight = claude ? Avalonia.Media.FontWeight.Medium : Avalonia.Media.FontWeight.SemiBold;
        PowerToysBrand.IsVisible = !claude;
        ClaudeBrand.IsVisible = claude;
        UpdatePageHeading();
    }
    private void StyleChanged(object? sender, SelectionChangedEventArgs e)
    {
        settings.InterfaceStyle = StyleBox.SelectedIndex == 1 ? "Claude" : "PowerToys";
        ApplyInterfaceStyle();
        try { settings.Save(); StatusText.Text = "界面风格已更新并保存。"; }
        catch (Exception ex) { StatusText.Text = "界面风格已切换，但保存失败：" + ex.Message; }
    }
    private void UpdatePageHeading()
    {
        bool showSettings = SettingsView.IsVisible;
        bool claude = settings.InterfaceStyle == "Claude";
        PageTitle.Text = showSettings ? (claude ? "外观与偏好" : "启动器设置") : (claude ? "回到你的方块世界" : "开始游戏");
        PageSubtitle.Text = showSettings ? "管理外观、游戏运行环境与下载选项。" : (claude ? "选好版本，让熟悉的冒险继续。" : "管理游戏版本与账号，启动 Minecraft。");
    }
    private void ShowPage(bool showSettings)
    {
        SettingsView.IsVisible = showSettings;
        GameView.IsVisible = !showSettings;
        GameNavButton.Classes.Set("selected", !showSettings);
        SettingsNavButton.Classes.Set("selected", showSettings);
        UpdatePageHeading();
        MainScroll.Offset = Vector.Zero;
    }
    private void GamePageClick(object? sender, RoutedEventArgs e) => ShowPage(false);
    private void SettingsPageClick(object? sender, RoutedEventArgs e) => ShowPage(true);
    private void SaveSettingsClick(object? sender, RoutedEventArgs e)
    {
        try { SaveSettings(); StatusText.Text = "设置已保存，下次启动游戏时生效。"; }
        catch (Exception ex) { StatusText.Text = "设置保存失败：" + ex.Message; }
    }
    private void SaveSettings()
    {
        settings.GamePath = Path.GetFullPath(PathBox.Text?.Trim() is { Length: > 0 } p ? p : settings.GamePath);
        settings.JavaPath = JavaBox.Text?.Trim() ?? "";
        settings.ClientId = ClientBox.Text?.Trim() ?? "";
        settings.OfflineName = OfflineNameBox.Text?.Trim() ?? "Player";
        settings.MicrosoftMode = AccountModeBox.SelectedIndex == 1;
        settings.Threads = Math.Clamp((int)(ThreadsBox.Value ?? 8), 1, 32);
        settings.Memory = Math.Clamp((int)(MemoryBox.Value ?? 4096), 512, 65536);
        settings.SourceMode = SourceBox.SelectedIndex switch { 1 => "BMCLAPI", 2 => "Official", _ => "Auto" };
        settings.Version = VersionBox.SelectedItem as string ?? settings.Version;
        settings.Save();
    }
    private HttpClient CreateClient()
    {
        var client = new HttpClient(new MirrorHandler(settings.UseMirror)) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TMCL/1.0");
        return client;
    }
    private MinecraftLauncher CreateLauncher(HttpClient client)
    {
        var parameters = MinecraftLauncherParameters.CreateDefault(new MinecraftPath(settings.GamePath), client);
        parameters.VersionLoader = LocalOnlyBox.IsChecked == true
            ? new LocalJsonVersionLoader(parameters.MinecraftPath!)
            : new FallbackVersionLoader(parameters.MinecraftPath!, client);
        foreach (var extractor in parameters.FileExtractors!.OfType<JavaFileExtractor>().ToArray()) parameters.FileExtractors!.Remove(extractor);
        parameters.GameInstaller = new VerifiedInstaller(client, settings.Threads) { CheckFileSize = true, CheckFileChecksum = true };
        return new MinecraftLauncher(parameters);
    }
    private async Task RunOperation(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        busy = true;
        operation = new CancellationTokenSource();
        SetBusy(true);
        Progress.Value = 0;
        Progress.IsIndeterminate = true;
        try { SaveSettings(); await action(operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { StatusText.Text = "操作已取消，可重新下载继续校验已有文件。"; }
        catch (Exception ex)
        {
            // Never write full authentication exceptions, request URLs, or launch arguments to disk.
            StatusText.Text = "操作失败：" + ex.Message;
        }
        finally
        {
            Progress.IsIndeterminate = false;
            SetBusy(false);
            busy = false;
            operation.Dispose();
            operation = null;
        }
    }
    private void SetBusy(bool value)
    {
        foreach (var control in new Control[] { DownloadButton, LaunchButton, LoginButton, LogoutButton, RefreshButton, SettingsPanel, AccountModeBox, OfflineNameBox, VersionBox, SnapshotBox, LocalOnlyBox })
            control.IsEnabled = !value;
        CancelButton.IsEnabled = value;
    }
    private async Task RefreshVersions(CancellationToken token)
    {
        StatusText.Text = "正在获取版本列表…";
        using var client = CreateClient();
        var launcher = CreateLauncher(client);
        versions = (await launcher.GetAllVersionsAsync(token)).OrderByDescending(v => v.ReleaseTime).ToArray();
        FilterVersions();
        StatusText.Text = $"已加载 {versions.Length} 个版本，可选择下载。";
    }
    private void FilterVersions()
    {
        var selected = VersionBox.SelectedItem as string ?? settings.Version;
        var names = versions.Where(v => SnapshotBox.IsChecked == true || v.Type is "release" or "local")
            .Select(v => v.Name).ToArray();
        // Preserve an explicitly selected installed/snapshot version across refreshes.
        if (!string.IsNullOrEmpty(selected) && versions.Any(v => v.Name == selected) && !names.Contains(selected))
            names = [selected, .. names];
        VersionBox.ItemsSource = names;
        VersionBox.SelectedItem = names.Contains(selected) ? selected : names.FirstOrDefault();
    }
    private void FilterChanged(object? sender, RoutedEventArgs e) { if (versions.Length > 0) FilterVersions(); }
    private async void RefreshClick(object? sender, RoutedEventArgs e) => await RunOperation(RefreshVersions);
    private async void DownloadClick(object? sender, RoutedEventArgs e) => await RunOperation(ct => InstallOrLaunch(false, ct));
    private async void LaunchClick(object? sender, RoutedEventArgs e) => await RunOperation(ct => InstallOrLaunch(true, ct));
    private async Task InstallOrLaunch(bool launch, CancellationToken token)
    {
        var version = VersionBox.SelectedItem as string;
        if (string.IsNullOrEmpty(version)) throw new InvalidOperationException("请先刷新并选择游戏版本。");
        MSession? launchSession = null;
        if (launch)
        {
            if (settings.MicrosoftMode)
            {
                if (session == null || login == null || activeClientId != settings.ClientId)
                    throw new InvalidOperationException("请先登录微软账号，或切换为离线账号。");
                StatusText.Text = "正在刷新账号授权…";
                launchSession = session = await login.AuthenticateSilently(token);
            }
            else
            {
                if (!Regex.IsMatch(settings.OfflineName, "^[A-Za-z0-9_]{3,16}$"))
                    throw new InvalidOperationException("离线用户名需要 3–16 位英文、数字或下划线。");
                launchSession = MSession.CreateOfflineSession(settings.OfflineName);
            }
        }
        StatusText.Text = "正在校验和下载游戏文件…";
        using var client = CreateClient();
        var launcher = CreateLauncher(client);
        var progress = new Progress<InstallerProgressChangedEventArgs>(p =>
        {
            Progress.IsIndeterminate = false;
            Progress.Value = p.TotalTasks > 0 ? 100d * p.ProgressedTasks / p.TotalTasks : 0;
            StatusText.Text = $"{p.ProgressedTasks:N0} / {p.TotalTasks:N0}  ·  {p.Name}";
        });
        if (!launch || LocalOnlyBox.IsChecked != true)
            await Task.Run(async () => await launcher.InstallAsync(version, progress, null, token), token);
        token.ThrowIfCancellationRequested();
        if (!launch) { Progress.Value = 100; StatusText.Text = $"{version} 下载完成，文件校验通过。"; return; }
        StatusText.Text = "正在启动游戏…";
        var options = new MLaunchOption
        {
            Session = launchSession!, MaximumRamMb = settings.Memory,
            GameLauncherName = "TMCL", GameLauncherVersion = "1.0.0"
        };
        var metadata = await launcher.GetVersionAsync(version, token);
        var major = int.TryParse(metadata.JavaVersion?.MajorVersion, out var required) ? required : 8;
        var javaProgress = new Progress<string>(message => { StatusText.Text = message; Progress.IsIndeterminate = true; });
        options.JavaPath = await new JavaService(client, Path.Combine(Settings.DataPath, "java"))
            .Ensure(major, settings.JavaPath, LocalOnlyBox.IsChecked != true, javaProgress, token);
        var process = await launcher.BuildProcessAsync(version, options, token);
        token.ThrowIfCancellationRequested();
        try { if (!process.Start()) throw new IOException("Java 进程未能启动。"); }
        catch { process.Dispose(); throw; }
        var gamePid = process.Id;
        _ = ObserveGame(process, version);
        StatusText.Text = $"Minecraft {version} 已启动（PID {gamePid}）。";
        Progress.Value = 100;
    }
    private async Task ObserveGame(System.Diagnostics.Process process, string version)
    {
        try
        {
            await process.WaitForExitAsync();
            var exitCode = process.ExitCode;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!busy) StatusText.Text = exitCode == 0 ? $"Minecraft {version} 已关闭。"
                    : $"Minecraft {version} 异常退出（{exitCode}）。请查看游戏目录的 logs / crash-reports。";
            });
        }
        finally { process.Dispose(); }
    }
    private async Task EnsureLogin(CancellationToken token)
    {
        if (!Guid.TryParse(settings.ClientId, out _))
            throw new InvalidOperationException("请在设置中填写你自己的 Microsoft OAuth Client ID；申请方法见 README。离线账号无需配置。");
        if (login != null && activeClientId == settings.ClientId) return;
        session = null;
        Directory.CreateDirectory(Settings.DataPath);
        msal = MsalClientHelper.BuildApplication(settings.ClientId);
        try
        {
            var cacheSettings = new MsalCacheSettings
            {
                CacheDir = Settings.DataPath, CacheFileName = "msal.cache",
                KeyChainServiceName = "TMCL", KeyChainAccountName = settings.ClientId,
                LinuxKeyRingSchema = "com.tmcl.token", LinuxKeyRingLabel = "TMCL Microsoft login"
            };
            var helper = await Microsoft.Identity.Client.Extensions.Msal.MsalCacheHelper.CreateAsync(cacheSettings.ToStorageCreationPropertiesBuilder().Build());
            helper.VerifyPersistence();
            helper.RegisterCache(msal.UserTokenCache);
        }
        catch { msal = MsalClientHelper.BuildApplication(settings.ClientId); StatusText.Text = "系统安全存储不可用，本次登录仅保留在内存中。"; }
        var manager = new InMemoryXboxGameAccountManager(JEGameAccount.FromSessionStorage);
        var account = manager.NewAccount();
        manager.Accounts.Add(account);
        login = new JELoginHandlerBuilder().WithAccountManager(manager)
            .WithOAuthProvider(new XboxAuthNet.Game.Msal.OAuth.MsalCodeFlowProvider(msal)).Build();
        activeClientId = settings.ClientId;
        token.ThrowIfCancellationRequested();
    }
    private async void LoginClick(object? sender, RoutedEventArgs e) => await RunOperation(async token =>
    {
        await EnsureLogin(token);
        StatusText.Text = "请在系统浏览器中完成微软授权…";
        var authenticator = login!.CreateAuthenticatorWithDefaultAccount(token);
        authenticator.AddMsalOAuth(msal!, b => b.SystemBrowser());
        authenticator.AddXboxAuthForJE(b => b.Basic());
        authenticator.AddForceJEAuthenticator();
        session = await authenticator.ExecuteForLauncherAsync();
        AccountText.Text = session.Username;
        AccountModeBox.SelectedIndex = 1;
        StatusText.Text = "微软账号登录成功。";
    });
    private async void LogoutClick(object? sender, RoutedEventArgs e) => await RunOperation(async _ =>
    {
        if (msal != null) await MsalClientHelper.RemoveAccounts(msal);
        login?.AccountManager.ClearAccounts();
        session = null;
        AccountText.Text = "微软账号未登录，离线账号可直接使用。";
        AccountModeBox.SelectedIndex = 0;
        StatusText.Text = "已退出微软账号，已切换为离线账号。";
    });
    private void CancelClick(object? sender, RoutedEventArgs e) { operation?.Cancel(); CancelButton.IsEnabled = false; }
    private async void FolderClick(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择游戏目录", AllowMultiple = false });
        if (result.FirstOrDefault()?.TryGetLocalPath() is { } path) PathBox.Text = path;
    }
    private void UpdateSourceStatus()
    {
        var mode = SourceBox.SelectedIndex;
        var useMirror = mode == 1 || (mode == 0 && (settings.AutoMirror ?? DownloadSources.DefaultMirror(settings.CountryCode)));
        var location = string.IsNullOrEmpty(settings.IPv4) ? "IPv4 地区待检测" : $"IPv4 {settings.IPv4} · {settings.CountryCode}";
        SourceStatusText.Text = $"{location} · 当前{(mode == 0 ? "自动" : "手动")}选择：{(useMirror ? "BMCLAPI" : "官方源")}";
    }
    private async Task DetectNetwork(CancellationToken token)
    {
        try
        {
            StatusText.Text = "正在检测公网 IPv4 和地区…";
            using var client = DownloadSources.CreateIPv4Client();
            var location = await new DownloadSources(client).DetectIPv4(token);
            if (settings.IPv4 != location.IPv4 || settings.CountryCode != location.CountryCode) settings.AutoMirror = null;
            settings.IPv4 = location.IPv4;
            settings.CountryCode = location.CountryCode;
            settings.NetworkDetectedAt = DateTimeOffset.UtcNow;
            settings.Save();
            UpdateSourceStatus();
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or IOException or OperationCanceledException)
        { SourceStatusText.Text = "IPv4 地区检测暂不可用；可手动选择源或进行测速。"; }
    }
    private async void SpeedTestClick(object? sender, RoutedEventArgs e) => await RunOperation(async token =>
    {
        await DetectNetwork(token);
        StatusText.Text = "正在测速官方与 BMCLAPI（同一资源，每源最多 256 KiB）…";
        using var client = DownloadSources.CreateIPv4Client();
        var sources = new DownloadSources(client);
        var official = await sources.Measure(false, token);
        var mirror = await sources.Measure(true, token);
        settings.AutoMirror = DownloadSources.Recommend(official, mirror, settings.UseMirror);
        settings.Save();
        UpdateSourceStatus();
        SourceStatusText.Text += $"\n官方：{official.Display} · BMCLAPI：{mirror.Display}";
        StatusText.Text = SourceBox.SelectedIndex == 0
            ? $"测速完成，自动选择：{(settings.UseMirror ? "BMCLAPI" : "官方源")}。"
            : "测速完成，已保留你手动指定的下载源；切换到自动可使用测速推荐。";
    });
    private async void JavaCheckClick(object? sender, RoutedEventArgs e) => await RunOperation(async token =>
    {
        var version = VersionBox.SelectedItem as string ?? throw new InvalidOperationException("请先选择游戏版本。");
        using var client = CreateClient();
        var metadata = await CreateLauncher(client).GetVersionAsync(version, token);
        var major = int.TryParse(metadata.JavaVersion?.MajorVersion, out var required) ? required : 8;
        var progress = new Progress<string>(message => { StatusText.Text = message; JavaStatusText.Text = message; });
        var java = await new JavaService(client, Path.Combine(Settings.DataPath, "java"))
            .Ensure(major, settings.JavaPath, LocalOnlyBox.IsChecked != true, progress, token);
        JavaStatusText.Text = $"Java {major} / {JavaService.Architecture} · {java}";
        StatusText.Text = $"Java {major} 已就绪。";
    });
    private async void JavaClick(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择 Java 可执行文件", AllowMultiple = false });
        if (result.FirstOrDefault()?.TryGetLocalPath() is { } path) JavaBox.Text = path;
    }
}
