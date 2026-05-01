using Sharlayan.Core.ChatLog;
using Sharlayan.Core.Native;
using Sharlayan.Core.Process;
using Sharlayan.Core.Resources;
using Sharlayan.Core.Scanning;

namespace Sharlayan.Core;

public sealed class MemoryClient : IDisposable
{
    private readonly INativeMemory _mem;
    private readonly ResourceLoader _resources;
    private IReadOnlyList<Signature>? _signatures;
    private StructuresContainer? _structs;
    private AttachedProcess? _process;
    private ChatLogReader? _chatLog;

    public MemoryClient(ResourceLoader? resources = null, INativeMemory? memory = null)
    {
        _mem = memory ?? NativePlatform.CreateMemory();
        _resources = resources ?? new ResourceLoader();
    }

    public AttachedProcess Process =>
        _process ?? throw new InvalidOperationException("Not attached. Call AttachAsync first.");

    public ChatLogReader ChatLog =>
        _chatLog ?? throw new InvalidOperationException("Not attached. Call AttachAsync first.");

    public bool IsAttached => _process is not null && _process.IsAlive;

    /// <summary>
    /// Discovers the running FFXIV process, parses its PE, loads signatures
    /// from cache (or downloads them) and runs the scan. After this call the
    /// ChatLog reader is ready to Poll.
    /// </summary>
    public async Task AttachAsync(CancellationToken ct = default)
    {
        var (sigs, structs) = await _resources.LoadAsync(ct);
        _signatures = sigs;
        _structs = structs;

        var ff = ProcessFinder.FindFFXIV(_mem) ?? throw new FFXIVNotRunningException();
        _process = ff;

        // Reset prior scan state if re-attaching.
        foreach (var s in sigs) s.SigScanAddress = 0;
        SignatureScanner.ScanRangeMulti(_mem, ff.Pid, ff.ModuleBase, ff.ModuleEnd, sigs);

        var chatSig = sigs.FirstOrDefault(s => s.Key == "CHATLOG")
            ?? throw new SignatureScanFailedException("CHATLOG");
        if (chatSig.SigScanAddress == 0)
            throw new SignatureScanFailedException("CHATLOG");

        _chatLog = new ChatLogReader(_mem, ff.Pid, chatSig, structs.ChatLogPointers);
    }

    /// <summary>Re-runs FindFFXIV + scan (e.g. after the game was restarted or patched).</summary>
    public Task ReattachAsync(CancellationToken ct = default)
    {
        _process = null;
        _chatLog = null;
        return AttachAsync(ct);
    }

    /// <summary>Diagnostic: returns the resolved address (or 0) for every loaded signature.</summary>
    public IReadOnlyDictionary<string, ulong> SignatureLocations()
    {
        if (_signatures is null) return new Dictionary<string, ulong>();
        return _signatures.ToDictionary(s => s.Key, s => s.SigScanAddress);
    }

    public void Dispose()
    {
        _process = null;
        _chatLog = null;
    }
}
