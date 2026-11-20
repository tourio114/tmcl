using CmlLib.Core.Auth;
namespace Tmcl.Auth;

public enum MicrosoftLoginMethod { Browser, DeviceCode }
public sealed record LoginProgress(string Message, string? UserCode = null, string? VerificationUrl = null);
public sealed record LoginResult(MSession Session, bool SecureCacheAvailable);

public interface IMicrosoftLoginBackend : IDisposable
{
    Task<LoginResult> Authenticate(bool allowInteraction, bool chooseAccount, MicrosoftLoginMethod method, IProgress<LoginProgress>? progress, CancellationToken token);
    Task SignOut(CancellationToken token);
}

public sealed class MicrosoftLoginService
{
    private readonly Func<string, CancellationToken, Task<IMicrosoftLoginBackend>> factory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IMicrosoftLoginBackend? backend;
    public string? ClientId { get; private set; }
    public MSession? Session { get; private set; }

    public MicrosoftLoginService(string dataDirectory) : this((id, ct) => MsalLoginBackend.Create(id, dataDirectory, ct)) { }
    public MicrosoftLoginService(Func<string, CancellationToken, Task<IMicrosoftLoginBackend>> factory) => this.factory = factory;

    public static bool IsVerificationUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Host is "microsoft.com" or "www.microsoft.com" or "login.microsoftonline.com";

    public static string NormalizeClientId(string? value)
    {
        if (!Guid.TryParse(value?.Trim(), out var id) || id == Guid.Empty)
            throw new AuthenticationException(AuthenticationFailure.Configuration, "请在“启动器设置 → 微软登录应用 ID”填写你自己的 Client ID。离线账号不需要此配置。不要填写 Client Secret。");
        return id.ToString("D");
    }

    public async Task<LoginResult> SignIn(string? clientId, bool allowInteraction, bool chooseAccount, MicrosoftLoginMethod method, IProgress<LoginProgress>? progress, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var normalized = NormalizeClientId(clientId);
            if (ClientId != normalized)
            {
                Session = null;
                var obsolete = backend;
                backend = null;
                ClientId = normalized;
                obsolete?.Dispose();
            }
            backend ??= await factory(normalized, token);
            var result = await backend.Authenticate(allowInteraction, chooseAccount, method, progress, token);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(result.Session.Username) || string.IsNullOrWhiteSpace(result.Session.UUID) ||
                string.IsNullOrWhiteSpace(result.Session.AccessToken) || result.Session.AccessToken == "access_token")
                throw new AuthenticationException(AuthenticationFailure.GameProfile, "登录未返回有效的 Java 版游戏档案，请重新登录。");
            Session = result.Session;
            return result;
        }
        catch (OperationCanceledException) { Session = null; throw; }
        catch (Exception ex) { Session = null; throw AuthenticationErrors.Translate(ex); }
        finally { gate.Release(); }
    }

    public async Task SignOut(string? configuredClientId, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            Session = null;
            if (backend == null && Guid.TryParse(configuredClientId?.Trim(), out var configured) && configured != Guid.Empty)
            {
                ClientId = configured.ToString("D");
                backend = await factory(ClientId, token);
            }
            // Retain the backend if clearing the secure cache fails so the user can retry logout.
            if (backend != null) await backend.SignOut(token);
            backend?.Dispose();
            backend = null;
            ClientId = null;
        }
        catch (OperationCanceledException) { throw; }
        catch { throw new AuthenticationException(AuthenticationFailure.Unknown, "本次登录已退出，但系统授权缓存未能完全清除，请重试退出账号。"); }
        finally { gate.Release(); }
    }
}
