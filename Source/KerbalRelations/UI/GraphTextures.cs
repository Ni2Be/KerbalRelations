using System;
using System.Collections.Generic;
using Expansions;
using Expansions.Missions;
using Expansions.Serenity;
using KerbalRelations.Core;
using KerbalRelations.Visuals;
using UnityEngine;

namespace KerbalRelations.UI
{
    /// <summary>Textures for the relationship graph. Shapes are drawn in code and tinted when used.</summary>
    public static class GraphTextures
    {
        private const int ShapeSize = 96;

        private static Texture2D _disc;
        private static Texture2D _ring;
        private static Texture2D _toolbar;
        private static readonly Dictionary<string, Texture> KerbalIcons = new Dictionary<string, Texture>();
        private static readonly Dictionary<string, Texture2D> Medals = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture> MedalPictures = new Dictionary<string, Texture>();

        /// <summary>A filled white circle.</summary>
        public static Texture2D Disc
        {
            get { return _disc != null ? _disc : (_disc = MakeShape(ShapeSize, (x, y) => Circle(x, y, 0f, 0.98f))); }
        }

        /// <summary>A white ring.</summary>
        public static Texture2D Ring
        {
            get { return _ring != null ? _ring : (_ring = MakeShape(ShapeSize, (x, y) => Circle(x, y, 0.8f, 0.98f))); }
        }

        /// <summary>The toolbar button: a heart.</summary>
        public static Texture2D Toolbar
        {
            get { return _toolbar != null ? _toolbar : (_toolbar = MakeToolbar()); }
        }

        /// <summary>Opacity of a ring between two radii at a point in [-1, 1] coordinates.</summary>
        private static float Circle(float x, float y, float inner, float outer)
        {
            var d = Mathf.Sqrt(x * x + y * y);
            const float soft = 0.04f;
            return Mathf.Clamp01((outer - d) / soft) * Mathf.Clamp01((d - inner) / soft + (inner <= 0f ? 1f : 0f));
        }

        private static float Heart(float x, float y)
        {
            // Implicit heart curve; negative inside.
            y = y * 1.1f + 0.1f;
            x *= 1.1f;
            var a = x * x + y * y - 0.55f;
            var f = a * a * a - x * x * y * y * y;
            return Mathf.Clamp01(-f * 60f);
        }

        private static Texture2D MakeShape(int size, Func<float, float, float> alpha)
        {
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; ++py)
            {
                var y = (py + 0.5f) / size * 2f - 1f;
                for (int px = 0; px < size; ++px)
                {
                    var x = (px + 0.5f) / size * 2f - 1f;
                    pixels[py * size + px] = new Color(1f, 1f, 1f, alpha(x, y));
                }
            }
            return FromPixels(size, pixels);
        }

        private static Texture2D MakeToolbar()
        {
            const int size = 38;
            var pixels = new Color32[size * size];
            var heart = new Color(1f, 0.42f, 0.62f);
            for (int py = 0; py < size; ++py)
            {
                var y = (py + 0.5f) / size * 2f - 1f;
                for (int px = 0; px < size; ++px)
                {
                    var x = (px + 0.5f) / size * 2f - 1f;
                    var a = Heart(x, y);
                    pixels[py * size + px] = new Color(heart.r, heart.g, heart.b, a);
                }
            }
            return FromPixels(size, pixels);
        }

        private static Texture2D FromPixels(int size, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        /// <summary>The medal of a ribbon: a strip of its cloth and a badge with its picture.</summary>
        public static Texture2D GetMedal(RibbonDef def)
        {
            var key = def.Id + "|" + def.Stripes + "|" + def.Icon;
            Texture2D texture;
            if (Medals.TryGetValue(key, out texture) && texture != null)
                return texture;

            // Twice the size it is drawn at, so it stays crisp when the interface is scaled up.
            const int scale = 2;
            var width = RibbonArt.Width * scale;
            var height = RibbonArt.Height * scale;
            var art = RibbonArt.Render(def, scale);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; ++y)
            {
                // The art has its top row first, a texture its bottom row.
                var from = (height - 1 - y) * width * 4;
                for (int x = 0; x < width; ++x)
                    pixels[y * width + x] = new Color32(art[from + x * 4], art[from + x * 4 + 1], art[from + x * 4 + 2], art[from + x * 4 + 3]);
            }

            texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            Medals[key] = texture;
            return texture;
        }

        /// <summary>
        /// For a ribbon of another mod that brings its own picture as a texture: that texture, to be
        /// drawn on the badge. Null for the built-in pictures, which are part of the medal already.
        /// </summary>
        public static Texture GetMedalPicture(RibbonDef def)
        {
            if (string.IsNullOrEmpty(def.Icon) || RibbonArt.Glyph(def.Icon) != null)
                return null;
            Texture picture;
            if (!MedalPictures.TryGetValue(def.Icon, out picture))
            {
                picture = GameDatabase.Instance.GetTexture(def.Icon, false);
                MedalPictures[def.Icon] = picture;
            }
            return picture;
        }

        /// <summary>The picture for a node: the Astronaut Complex portrait, or the egg's own shell.</summary>
        public static Texture GetIcon(ProtoCrewMember pcm, LifeStage stage)
        {
            if (stage == LifeStage.Egg)
                return EggMesh.GetIcon(pcm.name);

            // Suit and veteran status change the portrait, so they are part of the key.
            var key = pcm.name + "|" + pcm.suit + "|" + pcm.veteran + "|" + pcm.type + "|" + pcm.SpritePath;
            Texture icon;
            if (KerbalIcons.TryGetValue(key, out icon) && icon != null)
                return icon;
            icon = LoadKerbalIcon(pcm);
            KerbalIcons[key] = icon;
            return icon;
        }

        /// <summary>Follows what the stock crew list does to find a Kerbal's portrait.</summary>
        private static Texture LoadKerbalIcon(ProtoCrewMember pcm)
        {
            Texture texture = null;
            try
            {
                if (pcm.SpritePath == null)
                {
                    var path = pcm.GetKerbalIconSuitPath();
                    if (path.Contains("vintage") && ExpansionsLoader.IsExpansionInstalled("MakingHistory"))
                        texture = MissionsUtils.METexture(path + ".tif");
                    else if (path.Contains("future") && ExpansionsLoader.IsExpansionInstalled("Serenity"))
                        texture = SerenityUtils.SerenityTexture(path + ".tif");
                    else
                        texture = AssetBase.GetTexture(path);
                }
                else
                {
                    texture = GameDatabase.Instance.GetTexture(pcm.SpritePath, false);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("No portrait for " + pcm.name + ": " + ex.Message);
            }
            return texture != null ? texture : AssetBase.GetTexture("kerbalicon_suit");
        }
    }
}
