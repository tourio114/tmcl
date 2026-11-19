using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using Tmcl.Core;
using CmlLib.Core.FileExtractors;

var version = args.ElementAtOrDefault(0) ?? "1.8.9";
var folder = args.ElementAtOrDefault(1) ?? Path.Combine(Path.GetTempPath(), "tmcl-integration");
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
using var client = new HttpClient(new MirrorHandler(true)) { Timeout = TimeSpan.FromMinutes(3) };
var parameters = MinecraftLauncherParameters.CreateDefault(new MinecraftPath(folder), client);
foreach (var extractor in parameters.FileExtractors!.OfType<JavaFileExtractor>().ToArray()) parameters.FileExtractors!.Remove(extractor);
parameters.GameInstaller = new VerifiedInstaller(client, 8) { CheckFileSize = true };
client.DefaultRequestHeaders.UserAgent.ParseAdd("TMCL/1.0");
if (args.Contains("--network-only"))
{
    using var direct = DownloadSources.CreateIPv4Client();
    var sources = new DownloadSources(direct);
    try { var location = await sources.DetectIPv4(cts.Token); Console.WriteLine($"IPv4 location: {location.CountryCode}; China default mirror: {DownloadSources.DefaultMirror(location.CountryCode)}"); }
    catch (Exception ex) { Console.WriteLine("IPv4 detection unavailable: " + ex.GetType().Name); }
    var official = await sources.Measure(false, cts.Token);
    var mirror = await sources.Measure(true, cts.Token);
    Console.WriteLine($"Official: {official.Display}; BMCLAPI: {mirror.Display}; recommend mirror: {DownloadSources.Recommend(official, mirror, false)}");
    if (official.BytesPerSecond == null && mirror.BytesPerSecond == null) throw new IOException("Both source measurements failed.");
    return;
}
var javaService = new JavaService(client, Path.Combine(folder, "openjdk"));
var javaProgress = new Progress<string>(Console.WriteLine);
if (args.Contains("--java-only"))
{
    var javaPath = await javaService.Ensure(8, null, true, javaProgress, cts.Token);
    Console.WriteLine("Java verification succeeded: " + javaPath);
    return;
}
var launcher = new MinecraftLauncher(parameters);
var last = DateTime.MinValue;
launcher.FileProgressChanged += (_, p) =>
{
    if ((DateTime.UtcNow - last).TotalSeconds < 5) return;
    last = DateTime.UtcNow;
    Console.WriteLine($"{p.ProgressedTasks}/{p.TotalTasks} {p.Name}");
};
Console.WriteLine($"Installing vanilla {version} through BMCLAPI into {folder}");
await launcher.InstallAsync(version, cts.Token);
var metadata = await launcher.GetVersionAsync(version, cts.Token);
var major = int.TryParse(metadata.JavaVersion?.MajorVersion, out var required) ? required : 8;
var java = await javaService.Ensure(major, null, true, javaProgress, cts.Token);
using var process = await launcher.BuildProcessAsync(version, new MLaunchOption
{
    Session = MSession.CreateOfflineSession("TmclTester"), MaximumRamMb = 1024, JavaPath = java
}, cts.Token);
Console.WriteLine("Install succeeded; offline launch command created.");
if (args.Contains("--launch"))
{
    process.StartInfo.RedirectStandardOutput = true;
    process.StartInfo.RedirectStandardError = true;
    process.StartInfo.UseShellExecute = false;
    process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
    if (process.HasExited) throw new Exception($"Game exited early: {process.ExitCode}");
    process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
    Console.WriteLine("Offline game remained alive for 30 seconds; smoke test passed.");
}
