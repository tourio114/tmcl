using System.Net;
namespace Tmcl.Core;

public sealed class MirrorHandler(bool enabled, HttpMessageHandler? inner = null, TimeSpan? mirrorTimeout = null) : DelegatingHandler(inner ?? new HttpClientHandler())
{
    private long unavailableUntil;
    public static Uri Rewrite(Uri uri) => uri.Host.ToLowerInvariant() switch
    {
        "resources.download.minecraft.net" => new Uri("https://bmclapi2.bangbang93.com/assets" + uri.PathAndQuery),
        "libraries.minecraft.net" or "launchermeta.mojang.com" or "launcher.mojang.com" or
        "piston-meta.mojang.com" or "piston-data.mojang.com" => new Uri("https://bmclapi2.bangbang93.com" + uri.PathAndQuery),
        _ => uri
    };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var original = request.RequestUri!;
        var mirrored = enabled ? Rewrite(original) : original;
        for (int attempt = 0; ; attempt++)
        {
            bool useMirror = mirrored != original && attempt < 2 && DateTime.UtcNow.Ticks >= Interlocked.Read(ref unavailableUntil);
            using var copy = new HttpRequestMessage(request.Method, useMirror ? mirrored : original);
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(useMirror ? mirrorTimeout ?? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(30));
            try
            {
                var response = await base.SendAsync(copy, timeout.Token);
                if (response.IsSuccessStatusCode || attempt == 2 ||
                    (response.StatusCode is not HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500 && mirrored == original))
                    return response;
                if (useMirror && response.StatusCode == HttpStatusCode.TooManyRequests)
                    Interlocked.Exchange(ref unavailableUntil, DateTime.UtcNow.AddSeconds(30).Ticks);
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 2) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && attempt < 2)
            {
                // A slow redirected mirror node must not stall every asset for minutes.
                if (useMirror) Interlocked.Exchange(ref unavailableUntil, DateTime.UtcNow.AddSeconds(30).Ticks);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(350 * (attempt + 1)), token);
        }
    }
}
