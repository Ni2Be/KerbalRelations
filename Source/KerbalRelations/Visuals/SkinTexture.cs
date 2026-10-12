using System.Collections.Generic;
using KerbalRelations.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace KerbalRelations.Visuals
{
    /// <summary>Makes recoloured copies of Kerbal head textures for children.</summary>
    public static class SkinTexture
    {
        private static readonly Dictionary<string, Texture2D> Copies = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<int, float> Shares = new Dictionary<int, float>();

        /// <summary>
        /// A copy of this head texture with the skin in the given hue, and its colour as strong as the
        /// saturation says (1 = like the green it replaces); null if it cannot be made.
        /// </summary>
        public static Texture2D Get(Texture source, float hue, float saturation)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
                return null;

            var key = source.GetInstanceID() + "/" + Mathf.RoundToInt(hue) + "/" + Mathf.RoundToInt(saturation * 100f);
            Texture2D copy;
            if (Copies.TryGetValue(key, out copy) && copy != null)
                return copy;

            copy = Read(source, source.width, source.height, true);
            var pixels = copy.GetPixels32();
            var changed = Recolour(pixels, hue, saturation);
            Log.Info(string.Format("Recoloured the skin in texture {0} ({1}x{2}) to hue {4:0}: {3:0}% of it was skin", source.name,
                source.width, source.height, 100d * changed / Mathf.Max(1, pixels.Length), hue));
            copy.SetPixels32(pixels);
            copy.name = source.name + "_KR" + Mathf.RoundToInt(hue);
            copy.wrapMode = source.wrapMode;
            copy.filterMode = source.filterMode;
            copy.anisoLevel = source.anisoLevel;
            copy.Apply(true, true);

            Copies[key] = copy;
            return copy;
        }

        /// <summary>
        /// How much of a texture is skin coloured, from 0 to 1. Measured on a small copy, so it is cheap
        /// enough to ask of every texture of a model to find the head.
        /// </summary>
        public static float SkinShare(Texture source)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
                return 0f;
            float share;
            if (Shares.TryGetValue(source.GetInstanceID(), out share))
                return share;

            const int size = 64;
            var small = Read(source, size, size, false);
            var pixels = small.GetPixels32();
            Object.Destroy(small);
            share = Recolour(pixels, 178f, 1f) / (float)Mathf.Max(1, pixels.Length);
            Shares[source.GetInstanceID()] = share;
            return share;
        }

        /// <summary>Recolours the skin in these pixels; returns how many of them changed.</summary>
        private static int Recolour(Color32[] pixels, float hue, float saturation)
        {
            var changed = 0;
            for (int i = 0; i < pixels.Length; ++i)
            {
                var before = pixels[i];
                SkinTint.Shift(ref pixels[i].r, ref pixels[i].g, ref pixels[i].b, hue, saturation);
                if (before.r != pixels[i].r || before.g != pixels[i].g || before.b != pixels[i].b)
                    ++changed;
            }
            return changed;
        }

        /// <summary>
        /// Stock textures cannot be read directly; this draws one to a render target of the wanted
        /// size and reads that. The colour space of the copy has to match the source, or it comes out
        /// too dark.
        /// </summary>
        private static Texture2D Read(Texture source, int width, int height, bool mipmaps)
        {
            var linear = !GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, mipmaps, linear);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
            return copy;
        }

        /// <summary>Throws all copies away, after the colours were changed; whoever shows one asks again.</summary>
        public static void Clear()
        {
            foreach (var copy in Copies.Values)
            {
                if (copy != null)
                    Object.Destroy(copy);
            }
            Copies.Clear();
        }
    }
}
