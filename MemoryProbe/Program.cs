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
Console.WriteLine($"Metadata language={client.Inner.GameLanguage} playerName=[{client.Inner.PlayerName}] " +
                  $"playerIsFeminine={client.Inner.PlayerIsFeminine?.ToString() ?? "unknown"}");
Console.WriteLine();
Console.WriteLine("Signature locations (0 = not found):");
foreach (var (key, addr) in client.Inner.SignatureLocations().OrderBy(kv => kv.Key))
    Console.WriteLine($"  {key,-22} {(addr != 0 ? "0x" + addr.ToString("X") : "—")}");

if (args.Contains("--once", StringComparer.Ordinal))
{
    var snapshot = client.Inner.Dialogue.Poll();
    Console.WriteLine($"LIVE source={snapshot.SourceAvailable} visible={snapshot.IsVisible} " +
                      $"surface={snapshot.Surface} code={snapshot.Code} speaker=[{snapshot.Speaker}] " +
                      $"text=[{snapshot.Text}] status={snapshot.Status}");
    Console.WriteLine($"Metadata language={client.Inner.GameLanguage} playerName=[{client.Inner.PlayerName}] " +
                      $"playerIsFeminine={client.Inner.PlayerIsFeminine?.ToString() ?? "unknown"}");
    return 0;
}

Console.WriteLine();
Console.WriteLine("Polling dialogue and chat (Ctrl+C to stop)…");

// Prime both roads. Live dialogue is intentionally polled before chat: the two
// roads race in the game, and Dialogue.ShouldSuppressChat handles either order.
client.Inner.Dialogue.Poll();
client.Inner.ChatLog.Poll();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
string? previousState = null;


while (!cts.IsCancellationRequested)
{
    try
    {
        var live = client.Inner.Dialogue.Poll();
        foreach (var item in live.Lines)
            Console.WriteLine($"  LIVE [{item.TimeStamp:HH:mm:ss}] {item.Code} {item.Line}");

        foreach (var item in client.Inner.ChatLog.Poll())
        {
            if (!client.Inner.Dialogue.ShouldSuppressChat(item))
                Console.WriteLine($"  CHAT [{item.TimeStamp:HH:mm:ss}] {item.Code} {(item.JP ? "JP" : "  ")} {item.Line}");
        }

        var state = $"visible={live.IsVisible}|source={live.SourceAvailable}|surface={live.Surface}|" +
                    $"bounds={live.Bounds}|code={live.Code}|speaker={live.Speaker}|text={live.Text}|" +
                    $"status={live.Status}|player={client.Inner.PlayerName}|" +
                    $"feminine={client.Inner.PlayerIsFeminine?.ToString() ?? "unknown"}|" +
                    $"language={client.Inner.GameLanguage}";
        if (!string.Equals(previousState, state, StringComparison.Ordinal))
        {
            previousState = state;
            Console.WriteLine($"  STATE {state}");
        }
        if (live.Choice is { IsBeingAsked: true } choice)
            Console.WriteLine($"  CHOICE bounds={live.ChoiceBounds} answers={choice.Answers.Count}: {choice.AsBlock()}");
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
