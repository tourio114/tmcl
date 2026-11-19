using System.Net;
using System.Security.Cryptography;
using System.Text;
using CmlLib.Core.Files;
using Tmcl.Core;
using Xunit;

namespace Tmcl.Tests;

public sealed class DownloadTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tmcl-test-" + Guid.NewGuid());
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("verified minecraft asset");
    private GameFile MakeFile(string name = "asset") => new(name)
    {
        Path = Path.Combine(root, name), Url = "https://example.test/" + name,
        Hash = Convert.ToHexString(SHA1.HashData(Data)).ToLowerInvariant(), Size = Data.Length
    };
    public DownloadTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData("https://resources.download.minecraft.net/ab/abcdef", "https://bmclapi2.bangbang93.com/assets/ab/abcdef")]
    [InlineData("https://libraries.minecraft.net/a/b.jar", "https://bmclapi2.bangbang93.com/a/b.jar")]
    [InlineData("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json")]
    [InlineData("https://login.live.com/oauth20_token.srf", "https://login.live.com/oauth20_token.srf")]
    [InlineData("https://api.minecraftservices.com/minecraft/profile", "https://api.minecraftservices.com/minecraft/profile")]
    [InlineData("https://libraries.minecraft.net.evil.test/a", "https://libraries.minecraft.net.evil.test/a")]
    public void RewritesOnlyKnownDownloadHosts(string url, string expected) => Assert.Equal(expected, MirrorHandler.Rewrite(new Uri(url)).ToString());

    [Fact]
    public async Task CorruptDownloadIsRetriedAndReplacesInvalidExistingFile()
    {
        int calls = 0;
        using var client = new HttpClient(new Stub((_, _) => Task.FromResult(Ok(Interlocked.Increment(ref calls) == 1 ? new byte[Data.Length] : Data))));
        var file = MakeFile();
        await File.WriteAllTextAsync(file.Path!, "broken");
        await new VerifiedInstaller(client, 4).Install([file], null, null, default);
        Assert.Equal(2, calls);
        Assert.Equal(Data, await File.ReadAllBytesAsync(file.Path!));
        Assert.Empty(Directory.GetFiles(root, "*.part"));
    }
    [Fact]
    public async Task ValidFileIsSkipped()
    {
        using var client = new HttpClient(new Stub((_, _) => throw new InvalidOperationException("must not download")));
        var file = MakeFile();
        await File.WriteAllBytesAsync(file.Path!, Data);
        await new VerifiedInstaller(client, 2).Install([file, file], null, null, default);
    }
    [Fact]
    public async Task ExhaustedChecksumFailuresNeverPublishFile()
    {
        using var client = new HttpClient(new Stub((_, _) => Task.FromResult(Ok(new byte[Data.Length]))));
        var file = MakeFile();
        await Assert.ThrowsAsync<IOException>(async () => await new VerifiedInstaller(client, 2).Install([file], null, null, default));
        Assert.False(File.Exists(file.Path));
        Assert.Empty(Directory.GetFiles(root));
    }
    [Fact]
    public async Task ParallelDownloadsRespectConfiguredLimit()
    {
        int active = 0, peak = 0;
        using var client = new HttpClient(new Stub(async (_, ct) =>
        {
            int count = Interlocked.Increment(ref active);
            int seen;
            do { seen = peak; } while (count > seen && Interlocked.CompareExchange(ref peak, count, seen) != seen);
            try { await Task.Delay(50, ct); return Ok(Data); }
            finally { Interlocked.Decrement(ref active); }
        }));
        await new VerifiedInstaller(client, 3).Install(Enumerable.Range(0, 12).Select(i => MakeFile(i.ToString())), null, null, default);
        Assert.InRange(peak, 2, 3);
        Assert.Equal(12, Directory.GetFiles(root).Length);
    }
    [Fact]
    public async Task CancellationCleansTemporaryFileAndInstallerCanRunAgain()
    {
        using var cts = new CancellationTokenSource();
        bool block = true;
        using var client = new HttpClient(new Stub(async (_, ct) =>
        {
            if (block) { cts.Cancel(); await Task.Delay(1000, ct); }
            return Ok(Data);
        }));
        var installer = new VerifiedInstaller(client, 2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await installer.Install([MakeFile()], null, null, cts.Token));
        Assert.Empty(Directory.GetFiles(root));
        block = false;
        await installer.Install([MakeFile()], null, null, default);
        Assert.Single(Directory.GetFiles(root));
    }
    [Fact]
    public async Task MirrorFailureFallsBackToOfficialAndPreservesHeaders()
    {
        var hosts = new List<string>();
        using var client = new HttpClient(new MirrorHandler(true, new Stub((req, _) =>
        {
            hosts.Add(req.RequestUri!.Host);
            Assert.Equal("TMCL", req.Headers.UserAgent.ToString());
            return Task.FromResult(req.RequestUri.Host.StartsWith("bmclapi") ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(Data));
        })));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TMCL");
        using var response = await client.GetAsync("https://libraries.minecraft.net/example.jar");
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(new[] { "bmclapi2.bangbang93.com", "bmclapi2.bangbang93.com", "libraries.minecraft.net" }, hosts);
    }
    [Fact]
    public async Task SlowMirrorFallsBackAndUserCancellationIsNotRetried()
    {
        var hosts = new List<string>();
        using var client = new HttpClient(new MirrorHandler(true, new Stub(async (req, ct) =>
        {
            hosts.Add(req.RequestUri!.Host);
            if (req.RequestUri.Host.StartsWith("bmclapi")) await Task.Delay(5000, ct);
            return Ok(Data);
        }), TimeSpan.FromMilliseconds(30)));
        using var response = await client.GetAsync("https://libraries.minecraft.net/a.jar");
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(new[] { "bmclapi2.bangbang93.com", "libraries.minecraft.net" }, hosts);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://libraries.minecraft.net/b.jar", cancellation.Token));
    }
    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
