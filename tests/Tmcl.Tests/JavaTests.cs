using Tmcl.Core;
using Xunit;
namespace Tmcl.Tests;
public class JavaTests
{
    [Theory]
    [InlineData("openjdk version \"1.8.0_492\"", 8)]
    [InlineData("java version \"1.8.0_401\"", 8)]
    [InlineData("openjdk version \"17.0.18\" 2026-01-20", 17)]
    [InlineData("openjdk version \"21.0.13\"", 21)]
    [InlineData("java.version = 25.0.1", 25)]
    [InlineData("not java", 0)]
    public void ReadsLegacyAndModernJavaVersions(string output, int expected) => Assert.Equal(expected, JavaService.ParseMajor(output));

    [Fact]
    public async Task InvalidExplicitJavaDoesNotDownloadOrSilentlyOverride()
    {
        using var client = new HttpClient(new NoNetwork());
        var service = new JavaService(client, Path.GetTempPath());
        await Assert.ThrowsAsync<IOException>(() => service.Ensure(21, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "java"), true, null, default));
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("Should not access network");
    }
}
