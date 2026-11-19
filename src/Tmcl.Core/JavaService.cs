using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tmcl.Core;

public sealed record JavaInfo(string Path, int Major, string Architecture);

public sealed class JavaService(HttpClient client, string cacheRoot)
{
    public static int ParseMajor(string output)
    {
        var match = Regex.Match(output, "(?:java|openjdk) version \\\"(?<major>\\d+)(?:\\.(?<minor>\\d+))?", RegexOptions.IgnoreCase);
        if (!match.Success) match = Regex.Match(output, "java.version\\s*=\\s*(?<major>\\d+)(?:\\.(?<minor>\\d+))?");
        if (!match.Success) return 0;
        int major = int.Parse(match.Groups["major"].Value);
        return major == 1 && match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].Value) : major;
    }
    public static string Platform => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";
    public static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
        _ => throw new PlatformNotSupportedException("自动安装 Java 支持 x64 和 ARM64。")
    };
    private static string JavaName => OperatingSystem.IsWindows() ? "java.exe" : "java";
    private string InstallPath(int major) => Path.Combine(cacheRoot, $"temurin-{major}-{Platform}-{Architecture}");
    public async Task<JavaInfo?> Probe(string executable, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            } };
            process.StartInfo.ArgumentList.Add("-XshowSettings:properties");
            process.StartInfo.ArgumentList.Add("-version");
            if (!process.Start()) return null;
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var output = await stdout + "\n" + await stderr;
                var major = ParseMajor(output);
                var arch = Regex.Match(output, @"os.arch\s*=\s*(\S+)").Groups[1].Value;
                var normalized = arch switch { "amd64" or "x86_64" => "x64", "arm64" => "aarch64", _ => arch };
                return process.ExitCode == 0 && major > 0 ? new JavaInfo(executable, major, normalized) : null;
            }
            finally { if (!process.HasExited) process.Kill(true); }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return null; }
    }
    public async Task<string> Ensure(int major, string? preferred, bool allowDownload, IProgress<string>? progress, CancellationToken token)
    {
        if (major < 8) major = 8;
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var explicitJava = await Probe(preferred, token);
            if (explicitJava?.Major != major || explicitJava.Architecture != Architecture)
                throw new IOException($"指定的 Java 不可用或不匹配：需要 Java {major} / {Architecture}。清空路径可自动安装 OpenJDK。");
            progress?.Report($"已检测到 Java {major}：{preferred}");
            return preferred;
        }
        var candidates = new List<string>();
        var installed = InstallPath(major);
        if (Directory.Exists(installed)) candidates.AddRange(Directory.EnumerateFiles(installed, JavaName, SearchOption.AllDirectories));
        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home) candidates.Add(Path.Combine(home, "bin", JavaName));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.Combine(directory, JavaName)); } catch (ArgumentException) { }
        }
        foreach (var candidate in candidates.Distinct())
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(candidate)) continue;
            var info = await Probe(candidate, token);
            if (info?.Major == major && info.Architecture == Architecture)
            { progress?.Report($"已检测到 Java {major}：{candidate}"); return candidate; }
        }
        if (!allowDownload) throw new IOException($"未找到 Java {major}，请联网启动以自动安装 OpenJDK，或手动指定 Java 路径。");
        return await Install(major, progress, token);
    }
    private async Task<string> Install(int major, IProgress<string>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(cacheRoot);
        var target = InstallPath(major);
        await using var installLock = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        progress?.Report($"未找到兼容 Java，正在查询 Temurin OpenJDK {major} / {Architecture}…");
        JsonElement package = default;
        foreach (var type in new[] { "jre", "jdk" })
        {
            var url = $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture={Architecture}&os={Platform}&image_type={type}&vendor=eclipse";
            using var response = await client.GetAsync(url, token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            if (json.RootElement.GetArrayLength() > 0)
            { package = json.RootElement[0].GetProperty("binary").GetProperty("package").Clone(); break; }
        }
        if (package.ValueKind == JsonValueKind.Undefined)
            throw new IOException($"Temurin 暂未提供 Java {major} / {Platform} / {Architecture}；请手动安装兼容 OpenJDK。旧版 Minecraft 在 ARM64 上可能需要系统模拟支持。");
        var link = new Uri(package.GetProperty("link").GetString()!);
        if (link.Scheme != "https") throw new IOException("OpenJDK 下载地址必须使用 HTTPS。");
        var checksum = package.GetProperty("checksum").GetString()!;
        var staging = target + ".extract-" + Guid.NewGuid().ToString("N");
        var archive = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using (var source = await response.Content.ReadAsStreamAsync(token))
                    await using (var file = File.Create(archive))
                    {
                        var buffer = new byte[81920];
                        long total = 0, reported = 0;
                        int count;
                        while ((count = await source.ReadAsync(buffer, token)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, count), token);
                            total += count;
                            if (total - reported >= 1024 * 1024)
                            { reported = total; progress?.Report($"正在下载 OpenJDK {major}：{total / 1048576d:F1} MB"); }
                        }
                    }
                    await using var verify = File.OpenRead(archive);
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(verify, token));
                    if (!actual.Equals(checksum, StringComparison.OrdinalIgnoreCase)) throw new IOException("OpenJDK SHA-256 校验失败。");
                    break;
                }
                catch (Exception ex) when (attempt < 2 && ex is IOException or HttpRequestException)
                { await Task.Delay(500 * (attempt + 1), token); }
            }
            progress?.Report("OpenJDK 校验通过，正在解压…");
            Directory.CreateDirectory(staging);
            if (link.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                await Task.Run(() => ZipFile.ExtractToDirectory(archive, staging), token);
            else
            {
                await using var compressed = File.OpenRead(archive);
                await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gzip, staging, false, token);
            }
            token.ThrowIfCancellationRequested();
            var java = Directory.EnumerateFiles(staging, JavaName, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new IOException("OpenJDK 压缩包中缺少 Java 可执行文件。");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(java, File.GetUnixFileMode(java) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                foreach (var helper in Directory.EnumerateFiles(staging, "jspawnhelper", SearchOption.AllDirectories))
                    File.SetUnixFileMode(helper, File.GetUnixFileMode(helper) | UnixFileMode.UserExecute);
            }
            var info = await Probe(java, token);
            if (info?.Major != major || info.Architecture != Architecture) throw new IOException("OpenJDK 安装验证失败。");
            var relative = Path.GetRelativePath(staging, java);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(staging, target);
            progress?.Report($"OpenJDK {major} 已安装。无需管理员权限。");
            return Path.Combine(target, relative);
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}
