using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Translation.Core.Reference;

namespace Translation.Core;

/// <summary>
/// Looks up the hand-made Russian XIV translation before a provider is used.
/// The index is optional: construction only opens an index already on disk and
/// never downloads the large xivrus export implicitly.
/// </summary>
public sealed class ReferenceTranslationService : IDisposable
{
    public const string TargetLanguage = "ru";

    private readonly object _sync = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly string? _databasePathOverride;
    private readonly Action<string>? _log;
    private SqliteReferenceTranslationSource? _source;
    private string _loadedLanguage = string.Empty;
    private string _loadedPath = string.Empty;
    private int _isUpdating;
    private bool _disposed;

    public ReferenceTranslationService(string? databasePath = null, Action<string>? log = null)
    {
        _databasePathOverride = string.IsNullOrWhiteSpace(databasePath)
            ? null
            : SqliteReferenceTranslationSource.Resolve(databasePath);
        _log = log;

        lock (_sync)
        {
            EnsureSourceLocked("en");
        }
    }

    public bool IsUpdating => Volatile.Read(ref _isUpdating) != 0;

    public bool IsAvailable
    {
        get
        {
            lock (_sync)
            {
                return !_disposed && _source?.IsAvailable == true;
            }
        }
    }

    public string DatabasePath
    {
        get
        {
            lock (_sync)
            {
                return _source?.DatabasePath ?? _loadedPath;
            }
        }
    }

    public string LanguageCode
    {
        get
        {
            lock (_sync)
            {
                return _source?.LanguageCode ?? string.Empty;
            }
        }
    }

    public string SourceLanguageCode
    {
        get
        {
            lock (_sync)
            {
                return _source?.SourceLanguageCode ?? string.Empty;
            }
        }
    }

    public string Revision
    {
        get
        {
            lock (_sync)
            {
                return _source?.Revision ?? string.Empty;
            }
        }
    }

    public int LineCount
    {
        get
        {
            lock (_sync)
            {
                return _source?.LineCount ?? 0;
            }
        }
    }

    public int RulesVersion
    {
        get
        {
            lock (_sync)
            {
                return _source?.RulesVersion ?? 0;
            }
        }
    }

    /// <summary>
    /// Tries the reference index for a game line. Only source languages
    /// published with the game and Russian as the target are supported; false
    /// means that no reference line answered and a provider may be tried.
    /// </summary>
    public bool TryTranslate(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string? playerName,
        bool? playerIsFeminine,
        out string translated)
    {
        translated = string.Empty;

        if (!CanLookup(sourceLanguage, targetLanguage) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            var source = EnsureSourceLocked(sourceLanguage);
            if (source is null || !CanUseSource(source, sourceLanguage))
            {
                return false;
            }

            source.PlayerName = playerName ?? string.Empty;
            source.PlayerIsFeminine = playerIsFeminine;

            if (source.TryGetTranslation(text, out translated))
            {
                return true;
            }

            return text.IndexOf('\n') >= 0 &&
                   TryTranslateLinesLocked(source, text, out translated);
        }
    }

    public bool TryTranslateSpeaker(
        string speaker,
        string sourceLanguage,
        string targetLanguage,
        out string translated)
    {
        translated = string.Empty;

        if (!CanLookup(sourceLanguage, targetLanguage) || string.IsNullOrWhiteSpace(speaker))
        {
            return false;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            var source = EnsureSourceLocked(sourceLanguage);
            if (!CanUseSource(source, sourceLanguage))
            {
                return false;
            }

            return source!.TryGetSpeakerName(speaker, out translated);
        }
    }

    /// <summary>
    /// Downloads the current xivrus export, builds a SQLite index, and installs
    /// it atomically in the XDG data directory. Updates for the same language
    /// are serialized while lookups continue against the previous index.
    /// </summary>
    public async Task<ReferenceUpdateResult> UpdateAsync(
        string sourceLanguage,
        IProgress<ReferenceUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!ReferenceIndexUpdater.IsGameLanguage(sourceLanguage))
        {
            return new ReferenceUpdateResult(
                ReferenceUpdateOutcome.Failed,
                "The source language must be one of: " + string.Join(", ", ReferenceIndexUpdater.GameLanguages) + ".",
                0);
        }

        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _isUpdating, 1);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            string databasePath;
            string currentRevision;
            lock (_sync)
            {
                if (_disposed)
                {
                    return new ReferenceUpdateResult(ReferenceUpdateOutcome.Failed, "The reference service is disposed.", 0);
                }
                var source = EnsureSourceLocked(sourceLanguage);
                databasePath = ResolveUserDatabasePath(sourceLanguage);
                currentRevision = source is not null &&
                                  source.IsAvailable &&
                                  source.RulesVersion == ReferenceIndexBuilder.RulesVersion &&
                                  string.Equals(source.SourceLanguageCode, sourceLanguage, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(source.LanguageCode, TargetLanguage, StringComparison.OrdinalIgnoreCase)
                    ? source.Revision
                    : string.Empty;
            }

            var updater = new ReferenceIndexUpdater(_log);
            var result = await updater.UpdateAsync(
                databasePath,
                sourceLanguage,
                TargetLanguage,
                currentRevision,
                progress,
                // Linux permits replacing an open inode. Keeping the old
                // connection open until the new file is in place means lookups
                // never observe a disposed connection during the download.
                releaseIndex: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Outcome == ReferenceUpdateOutcome.Updated)
            {
                lock (_sync)
                {
                    if (!_disposed)
                    {
                        ReloadSourceLocked(sourceLanguage);
                    }
                }
            }

            return result;
        }
        finally
        {
            Volatile.Write(ref _isUpdating, 0);
            _updateGate.Release();
        }
    }

    public Task<ReferenceUpdateResult> UpdateAsync(
        string sourceLanguage,
        CancellationToken cancellationToken) =>
        UpdateAsync(sourceLanguage, null, cancellationToken);

    /// <summary>Builds and installs an index from an unpacked xivrus export.</summary>
    public ReferenceUpdateResult BuildFromFolder(
        string sourceLanguage,
        string exportRoot,
        IProgress<ReferenceUpdateProgress>? progress = null)
    {
        if (!ReferenceIndexUpdater.IsGameLanguage(sourceLanguage))
        {
            return new ReferenceUpdateResult(
                ReferenceUpdateOutcome.Failed,
                "The source language must be one of: " + string.Join(", ", ReferenceIndexUpdater.GameLanguages) + ".",
                0);
        }

        _updateGate.Wait();
        Volatile.Write(ref _isUpdating, 1);
        try
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return new ReferenceUpdateResult(ReferenceUpdateOutcome.Failed, "The reference service is disposed.", 0);
                }
            }

            var updater = new ReferenceIndexUpdater(_log);
            var result = updater.BuildFromFolder(
                ResolveUserDatabasePath(sourceLanguage), sourceLanguage, TargetLanguage, exportRoot, progress);
            if (result.Outcome == ReferenceUpdateOutcome.Updated)
            {
                lock (_sync)
                {
                    if (!_disposed)
                    {
                        ReloadSourceLocked(sourceLanguage);
                    }
                }
            }

            return result;
        }
        finally
        {
            Volatile.Write(ref _isUpdating, 0);
            _updateGate.Release();
        }
    }

    private bool CanLookup(string sourceLanguage, string targetLanguage) =>
        ReferenceIndexUpdater.IsGameLanguage(sourceLanguage) &&
        string.Equals(targetLanguage, TargetLanguage, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase);

    private static bool CanUseSource(SqliteReferenceTranslationSource? source, string sourceLanguage) =>
        source?.IsAvailable == true &&
        string.Equals(source.SourceLanguageCode, sourceLanguage, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(source.LanguageCode, TargetLanguage, StringComparison.OrdinalIgnoreCase);

    private static bool TryTranslateLinesLocked(
        SqliteReferenceTranslationSource source,
        string text,
        out string translated)
    {
        translated = string.Empty;
        var lines = text.Split('\n');
        if (lines.Length < 2)
        {
            return false;
        }

        var result = new string[lines.Length];
        var hasText = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                result[i] = string.Empty;
                continue;
            }

            hasText = true;
            var marker = ListMarker(line);
            if (!source.TryGetTranslation(line.Substring(marker.Length), out var known))
            {
                return false;
            }

            result[i] = marker + known;
        }

        if (!hasText)
        {
            return false;
        }

        translated = string.Join("\n", result);
        return true;
    }

    private static string ListMarker(string line)
    {
        var digits = 0;
        while (digits < line.Length && char.IsDigit(line[digits]))
        {
            digits++;
        }

        if (digits == 0 || digits > 2 || digits >= line.Length ||
            (line[digits] != '.' && line[digits] != ')'))
        {
            return string.Empty;
        }

        var after = digits + 1;
        while (after < line.Length && line[after] == ' ')
        {
            after++;
        }

        return after < line.Length ? line[..after] : string.Empty;
    }

    private string ResolveDatabasePath(string sourceLanguage)
    {
        if (_databasePathOverride != null)
        {
            return _databasePathOverride;
        }

        var userPath = ReferenceIndexLocation.DefaultDatabasePath(sourceLanguage, TargetLanguage);
        var shippedPath = ReferenceIndexLocation.DefaultShippedPath(sourceLanguage, TargetLanguage);
        return ReferenceIndexLocation.Choose(userPath, shippedPath, _log);
    }

    private string ResolveUserDatabasePath(string sourceLanguage)
    {
        if (_databasePathOverride != null)
        {
            return _databasePathOverride;
        }

        return ReferenceIndexLocation.DefaultDatabasePath(sourceLanguage, TargetLanguage);
    }

    private SqliteReferenceTranslationSource? EnsureSourceLocked(string sourceLanguage)
    {
        var requested = sourceLanguage.Trim().ToLowerInvariant();
        var path = ResolveDatabasePath(requested);
        if (string.Equals(_loadedLanguage, requested, StringComparison.Ordinal) &&
            string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return _source;
        }

        _source?.Dispose();
        _source = new SqliteReferenceTranslationSource(path, _log);
        _loadedLanguage = requested;
        _loadedPath = path;
        return _source;
    }

    private void ReloadSourceLocked(string sourceLanguage)
    {
        _loadedLanguage = string.Empty;
        _loadedPath = string.Empty;
        EnsureSourceLocked(sourceLanguage);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _source?.Dispose();
            _source = null;
        }

        // Do not dispose the gate: a caller can safely dispose after cancelling
        // an update without racing a finally block that releases it.
    }
}
