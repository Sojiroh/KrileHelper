using Sharlayan.Core;

Console.WriteLine("== FF14 Linux Sharlayan demo ==");

await using var client = new MemoryClientDisposable(new MemoryClient());
try
{
    await client.Inner.AttachAsync();
}
catch (FFXIVNotRunningException)
{
    Console.Error.WriteLine("FF14 process not found — start the game under Proton first.");
    return 1;
}
catch (SignatureScanFailedException ex)
{
    Console.Error.WriteLine($"Required signature not found: {ex.SignatureKey}. The cached signatures may be outdated.");
    return 2;
}

var ff = client.Inner.Process;
Console.WriteLine($"Attached to PID {ff.Pid}  base 0x{ff.ModuleBase:X}  size {ff.ModuleSize / 1024.0 / 1024.0:F1} MiB");
Console.WriteLine();
Console.WriteLine("Signature locations (0 = not found):");
foreach (var (key, addr) in client.Inner.SignatureLocations().OrderBy(kv => kv.Key))
    Console.WriteLine($"  {key,-22} {(addr != 0 ? "0x" + addr.ToString("X") : "—")}");

Console.WriteLine();
Console.WriteLine("Polling chat (Ctrl+C to stop)…");

// Prime the reader so we don't dump history.
client.Inner.ChatLog.Poll();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.IsCancellationRequested)
{
    try
    {
        var items = client.Inner.ChatLog.Poll();
        foreach (var item in items)
            Console.WriteLine($"  [{item.TimeStamp:HH:mm:ss}] {item.Code} {(item.JP ? "JP" : "  ")} {item.Line}");
    }
    catch (ProcessDetachedException)
    {
        Console.Error.WriteLine("FF14 process exited.");
        return 0;
    }

    try { await Task.Delay(150, cts.Token); }
    catch (OperationCanceledException) { break; }
}

return 0;

// Tiny helper because MemoryClient implements IDisposable, not IAsyncDisposable yet.
sealed class MemoryClientDisposable : IAsyncDisposable
{
    public MemoryClient Inner { get; }
    public MemoryClientDisposable(MemoryClient inner) { Inner = inner; }
    public ValueTask DisposeAsync() { Inner.Dispose(); return ValueTask.CompletedTask; }
}
