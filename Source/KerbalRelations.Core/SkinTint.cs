using System;

namespace KerbalRelations.Core
{
    /// <summary>
    /// Recolours Kerbal skin. Only the yellowish green of the skin is moved to the new hue, with its
    /// light and shade kept; hair, mouth and anything grey stay as they are.
    /// </summary>
    public static class SkinTint
    {
        /// <summary>Hue of stock Kerbal skin, in degrees.</summary>
        public const double SkinHue = 75d;

        private const double HueFrom = 50d;
        private const double HueTo = 140d;
        private const double HueEdge = 12d;
        private const double SaturationFrom = 0.12d;
        private const double SaturationFull = 0.28d;

        /// <summary>Recolours one pixel in place. A saturation scale below 1 makes the new colour paler.</summary>
        public static void Shift(ref byte r, ref byte g, ref byte b, double targetHue, double saturationScale = 1d)
        {
            double red = r / 255d, green = g / 255d, blue = b / 255d;
            var max = Math.Max(red, Math.Max(green, blue));
            var min = Math.Min(red, Math.Min(green, blue));
            var delta = max - min;
            if (max <= 0d || delta <= 0d)
                return;
            var saturation = delta / max;
            if (saturation <= SaturationFrom)
                return;

            double hue;
            if (max == red)
                hue = 60d * ((green - blue) / delta);
            else if (max == green)
                hue = 60d * ((blue - red) / delta + 2d);
            else
                hue = 60d * ((red - green) / delta + 4d);
            if (hue < 0d)
                hue += 360d;
            if (hue <= HueFrom || hue >= HueTo)
                return;

            // Fade in at the edges of the skin range, so there are no hard seams.
            var weight = Math.Min(1d, Math.Min(hue - HueFrom, HueTo - hue) / HueEdge)
                * Math.Min(1d, (saturation - SaturationFrom) / (SaturationFull - SaturationFrom));

            // Keep half of the hue variation of the skin, so it does not turn flat.
            var newHue = ((targetHue + (hue - SkinHue) * 0.5d) % 360d + 360d) % 360d;
            double nr, ng, nb;
            FromHsv(newHue, Math.Max(0d, Math.Min(1d, saturation * saturationScale)), max, out nr, out ng, out nb);

            r = ToByte(red + (nr - red) * weight);
            g = ToByte(green + (ng - green) * weight);
            b = ToByte(blue + (nb - blue) * weight);
        }

        /// <summary>Hue in degrees [0, 360), saturation and value in [0, 1].</summary>
        public static void FromHsv(double hue, double saturation, double value, out double r, out double g, out double b)
        {
            var sector = hue / 60d;
            var chroma = value * saturation;
            var x = chroma * (1d - Math.Abs(sector % 2d - 1d));
            var m = value - chroma;
            switch ((int)sector)
            {
                case 0: r = chroma; g = x; b = 0d; break;
                case 1: r = x; g = chroma; b = 0d; break;
                case 2: r = 0d; g = chroma; b = x; break;
                case 3: r = 0d; g = x; b = chroma; break;
                case 4: r = x; g = 0d; b = chroma; break;
                default: r = chroma; g = 0d; b = x; break;
            }
            r += m;
            g += m;
            b += m;
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Max(0d, Math.Min(255d, Math.Round(value * 255d)));
        }
    }
}
