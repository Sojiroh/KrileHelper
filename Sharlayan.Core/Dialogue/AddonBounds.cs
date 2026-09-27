namespace Sharlayan.Core.Dialogue;

/// <summary>Where an in-game UI addon is drawn in client coordinates.</summary>
public readonly struct AddonBounds : IEquatable<AddonBounds>
{
    public static AddonBounds Unknown => default;

    private AddonBounds(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsKnown = true;
    }

    public bool IsKnown { get; }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }

    public static AddonBounds From(float x, float y, ushort width, ushort height, float scale)
    {
        if (width == 0 || height == 0 || !(scale > 0f) ||
            float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y) ||
            float.IsNaN(scale) || float.IsInfinity(scale))
            return Unknown;

        var scaledWidth = width * scale;
        var scaledHeight = height * scale;
        return float.IsFinite(scaledWidth) && float.IsFinite(scaledHeight) &&
               scaledWidth > 0f && scaledHeight > 0f
            ? new AddonBounds(x, y, scaledWidth, scaledHeight)
            : Unknown;
    }

    public bool Equals(AddonBounds other) => IsKnown == other.IsKnown &&
        (!IsKnown || (X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height)));
    public override bool Equals(object? obj) => obj is AddonBounds other && Equals(other);
    public override int GetHashCode() => IsKnown ? HashCode.Combine(X, Y, Width, Height) : 0;
    public static bool operator ==(AddonBounds left, AddonBounds right) => left.Equals(right);
    public static bool operator !=(AddonBounds left, AddonBounds right) => !left.Equals(right);
    public override string ToString() => IsKnown ? $"{X},{Y} {Width}x{Height}" : "unknown";
}
