using System;

namespace KerbalRelations.Core
{
    /// <summary>
    /// Generates the fun egg textures as raw RGBA bytes (row 0 at the bottom, like Unity textures).
    /// Every egg gets its own colours and pattern from its name, and keeps them across reloads.
    /// </summary>
    public static class EggPattern
    {
        public enum Kind
        {
            Spots,
            Stripes,
            ZigZag,
            Speckles
        }

        private struct Rgb
        {
            public double R, G, B;

            public Rgb(double r, double g, double b)
            {
                R = r;
                G = g;
                B = b;
            }

            public static Rgb Lerp(Rgb a, Rgb b, double t)
            {
                t = Math.Max(0d, Math.Min(1d, t));
                return new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
            }
        }

        public static Kind KindOf(string seed)
        {
            return (Kind)(StableHash.Of(seed + "#kind") % 4u);
        }

        /// <summary>A tileable texture for wrapping around the egg mesh (u around, v bottom to top).</summary>
        public static byte[] Generate(string seed, int width, int height)
        {
            var data = new byte[width * height * 4];
            var palette = new Palette(seed);
            var kind = KindOf(seed);
            var random = new Random(unchecked((int)StableHash.Of(seed)));
            var spots = MakeSpots(random, kind == Kind.Speckles ? 60 : 14, kind == Kind.Speckles ? 0.012d : 0.05d);

            for (int y = 0; y < height; ++y)
            {
                var v = (y + 0.5d) / height;
                for (int x = 0; x < width; ++x)
                {
                    var u = (x + 0.5d) / width;
                    var c = Shade(kind, palette, spots, u, v);
                    var o = (y * width + x) * 4;
                    data[o] = ToByte(c.R);
                    data[o + 1] = ToByte(c.G);
                    data[o + 2] = ToByte(c.B);
                    data[o + 3] = 255;
                }
            }
            return data;
        }

        /// <summary>A flat egg-shaped icon with a transparent background, for the relationship graph.</summary>
        public static byte[] GenerateIcon(string seed, int size)
        {
            var data = new byte[size * size * 4];
            var palette = new Palette(seed);
            var kind = KindOf(seed);
            var random = new Random(unchecked((int)StableHash.Of(seed)));
            var spots = MakeSpots(random, kind == Kind.Speckles ? 60 : 14, kind == Kind.Speckles ? 0.012d : 0.05d);

            for (int y = 0; y < size; ++y)
            {
                // -1 at the bottom, +1 at the top.
                var py = (y + 0.5d) / size * 2d - 1d;
                for (int x = 0; x < size; ++x)
                {
                    var px = (x + 0.5d) / size * 2d - 1d;
                    var halfWidth = EggHalfWidth(py * 1.08d) * 0.78d;
                    var o = (y * size + x) * 4;
                    var edge = halfWidth - Math.Abs(px);
                    if (halfWidth <= 0d || edge <= 0d)
                        continue;

                    // Wrap the pattern around the visible half of the egg and shade towards the rim.
                    var across = px / halfWidth;
                    var u = 0.25d + Math.Asin(Math.Max(-1d, Math.Min(1d, across))) / (Math.PI * 2d);
                    var v = (py + 1d) / 2d;
                    var c = Shade(kind, palette, spots, u, v);
                    var light = 0.72d + 0.28d * Math.Sqrt(Math.Max(0d, 1d - across * across));
                    data[o] = ToByte(c.R * light);
                    data[o + 1] = ToByte(c.G * light);
                    data[o + 2] = ToByte(c.B * light);
                    data[o + 3] = ToByte(Math.Min(1d, edge * size / 2d));
                }
            }
            return data;
        }

        /// <summary>
        /// Half-width of the egg outline at height y in [-1, 1] (0 outside). Wide at the bottom, narrow at
        /// the top; the same profile is revolved to build the 3D mesh.
        /// </summary>
        public static double EggHalfWidth(double y)
        {
            if (y <= -1d || y >= 1d)
                return 0d;
            var theta = Math.Acos(-y);
            return Math.Sin(theta) * (1d - 0.2d * y);
        }

        private sealed class Palette
        {
            public readonly Rgb Base;
            public readonly Rgb Accent;
            public readonly Rgb Second;

            public Palette(string seed)
            {
                var hue = StableHash.Unit(seed + "#hue");
                Base = FromHsv(hue, 0.28d, 0.97d);
                Accent = FromHsv(hue + 0.45d + 0.2d * StableHash.Unit(seed + "#accent"), 0.7d, 0.85d);
                Second = FromHsv(hue + 0.2d, 0.55d, 0.95d);
            }
        }

        private static double[] MakeSpots(Random random, int count, double size)
        {
            // u, v, radius per spot
            var spots = new double[count * 3];
            for (int i = 0; i < count; ++i)
            {
                spots[i * 3] = random.NextDouble();
                spots[i * 3 + 1] = 0.08d + random.NextDouble() * 0.84d;
                spots[i * 3 + 2] = size * (0.6d + random.NextDouble() * 0.9d);
            }
            return spots;
        }

        private static Rgb Shade(Kind kind, Palette p, double[] spots, double u, double v)
        {
            switch (kind)
            {
                case Kind.Stripes:
                {
                    var band = Math.Sin(v * Math.PI * 9d);
                    var c = Rgb.Lerp(p.Base, p.Accent, (band - 0.35d) * 6d);
                    return Rgb.Lerp(c, p.Second, (Math.Sin(v * Math.PI * 9d + Math.PI) - 0.8d) * 12d);
                }
                case Kind.ZigZag:
                {
                    var tri = Math.Abs(Frac(u * 8d) - 0.5d) * 2d;
                    var c = p.Base;
                    for (int i = 0; i < 3; ++i)
                    {
                        var centre = 0.25d + i * 0.25d + (tri - 0.5d) * 0.09d;
                        var d = Math.Abs(v - centre);
                        c = Rgb.Lerp(c, i == 1 ? p.Second : p.Accent, (0.03d - d) * 90d);
                    }
                    return c;
                }
                default:
                {
                    var c = p.Base;
                    for (int i = 0; i < spots.Length; i += 3)
                    {
                        var du = Math.Abs(u - spots[i]);
                        if (du > 0.5d)
                            du = 1d - du;
                        // The texture is twice as wide as tall once wrapped, so stretch u to keep spots round.
                        du *= 2d;
                        var dv = v - spots[i + 1];
                        var d = Math.Sqrt(du * du + dv * dv);
                        var colour = (i / 3) % 3 == 0 ? p.Second : p.Accent;
                        c = Rgb.Lerp(c, colour, (spots[i + 2] - d) * 70d);
                    }
                    return c;
                }
            }
        }

        private static double Frac(double value)
        {
            return value - Math.Floor(value);
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Max(0d, Math.Min(255d, Math.Round(value * 255d)));
        }

        private static Rgb FromHsv(double h, double s, double v)
        {
            h = Frac(h) * 6d;
            var sector = (int)Math.Floor(h);
            var f = h - sector;
            var p = v * (1d - s);
            var q = v * (1d - s * f);
            var t = v * (1d - s * (1d - f));
            switch (sector % 6)
            {
                case 0: return new Rgb(v, t, p);
                case 1: return new Rgb(q, v, p);
                case 2: return new Rgb(p, v, t);
                case 3: return new Rgb(p, q, v);
                case 4: return new Rgb(t, p, v);
                default: return new Rgb(v, p, q);
            }
        }
    }
}
