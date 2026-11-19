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
        MirrorBox.IsChecked = settings.Mirror;
        ApplyTheme();
        LocalOnlyBox.IsCheckedChanged += async (_, _) => { if (!busy) await RunOperation(RefreshVersions); };
        Opened += async (_, _) => await RunOperation(RefreshVersions);
        Closing += (_, _) => { operation?.Cancel(); try { SaveSettings(); } catch (IOException) { } };
    }

    private void ApplyTheme() => Application.Current!.RequestedThemeVariant = settings.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    private void ThemeClick(object? sender, RoutedEventArgs e)
    {
        settings.Dark = !settings.Dark;
        ApplyTheme();
        try { SaveSettings(); } catch (Exception ex) { StatusText.Text = "主题已切换，但设置保存失败：" + ex.Message; }
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
        settings.Mirror = MirrorBox.IsChecked == true;
        settings.Version = VersionBox.SelectedItem as string ?? settings.Version;
        settings.Save();
    }
    private HttpClient CreateClient()
    {
        var client = new HttpClient(new MirrorHandler(settings.Mirror)) { Timeout = TimeSpan.FromMinutes(5) };
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
        catch (OperationCanceledException) { StatusText.Text = "操作已取消，可重新下载继续校验已有文件。"; }
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
        using var process = await launcher.BuildProcessAsync(version, options, token);
        token.ThrowIfCancellationRequested();
        process.Start();
        StatusText.Text = $"Minecraft {version} 已启动（PID {process.Id}）。";
        Progress.Value = 100;
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
        AccountText.Text = "尚未登录";
        AccountModeBox.SelectedIndex = 0;
        StatusText.Text = "已退出微软账号，已切换为离线账号。";
    });
    private void CancelClick(object? sender, RoutedEventArgs e) { operation?.Cancel(); CancelButton.IsEnabled = false; }
    private async void FolderClick(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择游戏目录", AllowMultiple = false });
        if (result.FirstOrDefault()?.TryGetLocalPath() is { } path) PathBox.Text = path;
    }
    private async void JavaClick(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择 Java 可执行文件", AllowMultiple = false });
        if (result.FirstOrDefault()?.TryGetLocalPath() is { } path) JavaBox.Text = path;
    }
}
