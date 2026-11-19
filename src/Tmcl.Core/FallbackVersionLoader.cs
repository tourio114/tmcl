using CmlLib.Core;
using CmlLib.Core.VersionLoader;
using CmlLib.Core.VersionMetadata;
namespace Tmcl.Core;
public sealed class FallbackVersionLoader(MinecraftPath path, HttpClient client) : IVersionLoader
{
    public async ValueTask<VersionMetadataCollection> GetVersionMetadatasAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { return await new MojangJsonVersionLoaderV2(path, client).GetVersionMetadatasAsync(timeout.Token); }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException)
        {
            var local = await new LocalJsonVersionLoader(path).GetVersionMetadatasAsync(cancellationToken);
            if (!local.Any()) throw new IOException("无法获取版本列表，且没有本地版本。请检查网络或切换下载源。", ex);
            return local;
        }
    }
}
