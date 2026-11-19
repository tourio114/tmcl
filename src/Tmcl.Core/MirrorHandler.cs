using System.Net;

namespace Tmcl.Core;

public sealed class MirrorHandler(bool enabled, HttpMessageHandler? inner = null) : DelegatingHandler(inner ?? new HttpClientHandler())
{
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
        // Each retry uses a fresh request; authentication hosts are never rewritten.
        for (int attempt = 0; ; attempt++)
        {
            using var copy = new HttpRequestMessage(request.Method, attempt < 2 ? mirrored : original);
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            try
            {
                var response = await base.SendAsync(copy, token);
                if (response.IsSuccessStatusCode || attempt == 2 ||
                    (response.StatusCode is not HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500 && mirrored == original))
                    return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 2) { }
            await Task.Delay(TimeSpan.FromMilliseconds(350 * (attempt + 1)), token);
        }
    }
}
