using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Msal;

namespace Tmcl.Auth;

internal sealed class MsalLoginBackend : IMicrosoftLoginBackend
{
    private readonly IPublicClientApplication app;
    private readonly MsalCacheHelper? cache;
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(45) };
    private string? selectedAccount;

    private MsalLoginBackend(IPublicClientApplication app, MsalCacheHelper? cache) => (this.app, this.cache) = (app, cache);

    public static async Task<IMicrosoftLoginBackend> Create(string clientId, string dataDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var app = MsalClientHelper.BuildApplication(clientId);
        MsalCacheHelper? cache = null;
        try
        {
            var directory = Path.Combine(dataDirectory, "auth", clientId);
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var settings = new MsalCacheSettings
            {
                CacheDir = directory, CacheFileName = "msal.cache",
                KeyChainServiceName = "TMCL", KeyChainAccountName = clientId,
                LinuxKeyRingSchema = "com.tmcl.token", LinuxKeyRingLabel = "TMCL Microsoft login",
                LinuxKeyRingAttr1 = new("ClientId", clientId), LinuxKeyRingAttr2 = new("ProductGroup", "TMCL")
            };
            var candidate = await MsalCacheHelper.CreateAsync(settings.ToStorageCreationPropertiesBuilder().Build());
            candidate.VerifyPersistence();
            candidate.RegisterCache(app.UserTokenCache);
            cache = candidate;
        }
        catch
        {
            // No plaintext fallback. A clean MSAL instance prevents partially registered cache hooks.
            app = MsalClientHelper.BuildApplication(clientId);
        }
        token.ThrowIfCancellationRequested();
        return new MsalLoginBackend(app, cache);
    }

    public async Task<LoginResult> Authenticate(bool allowInteraction, bool chooseAccount, MicrosoftLoginMethod method, IProgress<LoginProgress>? progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var ct = timeout.Token;
        try
        {
            AuthenticationResult? oauth = null;
            var accounts = (await app.GetAccountsAsync()).ToArray();
            var account = accounts.FirstOrDefault(a => a.HomeAccountId.Identifier == selectedAccount) ?? (accounts.Length == 1 ? accounts[0] : null);
            if (!chooseAccount && account != null)
            {
                progress?.Report(new("正在恢复微软授权…"));
                try { oauth = await app.AcquireTokenSilent(MsalClientHelper.XboxScopes, account).ExecuteAsync(ct); }
                catch (MsalUiRequiredException) { }
            }
            if (oauth == null)
            {
                if (!allowInteraction) throw AuthenticationErrors.InteractionRequired();
                if (method == MicrosoftLoginMethod.DeviceCode)
                {
                    oauth = await app.AcquireTokenWithDeviceCode(MsalClientHelper.XboxScopes, code =>
                    {
                        progress?.Report(new("请在浏览器中打开验证地址，输入下面的代码，并使用希望登录的微软账号授权。", code.UserCode, code.VerificationUrl));
                        return Task.CompletedTask;
                    }).ExecuteAsync(ct);
                }
                else
                {
                    progress?.Report(new("请在系统浏览器中完成微软登录，授权后会自动返回启动器。"));
                    oauth = await app.AcquireTokenInteractive(MsalClientHelper.XboxScopes)
                        .WithUseEmbeddedWebView(false).WithPrompt(Prompt.SelectAccount).ExecuteAsync(ct);
                }
            }
            ct.ThrowIfCancellationRequested();
            progress?.Report(new("微软授权已完成，正在验证 Xbox 与 Minecraft Java 版档案…"));
            var manager = new InMemoryXboxGameAccountManager(JEGameAccount.FromSessionStorage);
            manager.Accounts.Add(manager.NewAccount());
            using var cancellation = ct.Register(client.CancelPendingRequests);
            var handler = new JELoginHandlerBuilder().WithHttpClient(client).WithAccountManager(manager)
                .WithOAuthProvider(new XboxAuthNet.Game.Msal.OAuth.MsalCodeFlowProvider(app)).Build();
            var authenticator = handler.CreateAuthenticatorWithDefaultAccount(ct);
            authenticator.AddMsalOAuth(app, b => b.FromResult(oauth));
            authenticator.AddXboxAuthForJE(b => b.Basic());
            authenticator.AddForceJEAuthenticator();
            var session = await authenticator.ExecuteForLauncherAsync();
            ct.ThrowIfCancellationRequested();
            selectedAccount = oauth.Account.HomeAccountId.Identifier;
            return new(session, cache != null);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException(); }
    }
    public async Task SignOut(CancellationToken token)
    {
        foreach (var account in await app.GetAccountsAsync())
        {
            token.ThrowIfCancellationRequested();
            await app.RemoveAsync(account);
        }
        selectedAccount = null;
    }
    public void Dispose()
    {
        cache?.UnregisterCache(app.UserTokenCache);
        client.Dispose();
    }
}
