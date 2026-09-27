using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lumina;
using Lumina.Data.Files;

namespace KrileHelper.UI.Services;

/// <summary>Reads world names and font icons from the attached installation; no game assets are distributed.</summary>
public sealed class GameAssets : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Bitmap> _icons = new();
    private Dictionary<int, GameIconSheet.Place> _places = new();
    private byte[]? _pixels;
    private int _width;
    private int _height;
    private string _path = "";
    public IReadOnlyCollection<string> Worlds { get; private set; } = Array.Empty<string>();
    public string Status { get; private set; } = "Game assets not loaded.";

    public void Load(string executablePath)
    {
        var folder = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(folder)) return;
        var path = Path.Combine(folder, "sqpack");
        lock (_gate)
        {
            if (_path == path && _pixels is not null && Worlds.Count > 0) return;
            Clear();
            _path = path;
            try
            {
                using var game = new GameData(path);
                Worlds = GameWorldSheet.Read(game.GetFile("exd/world.exh")?.Data!, game.GetFile("exd/world_0.exd")?.Data!);
                _places = GameIconSheet.Read(game.GetFile("common/font/gfdata.gfd")?.Data!);
                var texture = game.GetFile<TexFile>("common/font/fonticon_xinput.tex");
                if (texture is not null)
                {
                    _pixels = texture.ImageData;
                    _width = texture.Header.Width;
                    _height = texture.Header.Height;
                }
                Status = $"{Worlds.Count} worlds; {_places.Count} game icons loaded.";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Status = $"Game assets unavailable: {ex.Message}";
            }
        }
    }

    public Bitmap? Icon(int id)
    {
        lock (_gate)
        {
            if (_icons.TryGetValue(id, out var bitmap)) return bitmap;
            if (_pixels is null || !_places.TryGetValue(id, out var place) ||
                place.Left + place.Width > _width || place.Top + place.Height > _height) return null;
            var rowBytes = place.Width * 4;
            if (((long)(place.Top + place.Height - 1) * _width + place.Left) * 4 + rowBytes > _pixels.Length)
                return null;
            var result = new WriteableBitmap(new PixelSize(place.Width, place.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var buffer = result.Lock())
            {
                for (int row = 0; row < place.Height; row++)
                    Marshal.Copy(_pixels, ((place.Top + row) * _width + place.Left) * 4,
                        buffer.Address + row * buffer.RowBytes, rowBytes);
            }
            _icons.Add(id, result);
            return result;
        }
    }

    private void Clear()
    {
        foreach (var image in _icons.Values) image.Dispose();
        _icons.Clear();
        _places.Clear();
        _pixels = null;
        Worlds = Array.Empty<string>();
    }

    public void Dispose()
    {
        lock (_gate) Clear();
    }
}
