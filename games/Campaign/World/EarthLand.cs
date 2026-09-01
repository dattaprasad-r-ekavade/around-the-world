using Microsoft.Xna.Framework;
using System;
using System.IO;

namespace Campaign;

/// <summary>
/// Natural Earth 50m land, 0.125° cells, packed 1-bit. Sampled in lon/lat so
/// coasts sit where they sit on Earth, not as painted ellipses.
/// </summary>
public static class EarthLand
{
    public const int Width = 2880;
    public const int Height = 1440;

    private static byte[] _bits = [];

    public static bool Ready => _bits.Length > 0;

    public static void Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8)
            throw new InvalidDataException("Earth land mask is empty.");
        var width = bytes[0] | (bytes[1] << 8);
        var height = bytes[2] | (bytes[3] << 8);
        if (width != Width || height != Height)
            throw new InvalidDataException($"Earth land mask is {width}×{height}, expected {Width}×{Height}.");
        var need = (Width * Height + 7) / 8;
        if (bytes.Length < 4 + need)
            throw new InvalidDataException("Earth land mask is truncated.");
        _bits = new byte[need];
        Buffer.BlockCopy(bytes, 4, _bits, 0, need);
    }

    public static float Field(float x, float z) =>
        0.15f + Sample(EarthGlobe.Lon(x), EarthGlobe.Lat(z)) * 0.75f;

    public static bool LandAt(float lon, float lat) => Sample(lon, lat) >= 0.5f;

    public static float Sample(float lon, float lat)
    {
        if (_bits.Length == 0) return 0f;

        var u = (lon + 180f) / 360f * Width;
        var v = (90f - lat) / 180f * Height;
        if (v < 0f) v = 0f;
        if (v > Height - 1.001f) v = Height - 1.001f;

        var x0 = (int)MathF.Floor(u);
        var y0 = (int)MathF.Floor(v);
        var tx = u - x0;
        var ty = v - y0;
        var x1 = x0 + 1;
        var y1 = Math.Min(Height - 1, y0 + 1);
        x0 = WrapX(x0);
        x1 = WrapX(x1);

        var a = Bit(x0, y0);
        var b = Bit(x1, y0);
        var c = Bit(x0, y1);
        var d = Bit(x1, y1);
        return MathHelper.Lerp(MathHelper.Lerp(a, b, tx), MathHelper.Lerp(c, d, tx), ty);
    }

    private static int WrapX(int x)
    {
        x %= Width;
        return x < 0 ? x + Width : x;
    }

    private static float Bit(int x, int y)
    {
        var i = y * Width + x;
        return (_bits[i >> 3] & (1 << (i & 7))) != 0 ? 1f : 0f;
    }
}
