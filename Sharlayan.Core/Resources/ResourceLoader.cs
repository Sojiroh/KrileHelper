using System.Text.Json;

namespace Sharlayan.Core.Resources;

public sealed class ResourceLoader
{
    private const string DefaultSignaturesUrl =
        "https://raw.githubusercontent.com/NightlyRevenger/sharlayan-resources/master/signatures/latest/x64.json";
    private const string DefaultStructuresUrl =
        "https://raw.githubusercontent.com/NightlyRevenger/sharlayan-resources/master/structures/latest/x64.json";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public string CacheDirectory { get; }
    public string SignaturesUrl { get; init; } = DefaultSignaturesUrl;
    public string StructuresUrl { get; init; } = DefaultStructuresUrl;
    public TimeSpan? CacheMaxAge { get; init; } = TimeSpan.FromDays(7);

    public ResourceLoader(string? cacheDirectory = null)
    {
        CacheDirectory = cacheDirectory ?? DefaultCacheDir();
        Directory.CreateDirectory(CacheDirectory);
    }

    private static string DefaultCacheDir()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cacheRoot = !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(home, ".cache");
        return Path.Combine(cacheRoot, "sharlayan-core");
    }

    public async Task<(IReadOnlyList<Scanning.Signature> Sigs, StructuresContainer Structs)> LoadAsync(
        CancellationToken ct = default)
    {
        var sigPath = Path.Combine(CacheDirectory, "signatures-x64.json");
        var strPath = Path.Combine(CacheDirectory, "structures-x64.json");

        await EnsureFreshAsync(sigPath, SignaturesUrl, ct);
        await EnsureFreshAsync(strPath, StructuresUrl, ct);

        var defs = JsonSerializer.Deserialize<List<SignatureDefinition>>(
            await File.ReadAllTextAsync(sigPath, ct), JsonOpts) ?? new();
        var structs = JsonSerializer.Deserialize<StructuresContainer>(
            await File.ReadAllTextAsync(strPath, ct), JsonOpts) ?? new();

        var sigs = defs
            .Where(d => !string.IsNullOrEmpty(d.Value))
            .Select(d => new Scanning.Signature(
                d.Key,
                d.Value.Replace("*", "?"),
                d.PointerPath,
                d.ASMSignature))
            .ToList();

        return (sigs, structs);
    }

    private async Task EnsureFreshAsync(string path, string url, CancellationToken ct)
    {
        if (File.Exists(path) && CacheMaxAge is { } age && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow - age)
            return;
        if (File.Exists(path) && CacheMaxAge is null)
            return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(path, bytes, ct);
        }
        catch when (File.Exists(path))
        {
            // Network failed but a stale cache exists; use it.
        }
    }
}
