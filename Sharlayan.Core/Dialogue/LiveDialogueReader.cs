
using System.Buffers.Binary;
using System.Text;
using Sharlayan.Core.ChatLog;
using Sharlayan.Core.Native;
using Sharlayan.Core.Scanning;

namespace Sharlayan.Core.Dialogue;

/// <summary>
/// Reads visible Talk, TalkSubtitle, MiniTalk/_MiniTalk and choice addons using
/// only INativeMemory. A failed live read is represented by an unavailable
/// snapshot; it never prevents ChatLogReader from continuing.
/// </summary>
public class LiveDialogueReader
{
    private const string Talk = "Talk";
    private const string Subtitle = "TalkSubtitle";
    private const string MiniTalk = "MiniTalk";
    private const string AlternateMiniTalk = "_MiniTalk";
    private const string ChoiceAddon = "CutSceneSelectString";
    private const string DirectCode = "003D";
    private const string CutsceneCode = "0044";
    private const int TextNodeType = 3;
    private const int NineGridNodeType = 4;
    private const ushort VisibleNodeFlag = 0x10;
    private const int MaxLoadedUnits = 256;
    private const int MaxNodes = 512;
    private const int MaxDepth = 4;
    private const int MaxUtf8Bytes = 4096;
    private const long Utf8Pointer = 0;
    private const long Utf8Used = 16;
    private const long Utf8Length = 24;
    private const long Utf8Inline = 33;
    private const long Utf8InlineBuffer = 34;

    private readonly INativeMemory _memory;
    private readonly int _pid;
    private readonly Signature _chatSignature;
    private readonly Signature? _playerStateSignature;
    private readonly DialogueMemoryLayout _layout;
    private readonly RecentUtterance _recent = new();
    private readonly SpeechBubbles _speechBubbles = new();
    private readonly byte[] _playerNameBytes = new byte[64];
    private readonly Dictionary<string, HashSet<string>> _recentRealtimeLines = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, string Speaker)> _recentRealtimeOrder = new();
    private HashSet<string> _lastAddonText = new(StringComparer.Ordinal);
    private string? _stickyCandidate;
    private string _lastChoiceSignature = string.Empty;
    private long? _windowListOffset;
    private DateTime _lastWindowSearch = DateTime.MinValue;
    private LiveDialogueSnapshot _last = LiveDialogueSnapshot.Unavailable("not-polled");

    public LiveDialogueReader(INativeMemory memory, int pid, Signature chatSignature,
        DialogueMemoryLayout layout, Signature? playerStateSignature = null)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _pid = pid;
        _chatSignature = chatSignature ?? throw new ArgumentNullException(nameof(chatSignature));
        _playerStateSignature = playerStateSignature;
        _layout = layout ?? DialogueMemoryLayout.Unavailable;
    }

    public LiveDialogueSnapshot Current => _last;
    public string PlayerName { get; private set; } = string.Empty;
    public bool? PlayerIsFeminine { get; private set; }

    /// <summary>Reads one coherent live surface and returns only newly spoken lines.</summary>
    public LiveDialogueSnapshot Poll()
    {
        try
        {
            TryReadPlayerState();
            if (!_layout.IsUsable)
                return _last = LiveDialogueSnapshot.Unavailable("dialogue-layout-unavailable; chat-log-independent");

            var chatAddress = PointerResolver.Resolve(_memory, _pid, _chatSignature);
            if (chatAddress <= 0)
                return _last = LiveDialogueSnapshot.Unavailable("chat-anchor-unavailable; chat-log-independent");

            var uiModule = Add(chatAddress, -_layout.RaptureLogModuleOffset - _layout.ChatPointerMapOffset);
            if (uiModule == 0)
                return _last = LiveDialogueSnapshot.Unavailable("ui-module-unavailable; chat-log-independent");

            TryReadLastTalk(uiModule, out var lastName, out var lastText);
            if (!TryFindWindowList(uiModule, out var manager))
                return _last = LiveDialogueSnapshot.Unavailable("ui-window-list-unavailable; chat-log-independent");

            var result = ReadLoadedUnits(manager, lastName, lastText);
            return _last = result;
        }
        catch (ProcessDetachedException)
        {
            return _last = LiveDialogueSnapshot.Unavailable("process-detached; chat-log-independent");
        }
        catch (Exception ex)
        {
            return _last = LiveDialogueSnapshot.Unavailable($"live-read-failed: {ex.GetType().Name}; chat-log-independent");
        }
    }

    public static DialogueSurface SurfaceOf(string? candidateKey)
    {
        if (string.IsNullOrEmpty(candidateKey)) return DialogueSurface.None;
        var at = candidateKey.IndexOf('@');
        var name = at > 0 ? candidateKey[..at] : candidateKey;
        return name.Equals(Talk, StringComparison.OrdinalIgnoreCase) ? DialogueSurface.Window :
            name.Equals(Subtitle, StringComparison.OrdinalIgnoreCase) ? DialogueSurface.Subtitle :
            name.Equals(MiniTalk, StringComparison.OrdinalIgnoreCase) || name.Equals(AlternateMiniTalk, StringComparison.OrdinalIgnoreCase)
                ? DialogueSurface.Bubble : DialogueSurface.None;
    }

    /// <summary>
    /// Filters the independent chat road against live utterances in either
    /// arrival order.
    /// </summary>
    public bool ShouldSuppressChat(ChatLogItem? item, DateTime? now = null)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Line)) return false;
        var key = BuildDuplicateKey(item.Line);
        if (key.Length == 0 || !IsLiveDialogueCode(item.Code)) return false;
        var speaker = SpeakerOf(item.Line);
        if (IsRememberedRealtime(key, speaker)) return true;
        return _recent.IsEcho(key, speaker, now ?? DateTime.Now);
    }

    public void Reset()
    {
        _speechBubbles.Forget();
        _recent.Forget();
        _recentRealtimeLines.Clear();
        _recentRealtimeOrder.Clear();
        _lastAddonText.Clear();
        _stickyCandidate = null;
        _lastChoiceSignature = string.Empty;
        _last = LiveDialogueSnapshot.Unavailable("reset");
    }

    public static string Normalize(string? value) => (value ?? string.Empty).Trim();

    public static string BuildDuplicateKey(string? line)
    {
        var normalized = GameIcons.Strip(Normalize(line));
        if (normalized.Length == 0) return string.Empty;
        var colon = normalized.IndexOf(':');
        if (colon > 0 && colon < normalized.Length - 1) normalized = normalized[(colon + 1)..];
        var builder = new StringBuilder(normalized.Length);
        var space = false;
        foreach (var c in normalized)
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && builder.Length > 0) builder.Append(' ');
            space = false;
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    public static string SpeakerOf(string? line)
    {
        var normalized = Normalize(line);
        var colon = normalized.IndexOf(':');
        return colon > 0 && colon < normalized.Length - 1 ? normalized[..colon] : string.Empty;
    }

    public static string BuildDialogLine(string? speaker, string? text)
    {
        var body = Normalize(text);
        if (body.Length == 0) return string.Empty;
        var name = Normalize(speaker);
        return name.Length == 0 ? body : name + ":" + body;
    }

    /// <summary>Decodes SeString bytes while replacing icon payloads with stable marks.</summary>
    public static string DecodeGameString(byte[]? data, int start, int count)
    {
        if (data is null || count <= 0 || start < 0 || start >= data.Length) return string.Empty;
        var end = Math.Min(data.Length, start + count);
        var cleaned = new byte[end - start + 4];
        var written = 0;
        for (var i = start; i < end; i++)
        {
            if (data[i] != 0x02)
            {
                cleaned[written++] = data[i];
                continue;
            }
            var close = Array.IndexOf(data, (byte)0x03, i, end - i);
            if (close < 0) break;
            var icon = IconIn(data, i, close);
            if (icon > 0)
            {
                var mark = Encoding.UTF8.GetBytes(GameIcons.Mark(icon));
                if (written + mark.Length <= cleaned.Length)
                {
                    mark.CopyTo(cleaned, written);
                    written += mark.Length;
                }
            }
            i = close;
        }
        return written == 0 ? string.Empty : Encoding.UTF8.GetString(cleaned, 0, written);
    }
    public static int IconIn(byte[] data, int start, int end)
    {
        if (data is null || start < 0 || end > data.Length || end - start < 4 || data[start + 1] != 0x12) return 0;
        var value = data[start + 3];
        if (value < 0xD0) return end - start == 4 ? value - 1 : 0;
        return value == 0xF0 && end - start == 5 ? data[start + 4] : 0;
    }

    public static bool LooksLikeDialogueText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 12 || value.Contains('_')) return false;
        var letters = 0; var visible = 0; var words = 1;
        foreach (var c in value)
        {
            if (char.IsControl(c) && c is not ('\n' or '\r' or '\t')) return false;
            if (char.IsWhiteSpace(c)) { words++; continue; }
            visible++; if (char.IsLetter(c)) letters++;
        }
        return words >= 3 && visible > 0 && letters * 10 >= visible * 7;
    }

    public static bool TryParseUtf8StringHeader(byte[]? buffer, int offset, out long byteCount,
        out bool isInline, out long dataPointer)
    {
        byteCount = 0; isInline = false; dataPointer = 0;
        if (buffer is null || offset < 0 || offset + Utf8InlineBuffer > buffer.Length) return false;
        var used = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + (int)Utf8Used));
        var length = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + (int)Utf8Length));
        byteCount = used > 0 ? used : length;
        if (byteCount <= 0 || byteCount > MaxUtf8Bytes || used > 0 && length > 0 && length < used - 1) return false;
        isInline = buffer[offset + (int)Utf8Inline] != 0;
        dataPointer = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset));
        return isInline || dataPointer > 0x10000 && dataPointer <= 0x7FFFFFFFFFFF;
    }

    private bool TryReadPlayerState()
    {
        if (_playerStateSignature is null || _playerStateSignature.SigScanAddress <= 17 ||
            _layout.PlayerStateNameOffset < 0 || _layout.PlayerStateSexOffset < 0)
            return ClearPlayerState();
        var needed = Math.Max(_layout.PlayerStateNameOffset + 64, _layout.PlayerStateSexOffset + 1);
        if (needed > _layout.PlayerStateSourceSize || needed > MaxUtf8Bytes)
            return ClearPlayerState();
        var displacementAddress = _playerStateSignature.SigScanAddress - 17;
        Span<byte> disp = stackalloc byte[4];
        if (ReadMemory(displacementAddress, disp) != 4) return ClearPlayerState();
        var state = unchecked((ulong)((long)displacementAddress + BinaryPrimitives.ReadInt32LittleEndian(disp) + 4));
        Span<byte> bytes = stackalloc byte[(int)needed];
        if (ReadMemory(state, bytes) != bytes.Length || bytes[0] == 0) return ClearPlayerState();
        var nameBytes = bytes.Slice((int)_layout.PlayerStateNameOffset, 64);
        if (!nameBytes.SequenceEqual(_playerNameBytes))
        {
            nameBytes.CopyTo(_playerNameBytes);
            var zero = nameBytes.IndexOf((byte)0);
            PlayerName = Encoding.UTF8.GetString(zero < 0 ? nameBytes : nameBytes[..zero]).Trim();
        }
        PlayerIsFeminine = bytes[(int)_layout.PlayerStateSexOffset] switch { 0 => false, 1 => true, _ => null };
        return PlayerName.Length > 0;
    }

    private bool ClearPlayerState()
    {
        if (PlayerName.Length > 0) Array.Clear(_playerNameBytes);
        PlayerName = string.Empty;
        PlayerIsFeminine = null;
        return false;
    }


    private LiveDialogueSnapshot ReadLoadedUnits(ulong manager, string lastName, string lastText)
    {
        var list = Add(manager, _layout.AllLoadedUnitsListOffset);
        if (list == 0) return LiveDialogueSnapshot.Unavailable("addon-list-unavailable; chat-log-independent");
        var count = ReadU16(list, _layout.AtkUnitListCountOffset);
        if (count == 0 || count > MaxLoadedUnits) return LiveDialogueSnapshot.Unavailable("addon-list-empty; chat-log-independent");
        var entries = Add(list, _layout.AtkUnitListEntriesOffset);
        if (entries == 0) return LiveDialogueSnapshot.Unavailable("addon-entries-unavailable; chat-log-independent");

        var addons = new List<(ulong Address, string Name)>();
        var choices = new List<(GameChoice Choice, AddonBounds Bounds)>();
        for (var i = 0; i < count; i++)
        {
            var address = ReadPtr(Add(entries, i * IntPtr.Size));
            if (address == 0) continue;
            if (!TryReadAddonName(address, out var name)) continue;
            if (IsChoice(name))
            {
                if (string.Equals(name, ChoiceAddon, StringComparison.OrdinalIgnoreCase) && !IsAddonOffScreen(address))
                {
                    var candidateChoice = ReadChoice(address);
                    if (candidateChoice.IsBeingAsked && TryReadAddonBounds(address, out var where)) choices.Add((candidateChoice, where));
                }
                continue;
            }
            if (IsWanted(name) && !IsAddonOffScreen(address)) addons.Add((address, name));
        }

        var candidates = new List<Candidate>();
        var lines = new List<ChatLogItem>();
        Candidate? bubble = null;
        foreach (var (address, name) in addons)
        {
            if (!TryReadAddonTexts(address, name, lastName, lastText, out var speaker, out var text)) continue;
            var candidateBounds = TryReadAddonBounds(address, out var b) ? b : AddonBounds.Unknown;
            if (text.Length == 0) continue;
            var candidateSurface = SurfaceOf(name);
            if (candidateSurface == DialogueSurface.Window && !DrawsAFrame(address)) candidateSurface = DialogueSurface.Notice;
            var candidate = new Candidate(name + "@" + address.ToString("X"), name, DirectCodeFor(name), speaker, text, candidateSurface, candidateBounds);
            if (candidateSurface == DialogueSurface.Bubble)
            {
                // Bubbles are transient chat events, not a replacement for an open dialogue.
                // Picking one as the sticky source strands the overlay on its next, empty sweep.
                RememberLive(BuildDialogLine(speaker, text), speaker, candidate.Code, lines);
                bubble ??= candidate;
            }
            else
            {
                candidates.Add(candidate);
            }
        }

        var active = SelectCandidate(candidates) ?? bubble;
        var choice = choices.OrderBy(c => c.Bounds.Y).FirstOrDefault();
        var hasChoice = choice.Choice is not null && choice.Choice.IsBeingAsked;
        var code = string.Empty;
        var speakerText = string.Empty;
        var textBody = string.Empty;
        var surface = DialogueSurface.None;
        var bounds = AddonBounds.Unknown;
        var visible = false;

        if (hasChoice)
        {
            var c = choice.Choice!;
            var signature = c.Signature();
            if (!string.Equals(signature, _lastChoiceSignature, StringComparison.Ordinal))
            {
                _lastChoiceSignature = signature;
                var line = c.AsBlock();
                RememberLive(line, string.Empty, CutsceneCode, lines);
            }
            code = CutsceneCode; textBody = c.AsBlock(); surface = DialogueSurface.Choice;
            bounds = choice.Bounds; visible = true;
        }
        else
        {
            _lastChoiceSignature = string.Empty;
            if (active is not null)
            {
                code = active.Code; speakerText = active.Speaker; textBody = active.Text;
                surface = active.Surface; bounds = active.Bounds; visible = true;
                if (active.IsNew) RememberLive(BuildDialogLine(active.Speaker, active.Text), active.Speaker, active.Code, lines);
            }
        }

        return new LiveDialogueSnapshot(lines, speakerText, textBody, code, surface, bounds,
            hasChoice ? choice.Choice : null, hasChoice ? choice.Bounds : AddonBounds.Unknown,
            visible, true, visible ? "Live dialogue available." : "Live reader ready; no visible dialogue.");
    }

    private Candidate? SelectCandidate(List<Candidate> candidates)
    {
        if (candidates.Count == 0)
        {
            _stickyCandidate = null; _lastAddonText.Clear(); return null;
        }
        var current = new HashSet<string>(StringComparer.Ordinal);
        Candidate? changed = null;
        foreach (var candidate in candidates)
        {
            var identity = candidate.Name + "\u001f" + candidate.Speaker + "\u001f" + candidate.Text;
            if (current.Add(identity) && changed is null && !_lastAddonText.Contains(identity)) changed = candidate;
        }
        _lastAddonText = current;
        // New text controls chat delivery, not whether a currently visible addon
        // can be presented. The game may replace an addon between sweeps while
        // retaining exactly the same speaker/text.
        var selected = changed;
        if (selected is null)
        {
            selected = candidates[0];
            foreach (var candidate in candidates)
            {
                if (candidate.Key != _stickyCandidate) continue;
                selected = candidate;
                break;
            }
        }
        _stickyCandidate = selected.Key;
        selected.IsNew = ReferenceEquals(changed, selected);
        return selected;

    }

    private void RememberLive(string line, string speaker, string code, List<ChatLogItem> lines)
    {
        var key = BuildDuplicateKey(line);
        if (key.Length == 0) return;
        var now = DateTime.Now;
        var echo = _recent.IsEcho(key, speaker, now);
        RememberRecent(key, speaker);
        if (!echo)
        {
            lines.Add(new ChatLogItem { Code = code, Line = line, TimeStamp = now });
        }
    }

    private void RememberRecent(string key, string speaker)
    {
        if (!_recentRealtimeLines.TryGetValue(key, out var speakers))
            _recentRealtimeLines[key] = speakers = new HashSet<string>(StringComparer.Ordinal);
        speakers.Add(speaker);
        _recentRealtimeOrder.Enqueue((key, speaker));
        while (_recentRealtimeOrder.Count > 64)
        {
            var old = _recentRealtimeOrder.Dequeue();
            if (_recentRealtimeLines.TryGetValue(old.Key, out var oldSpeakers))
            {
                oldSpeakers.Remove(old.Speaker);
                if (oldSpeakers.Count == 0) _recentRealtimeLines.Remove(old.Key);
            }
        }
    }

    private bool IsRememberedRealtime(string key, string speaker)
    {
        if (!_recentRealtimeLines.TryGetValue(key, out var speakers)) return false;
        return speakers.Contains(string.Empty) || speaker.Length == 0 || speakers.Contains(speaker);
    }

    private static bool IsLiveDialogueCode(string? code) =>
        code is not null && (code.Equals("003D", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("0044", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("2AB9", StringComparison.OrdinalIgnoreCase));

    private bool TryReadAddonTexts(ulong address, string name, string lastName, string lastText,
        out string speaker, out string text)
    {
        speaker = string.Empty; text = string.Empty;
        if (name.Equals(Talk, StringComparison.OrdinalIgnoreCase))
        {
            // Node ids are stable layout identifiers and are preferred over
            // field order, which can change while the addon keeps its shape.
            if (_layout.HasNodeLayout)
            {
                TryReadTextNodeById(address, 2, out speaker);
                TryReadTextNodeById(address, 3, out text);
            }
            if (speaker.Length == 0 && _layout.TalkSpeakerNodeOffset >= 0)
                speaker = ReadNodeText(ReadPtr(Add(address, _layout.TalkSpeakerNodeOffset)));
            if (text.Length == 0 && _layout.TalkTextNodeOffset >= 0)
                text = ReadNodeText(ReadPtr(Add(address, _layout.TalkTextNodeOffset)));
            if (text.Length == 0) return true;
        }
        else if (name.Equals(Subtitle, StringComparison.OrdinalIgnoreCase))
        {
            if (_layout.TalkSubtitleTextOffset < 0 || !TryReadUtf8(address, _layout.TalkSubtitleTextOffset, out text)) return true;
        }
        else
        {
            var all = new List<string>();
            if (_layout.MiniTalkEntriesOffset >= 0 && _layout.MiniTalkEntrySize > 0 && _layout.MiniTalkEntryCount > 0)
            {
                var entries = Add(address, _layout.MiniTalkEntriesOffset);
                for (var i = 0; i < _layout.MiniTalkEntryCount; i++)
                {
                    var entry = Add(entries, i * _layout.MiniTalkEntrySize);
                    var node = ReadPtr(Add(entry, _layout.MiniTalkTextNodeOffset));
                    var value = ReadNodeText(node);
                    if (value.Length > 0) all.Add(value);
                }
            }
            text = _speechBubbles.Pick(all);
        }
        if (speaker.Length == 0 && string.Equals(lastText, text, StringComparison.Ordinal)) speaker = lastName;
        return true;
    }

    private GameChoice ReadChoice(ulong address)
    {
        if (!_layout.HasNodeLayout || _layout.ComponentOffset < 0 || _layout.ComponentUldManagerOffset < 0) return GameChoice.None;
        var found = new List<(string Text, AddonBounds Bounds)>();
        GatherTexts(Add(address, _layout.UldManagerOffset), found, 0);
        var drawn = found.Where(f => f.Text.Length > 0 && f.Bounds.IsKnown)
            .OrderBy(f => f.Bounds.Y).GroupBy(f => f.Text, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(f => f.Bounds.Y).ToArray();
        if (drawn.Length < 2) return GameChoice.None;
        return new GameChoice(drawn[0].Text, drawn[0].Bounds,
            drawn.Skip(1).Select(f => f.Text).ToArray(), drawn.Skip(1).Select(f => f.Bounds).ToArray());
    }

    private void GatherTexts(ulong manager, List<(string Text, AddonBounds Bounds)> found, int depth)
    {
        if (manager == 0 || depth > MaxDepth || found.Count > 32) return;
        var count = ReadU16(manager, _layout.NodeListCountOffset);
        var nodes = ReadPtr(Add(manager, _layout.NodeListOffset));
        if (count == 0 || count > MaxNodes || nodes == 0) return;
        for (var i = 0; i < count; i++)
        {
            var node = ReadPtr(Add(nodes, i * IntPtr.Size));
            if (node == 0) continue;
            var type = ReadU16(node, _layout.NodeTypeOffset);
            var visible = (ReadU16(node, _layout.NodeFlagsOffset) & VisibleNodeFlag) != 0;
            if (type == TextNodeType)
            {
                if (visible) { var text = ReadNodeText(node); if (text.Length > 0) found.Add((text, NodeBounds(node))); }
                continue;
            }
            if (type >= 1000 && visible)
            {
                var component = ReadPtr(Add(node, _layout.ComponentOffset));
                GatherTexts(Add(component, _layout.ComponentUldManagerOffset), found, depth + 1);
            }
        }
    }

    private bool TryFindWindowList(ulong uiModule, out ulong manager)
    {
        manager = 0;
        var described = _layout.RaptureAtkModuleOffset;
        if (_windowListOffset is { } known && TryReachWindowList(uiModule, known, out manager)) return true;
        if (TryReachWindowList(uiModule, described, out manager)) { _windowListOffset = described; return true; }
        if (DateTime.UtcNow - _lastWindowSearch < TimeSpan.FromSeconds(2)) return false;
        _lastWindowSearch = DateTime.UtcNow;
        for (long distance = 8; distance <= 0x2000; distance += 8)
        {
            if (TryReachWindowList(uiModule, described + distance, out manager)) { _windowListOffset = described + distance; return true; }
            if (described >= distance && TryReachWindowList(uiModule, described - distance, out manager)) { _windowListOffset = described - distance; return true; }
        }
        return false;
    }

    private bool TryReachWindowList(ulong uiModule, long offset, out ulong manager)
    {
        manager = 0;
        var module = Add(uiModule, offset);
        var candidate = ReadPtr(Add(module, _layout.AtkUnitManagerOffset));
        if (candidate == 0) return false;
        var list = Add(candidate, _layout.AllLoadedUnitsListOffset);
        var count = ReadU16(list, _layout.AtkUnitListCountOffset);
        if (count == 0 || count > MaxLoadedUnits) return false;
        var entries = Add(list, _layout.AtkUnitListEntriesOffset);
        if (entries == 0) return false;
        var named = 0; var examined = Math.Min((int)count, 32);
        for (var i = 0; i < examined; i++)
        {
            var addon = ReadPtr(Add(entries, i * IntPtr.Size));
            if (addon != 0 && TryReadAddonName(addon, out var name) && LooksLikeAnAddonName(name)) named++;
        }
        if (named * 2 < examined) return false;
        manager = candidate; return true;
    }

    public static bool LooksLikeAnAddonName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < 3 || name.Length > 32 || name[0] != '_' && !char.IsLetter(name[0])) return false;
        return name.All(c => c == '_' || char.IsLetterOrDigit(c));
    }

    private bool TryReadAddonName(ulong address, out string name)
    {
        name = string.Empty;
        var bytes = ReadBytes(Add(address, _layout.AtkUnitBaseNameOffset), _layout.AtkUnitBaseNameLength);
        if (bytes.Length == 0) return false;
        var length = Array.IndexOf(bytes, (byte)0); if (length < 0) length = bytes.Length;
        name = Encoding.ASCII.GetString(bytes, 0, length).Trim(); return name.Length > 0;
    }

    private bool IsAddonOffScreen(ulong address)
    {
        if (_layout.AtkUnitBaseRootNodeOffset < 0 || _layout.NodeFlagsOffset < 0) return false;
        var root = ReadPtr(Add(address, _layout.AtkUnitBaseRootNodeOffset));
        return root != 0 && (ReadU16(root, _layout.NodeFlagsOffset) & VisibleNodeFlag) == 0;
    }

    private bool TryReadAddonBounds(ulong address, out AddonBounds bounds)
    {
        bounds = AddonBounds.Unknown;
        if (!_layout.HasBounds) return false;
        var root = ReadPtr(Add(address, _layout.AtkUnitBaseRootNodeOffset));
        if (root == 0) return false;
        bounds = AddonBounds.From(ReadF32(root, _layout.AtkResNodeScreenXOffset), ReadF32(root, _layout.AtkResNodeScreenYOffset),
            ReadU16(root, _layout.AtkResNodeWidthOffset), ReadU16(root, _layout.AtkResNodeHeightOffset), ReadF32(root, _layout.AtkResNodeScaleXOffset));
        return bounds.IsKnown;
    }

    private bool DrawsAFrame(ulong address)
    {
        if (!_layout.HasNodeLayout) return true;
        var manager = Add(address, _layout.UldManagerOffset);
        var count = ReadU16(manager, _layout.NodeListCountOffset);
        var nodes = ReadPtr(Add(manager, _layout.NodeListOffset));
        if (count == 0 || count > MaxNodes || nodes == 0) return true;
        for (var i = 0; i < count; i++)
        {
            var node = ReadPtr(Add(nodes, i * IntPtr.Size));
            if (node != 0 && ReadU16(node, _layout.NodeTypeOffset) == NineGridNodeType &&
                (ReadU16(node, _layout.NodeFlagsOffset) & VisibleNodeFlag) != 0) return true;
        }
        return false;
    }

    private bool TryReadTextNodeById(ulong addon, uint id, out string text)
    {
        text = string.Empty;
        if (!_layout.HasNodeLayout) return false;
        var manager = Add(addon, _layout.UldManagerOffset);
        var count = ReadU16(manager, _layout.NodeListCountOffset);
        var nodes = ReadPtr(Add(manager, _layout.NodeListOffset));
        if (count == 0 || count > MaxNodes || nodes == 0) return false;
        for (var i = 0; i < count; i++)
        {
            var node = ReadPtr(Add(nodes, i * IntPtr.Size));
            if (node == 0 || ReadU32(Add(node, _layout.NodeIdOffset)) != id || ReadU16(node, _layout.NodeTypeOffset) != TextNodeType) continue;
            if ((ReadU16(node, _layout.NodeFlagsOffset) & VisibleNodeFlag) == 0) return false;
            text = ReadNodeText(node); return text.Length > 0;
        }
        return false;
    }

    private string ReadNodeText(ulong node) => node != 0 && TryReadUtf8(node, _layout.AtkTextNodeNodeTextOffset, out var text) ? text : string.Empty;

    private bool TryReadLastTalk(ulong ui, out string name, out string text)
    {
        var nameRead = TryReadUtf8(ui, _layout.LastTalkNameOffset, out name);
        var textRead = TryReadUtf8(ui, _layout.LastTalkTextOffset, out text);
        return nameRead || textRead;
    }

    private bool TryReadUtf8(ulong address, long offset, out string value)
    {
        value = string.Empty;
        if (address == 0 || offset < 0) return false;
        var s = Add(address, offset);
        var used = ReadI64(Add(s, Utf8Used)); var length = ReadI64(Add(s, Utf8Length));
        var count = used > 0 ? used : length;
        if (count <= 0) return true;
        if (count > MaxUtf8Bytes) return false;
        var inline = ReadByte(Add(s, Utf8Inline)) != 0;
        var data = inline ? Add(s, Utf8InlineBuffer) : ReadPtr(Add(s, Utf8Pointer));
        if (data == 0) return false;
        var bytes = ReadBytes(data, (int)count);
        var end = Array.IndexOf(bytes, (byte)0); if (end < 0) end = bytes.Length;
        value = DecodeGameString(bytes, 0, end); return true;
    }

    private AddonBounds NodeBounds(ulong node) => _layout.HasBounds
        ? AddonBounds.From(ReadF32(node, _layout.AtkResNodeScreenXOffset), ReadF32(node, _layout.AtkResNodeScreenYOffset),
            ReadU16(node, _layout.AtkResNodeWidthOffset), ReadU16(node, _layout.AtkResNodeHeightOffset), ReadF32(node, _layout.AtkResNodeScaleXOffset))
        : AddonBounds.Unknown;


    private static bool IsWanted(string name) => name.Equals(Talk, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(Subtitle, StringComparison.OrdinalIgnoreCase) || name.Equals(MiniTalk, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(AlternateMiniTalk, StringComparison.OrdinalIgnoreCase);
    private static bool IsChoice(string name) => name.StartsWith("Select", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("CutSceneSelect", StringComparison.OrdinalIgnoreCase) || name.Equals("_TextInput", StringComparison.OrdinalIgnoreCase);
    private static string DirectCodeFor(string name) => name.Equals(Talk, StringComparison.OrdinalIgnoreCase) ? DirectCode : CutsceneCode;

    private ulong ReadPtr(ulong address) => ReadU64(address);
    private ulong Add(ulong address, long offset) => offset < 0 && (ulong)(-offset) > address ? 0 : unchecked((ulong)((long)address + offset));

    private int ReadMemory(ulong address, Span<byte> destination)
    {
        if (address == 0) return 0;
        try { return _memory.Read(_pid, address, destination); }
        // Candidate pointers can refer to a window being destroyed, or to the wrong layout.
        // Only inaccessible addresses are expected misses; permission/process failures still surface.
        catch (NativeMemoryReadException ex) when (ex.ErrorCode is 5 or 14) { return 0; }
    }

    private byte[] ReadBytes(ulong address, int count)
    {
        if (address == 0 || count <= 0 || count > 1 << 20) return Array.Empty<byte>();
        var bytes = new byte[count]; var read = ReadMemory(address, bytes);
        if (read < 0) return Array.Empty<byte>();
        if (read != count) Array.Resize(ref bytes, Math.Max(0, read));
        return bytes;
    }
    private ulong ReadU64(ulong address)
    {
        Span<byte> b = stackalloc byte[8];
        return ReadMemory(address, b) == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(b) : 0;
    }
    private long ReadI64(ulong address)
    {
        Span<byte> b = stackalloc byte[8];
        return ReadMemory(address, b) == 8 ? BinaryPrimitives.ReadInt64LittleEndian(b) : 0;
    }
    private uint ReadU32(ulong address)
    {
        Span<byte> b = stackalloc byte[4];
        return ReadMemory(address, b) == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(b) : 0;
    }
    private ushort ReadU16(ulong address, long offset)
    {
        var target = Add(address, offset);
        Span<byte> b = stackalloc byte[2];
        return ReadMemory(target, b) == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(b) : (ushort)0;
    }
    private byte ReadByte(ulong address)
    {
        Span<byte> b = stackalloc byte[1];
        return ReadMemory(address, b) == 1 ? b[0] : (byte)0;
    }
    private float ReadF32(ulong address, long offset)
    {
        var target = Add(address, offset);
        Span<byte> b = stackalloc byte[4];
        return ReadMemory(target, b) == 4 ? BitConverter.ToSingle(b) : 0f;
    }

    private sealed class Candidate
    {
        public Candidate(string key, string name, string code, string speaker, string text, DialogueSurface surface, AddonBounds bounds)
        { Key = key; Name = name; Code = code; Speaker = speaker; Text = text; Surface = surface; Bounds = bounds; }
        public string Key { get; }
        public string Name { get; }
        public string Code { get; }
        public string Speaker { get; }
        public string Text { get; }
        public DialogueSurface Surface { get; }
        public AddonBounds Bounds { get; }
        public bool IsNew { get; set; }
    }
}
