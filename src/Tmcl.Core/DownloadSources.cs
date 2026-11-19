using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Tmcl.Core;

public sealed record NetworkLocation(string IPv4, string CountryCode);
public sealed record SourceSpeed(bool Mirror, double? BytesPerSecond)
{
    public string Display => BytesPerSecond is { } speed ? $"{speed / 1048576:F2} MB/s" : "不可用 / 超时";
}
public sealed class DownloadSources(HttpClient directClient)
{
    // Download the same asset from both sources; include connection and first-byte latency.
    public static readonly Uri ProbeAsset = new("https://resources.download.minecraft.net/32/32a43d1648c3d8c1e47357ac59dd9a063b43836a");
    public static bool DefaultMirror(string? countryCode) => string.Equals(countryCode, "CN", StringComparison.OrdinalIgnoreCase);
    public static bool Recommend(SourceSpeed official, SourceSpeed mirror, bool fallback) => (official.BytesPerSecond, mirror.BytesPerSecond) switch
    {
        (null, null) => fallback,
        (null, _) => true,
        (_, null) => false,
        (var a, var b) => b > a
    };
    public static HttpClient CreateIPv4Client()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(6),
            ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, AddressFamily.InterNetwork, token);
                foreach (var address in addresses)
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); if (token.IsCancellationRequested) throw; }
                }
                throw new HttpRequestException("无法建立 IPv4 连接。");
            }
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TMCL/1.0");
        return client;
    }
    public static NetworkLocation ParseTrace(string trace)
    {
        var values = trace.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1].Trim());
        if (!values.TryGetValue("ip", out var ip) || !IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new IOException("未检测到公网 IPv4 地址。");
        if (!values.TryGetValue("loc", out var country) || country.Length != 2) throw new IOException("无法确定 IPv4 所在地区。");
        return new NetworkLocation(ip, country.ToUpperInvariant());
    }
    public async Task<NetworkLocation> DetectIPv4(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        var trace = await directClient.GetStringAsync("https://www.cloudflare.com/cdn-cgi/trace", timeout.Token);
        return ParseTrace(trace);
    }
    public async Task<SourceSpeed> Measure(bool mirror, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Get, mirror ? MirrorHandler.Rewrite(ProbeAsset) : ProbeAsset);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 262143);
            using var response = await directClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[16384];
            long bytes = 0;
            while (bytes < 262144)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, 262144 - bytes)), timeout.Token);
                if (count == 0) break;
                bytes += count;
            }
            stopwatch.Stop();
            return new SourceSpeed(mirror, bytes > 0 ? bytes / Math.Max(stopwatch.Elapsed.TotalSeconds, .001) : null);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new SourceSpeed(mirror, null); }
        catch (HttpRequestException) { return new SourceSpeed(mirror, null); }
        catch (IOException) { return new SourceSpeed(mirror, null); }
    }
}
