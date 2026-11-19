using System.Security.Cryptography;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;

namespace Tmcl.Core;

public sealed class VerifiedInstaller : GameInstallerBase
{
    private readonly HttpClient client;
    private readonly int concurrency;
    public VerifiedInstaller(HttpClient client, int concurrency) : base(client)
    { this.client = client; this.concurrency = concurrency; }
    protected override async ValueTask Install(IEnumerable<GameFile> gameFiles, CancellationToken token)
    {
        var files = gameFiles.Where(f => !string.IsNullOrEmpty(f.Path) && !IsExcludedPath(f.Path))
            .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        int done = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(concurrency, 1, 32), CancellationToken = token
        }, async (file, ct) =>
        {
            if (NeedUpdate(file))
            {
                if (string.IsNullOrEmpty(file.Url)) throw new IOException($"缺少文件下载地址：{file.Name}");
                await DownloadVerified(file, ct);
            }
            ct.ThrowIfCancellationRequested();
            await file.ExecuteUpdateTask(ct);
            FireFileProgress(files.Length, Interlocked.Increment(ref done), file.Name, InstallerEventType.Done);
        });
    }

    private async Task DownloadVerified(GameFile file, CancellationToken token)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.Path!)!);
        var temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using (var source = await response.Content.ReadAsStreamAsync(token))
                    await using (var destination = File.Create(temporary))
                        await source.CopyToAsync(destination, token);
                    token.ThrowIfCancellationRequested();
                    if (file.Size > 0 && new FileInfo(temporary).Length != file.Size)
                        throw new IOException($"文件长度不正确：{file.Name}");
                    if (!string.IsNullOrEmpty(file.Hash))
                    {
                        await using var stream = File.OpenRead(temporary);
                        var hash = Convert.ToHexString(await SHA1.HashDataAsync(stream, token));
                        if (!hash.Equals(file.Hash, StringComparison.OrdinalIgnoreCase))
                            throw new IOException($"SHA-1 校验失败：{file.Name}");
                    }
                    File.Move(temporary, file.Path!, true);
                    return;
                }
                catch (Exception ex) when (attempt < 2 && ex is IOException or HttpRequestException)
                { await Task.Delay(500 * (attempt + 1), token); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
