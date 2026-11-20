using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using Microsoft.Identity.Client;
using Tmcl.Auth;
using Xunit;

namespace Tmcl.Tests;

public sealed class AuthenticationTests
{
    private const string Client = "27f151db-8451-45e6-b204-1c4398334245"; // Arbitrary test-only identifier; never used on the network.
    private static LoginResult Valid() => new(new MSession { Username = "TestPlayer", UUID = "0123456789abcdef0123456789abcdef", AccessToken = "synthetic-test-token" }, true);
    private static MicrosoftLoginService Service(FakeBackend backend) => new((_, _) => Task.FromResult<IMicrosoftLoginBackend>(backend));
    private static Task<LoginResult> Login(MicrosoftLoginService service, string id = Client, CancellationToken ct = default) => service.SignIn(id, true, false, MicrosoftLoginMethod.Browser, null, ct);

    [Theory]
    [InlineData("")] [InlineData("not-a-client-id")] [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidConfigurationNeverInitializesBackend(string id)
    {
        var service = new MicrosoftLoginService((_, _) => throw new Exception("must not initialize"));
        var error = await Assert.ThrowsAsync<AuthenticationException>(() => Login(service, id));
        Assert.Equal(AuthenticationFailure.Configuration, error.Failure);
        Assert.Null(service.Session);
    }
    [Fact]
    public async Task EquivalentClientIdsReuseBackend()
    {
        int created = 0;
        var backend = new FakeBackend();
        var service = new MicrosoftLoginService((_, _) => { created++; return Task.FromResult<IMicrosoftLoginBackend>(backend); });
        await Login(service);
        await Login(service, "  " + Client.ToUpperInvariant() + "  ");
        Assert.Equal(1, created);
        Assert.Equal(Client, service.ClientId);
    }
    [Fact]
    public async Task ChangingApplicationCannotReusePreviousSession()
    {
        var first = new FakeBackend();
        var service = new MicrosoftLoginService((id, _) => id == Client ? Task.FromResult<IMicrosoftLoginBackend>(first) : throw new IOException("secret-second-app-error"));
        await Login(service);
        var error = await Assert.ThrowsAsync<AuthenticationException>(() => Login(service, Guid.NewGuid().ToString()));
        Assert.True(first.Disposed);
        Assert.Null(service.Session);
        Assert.DoesNotContain("secret-second-app-error", error.ToString());
    }
    [Fact]
    public async Task RefreshDoesNotOpenInteractiveAuthentication()
    {
        var backend = new FakeBackend();
        var service = Service(backend);
        await service.SignIn(Client, false, false, MicrosoftLoginMethod.DeviceCode, null, default);
        Assert.False(backend.AllowInteraction);
        Assert.False(backend.ChooseAccount);
        Assert.Equal(MicrosoftLoginMethod.DeviceCode, backend.Method);
    }
    [Fact]
    public async Task SwitchingAccountExplicitlyBypassesSilentChoice()
    {
        var backend = new FakeBackend();
        await Service(backend).SignIn(Client, true, true, MicrosoftLoginMethod.Browser, null, default);
        Assert.True(backend.AllowInteraction);
        Assert.True(backend.ChooseAccount);
    }
    [Fact]
    public async Task CancellationAfterBackendCompletionDoesNotPublishSession()
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new FakeBackend { Login = _ => { cancellation.Cancel(); return Task.FromResult(Valid()); } };
        var service = Service(backend);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Login(service, ct: cancellation.Token));
        Assert.Null(service.Session);
    }
    [Fact]
    public async Task OfflineSessionCannotBeMistakenForOnlineLogin()
    {
        var service = Service(new FakeBackend { Login = _ => Task.FromResult(new LoginResult(MSession.CreateOfflineSession("Player"), false)) });
        var error = await Assert.ThrowsAsync<AuthenticationException>(() => Login(service));
        Assert.Equal(AuthenticationFailure.GameProfile, error.Failure);
        Assert.Null(service.Session);
    }
    [Fact]
    public async Task CacheLogoutFailureStillClearsSessionAndCanBeRetried()
    {
        var backend = new FakeBackend { FailLogout = true };
        var service = Service(backend);
        await Login(service);
        await Assert.ThrowsAsync<AuthenticationException>(() => service.SignOut(Client, default));
        Assert.Null(service.Session);
        Assert.False(backend.Disposed);
        backend.FailLogout = false;
        await service.SignOut(Client, default);
        Assert.True(backend.Disposed);
        Assert.Null(service.ClientId);
        Assert.Equal(2, backend.LogoutCalls);
    }
    [Fact]
    public async Task LogoutAfterRestartClearsConfiguredCacheWithoutSigningIn()
    {
        var backend = new FakeBackend { Login = _ => throw new Exception("must not sign in") };
        await Service(backend).SignOut(Client, default);
        Assert.Equal(1, backend.LogoutCalls);
        Assert.True(backend.Disposed);
    }
    [Fact]
    public async Task CachedInteractionRequiredIsActionableAndTokenSafe()
    {
        var backend = new FakeBackend { Login = _ => throw new MsalUiRequiredException("invalid_grant", "secret-refresh-token") };
        var error = await Assert.ThrowsAsync<AuthenticationException>(() => Login(Service(backend)));
        Assert.Equal(AuthenticationFailure.InteractionRequired, error.Failure);
        Assert.DoesNotContain("secret-refresh-token", error.ToString());
    }
    [Fact]
    public async Task BackendCallsCannotOverlapWithLogout()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend { Login = async _ => { entered.SetResult(); await release.Task; return Valid(); } };
        var service = Service(backend);
        var login = Login(service);
        await entered.Task;
        var logout = service.SignOut(Client, default);
        Assert.Equal(0, backend.LogoutCalls);
        release.SetResult();
        await Task.WhenAll(login, logout);
        Assert.Null(service.Session);
        Assert.Equal(1, backend.LogoutCalls);
    }
    [Theory]
    [InlineData("https://microsoft.com/devicelogin", true)]
    [InlineData("https://www.microsoft.com/link", true)]
    [InlineData("https://login.microsoftonline.com/common/oauth2", true)]
    [InlineData("https://microsoft.com.evil.test/link", false)]
    [InlineData("https://microsoft.com@evil.test/link", false)]
    [InlineData("http://microsoft.com/link", false)]
    [InlineData("https://microsoft.com:444/link", false)]
    [InlineData("file:///etc/passwd", false)]
    public void DeviceCodeLinksOnlyOpenMicrosoftHttps(string url, bool expected) => Assert.Equal(expected, MicrosoftLoginService.IsVerificationUrl(url));
    [Theory]
    [InlineData("Invalid app registration; bearer SECRET", AuthenticationFailure.ApplicationPermission)]
    [InlineData("XErr 2148916233 SECRET", AuthenticationFailure.XboxProfile)]
    [InlineData("XErr 2148916238 SECRET", AuthenticationFailure.FamilyRestriction)]
    [InlineData("XErr 2148916235 SECRET", AuthenticationFailure.RegionRestriction)]
    [InlineData("The user doesn't own the game SECRET", AuthenticationFailure.GameProfile)]
    [InlineData("unrecognized SECRET", AuthenticationFailure.Unknown)]
    public void ReportsKnownFailuresWithoutLeakingRawException(string text, AuthenticationFailure expected)
    {
        var error = AuthenticationErrors.Translate(new Exception("outer", new Exception(text)));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("SECRET", error.ToString());
    }
    [Fact]
    public void NonInteractiveTimeoutIsNotMisreportedAsUserCancellation() => Assert.Equal(AuthenticationFailure.Timeout, AuthenticationErrors.Translate(new TimeoutException()).Failure);
    [Fact]
    public void NetworkFailureDoesNotExposeRequestUrl() => Assert.DoesNotContain("access_token", AuthenticationErrors.Translate(new HttpRequestException("https://example.test/?access_token=SECRET")).ToString());
    [Fact]
    public void NoRawMinecraftServiceResponseIsDisplayed() => Assert.DoesNotContain("SECRET", AuthenticationErrors.Translate(new JEAuthException("FORBIDDEN SECRET")).ToString());

    private sealed class FakeBackend : IMicrosoftLoginBackend
    {
        public Func<CancellationToken, Task<LoginResult>> Login { get; set; } = _ => Task.FromResult(Valid());
        public bool AllowInteraction { get; private set; }
        public bool ChooseAccount { get; private set; }
        public MicrosoftLoginMethod Method { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailLogout { get; set; }
        public int LogoutCalls { get; private set; }
        public Task<LoginResult> Authenticate(bool allowInteraction, bool chooseAccount, MicrosoftLoginMethod method, IProgress<LoginProgress>? progress, CancellationToken token)
        { AllowInteraction = allowInteraction; ChooseAccount = chooseAccount; Method = method; return Login(token); }
        public Task SignOut(CancellationToken token)
        { LogoutCalls++; if (FailLogout) throw new IOException("secret-cache-error"); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }
}
