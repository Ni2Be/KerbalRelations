using System;
using System.Collections.Generic;
using System.Linq;
using KerbalRelations.Core;
using Xunit;

namespace KerbalRelations.Tests
{
    public class CurveTests
    {
        [Theory]
        [InlineData(0d, 0.5d)]
        [InlineData(50d, 0.75d)]
        [InlineData(70d, 0.85d)]
        [InlineData(100d, 1d)]
        [InlineData(150d, 1.5d)]
        [InlineData(200d, 2d)]
        [InlineData(-50d, 0.5d)]
        [InlineData(900d, 2d)]
        public void Default_curve_maps_happiness_to_half_up_to_double(double happiness, double expected)
        {
            Assert.Equal(expected, Curve.Default().Evaluate(happiness), 9);
        }

        [Fact]
        public void Each_effect_can_have_its_own_curve()
        {
            var registry = new EffectRegistry();
            registry.Set(EffectRegistry.RunSpeed, new Curve().Add(200d, 1.2d).Add(0d, 0.8d));

            Assert.Equal(1.2d, registry.GetMultiplier(EffectRegistry.RunSpeed, 200d), 9);
            Assert.Equal(1d, registry.GetMultiplier(EffectRegistry.RunSpeed, 100d), 9);
            Assert.Equal(2d, registry.GetMultiplier("SomeOtherMod", 200d), 9);
        }
    }

    public class GraphLayoutTests
    {
        [Fact]
        public void Stronger_relationships_end_up_closer_together()
        {
            var layout = new GraphLayout();
            layout.Sync(new[] { "A", "B", "C" });
            var edges = new List<GraphEdge>
            {
                new GraphEdge { A = "A", B = "B", Strength = 100d },
                new GraphEdge { A = "A", B = "C", Strength = 5d }
            };
            layout.Run(edges, 800);

            Assert.True(layout.Distance("A", "B") < layout.Distance("A", "C"));
            Assert.InRange(layout.Distance("A", "B"), layout.MinLength * 0.8d, layout.MinLength * 1.8d);
        }

        [Fact]
        public void The_layout_settles_and_stays_inside_its_bounds()
        {
            var layout = new GraphLayout();
            var ids = new List<string>();
            var edges = new List<GraphEdge>();
            for (int i = 0; i < 14; ++i)
            {
                ids.Add("K" + i);
                if (i > 0)
                    edges.Add(new GraphEdge { A = "K" + (i - 1), B = "K" + i, Strength = i * 7d });
            }
            layout.Sync(ids);
            layout.Run(edges, 1500);

            Assert.True(layout.Step(edges) < 1d);
            foreach (var node in layout.Nodes.Values)
            {
                Assert.InRange(node.X, -layout.Width / 2d, layout.Width / 2d);
                Assert.InRange(node.Y, -layout.Height / 2d, layout.Height / 2d);
            }
        }

        [Fact]
        public void Nodes_never_sit_on_top_of_each_other()
        {
            var layout = new GraphLayout();
            layout.Sync(new[] { "A", "B" });
            layout.Nodes["A"].X = layout.Nodes["B"].X = 0d;
            layout.Nodes["A"].Y = layout.Nodes["B"].Y = 0d;
            layout.Run(new List<GraphEdge>(), 200);
            Assert.True(layout.Distance("A", "B") > 20d);
        }

        [Fact]
        public void Sync_keeps_existing_positions_and_drops_removed_nodes()
        {
            var layout = new GraphLayout();
            layout.Sync(new[] { "A", "B" });
            layout.Nodes["A"].X = 42d;
            layout.Sync(new[] { "A", "C" });

            Assert.Equal(42d, layout.Nodes["A"].X);
            Assert.False(layout.Nodes.ContainsKey("B"));
            Assert.True(layout.Nodes.ContainsKey("C"));
        }

        [Fact]
        public void A_pinned_node_does_not_move()
        {
            var layout = new GraphLayout();
            layout.Sync(new[] { "A", "B" });
            layout.Nodes["A"].Pinned = true;
            var x = layout.Nodes["A"].X;
            layout.Run(new List<GraphEdge> { new GraphEdge { A = "A", B = "B", Strength = 100d } }, 100);
            Assert.Equal(x, layout.Nodes["A"].X);
        }
    }

    public class EggPatternTests
    {
        [Fact]
        public void Texture_has_the_requested_size_and_is_opaque()
        {
            var data = EggPattern.Generate("Eggbert Kerman", 64, 32);
            Assert.Equal(64 * 32 * 4, data.Length);
            for (int i = 3; i < data.Length; i += 4)
                Assert.Equal(255, data[i]);
        }

        [Fact]
        public void The_same_egg_always_looks_the_same_and_different_eggs_differ()
        {
            Assert.Equal(EggPattern.Generate("Eggbert Kerman", 32, 16), EggPattern.Generate("Eggbert Kerman", 32, 16));
            Assert.NotEqual(EggPattern.Generate("Eggbert Kerman", 32, 16), EggPattern.Generate("Shelly Kerman", 32, 16));
        }

        [Fact]
        public void Icon_is_egg_shaped_with_a_transparent_background()
        {
            const int size = 48;
            var data = EggPattern.GenerateIcon("Eggbert Kerman", size);
            Assert.Equal(size * size * 4, data.Length);

            int Alpha(int x, int y) => data[(y * size + x) * 4 + 3];
            Assert.Equal(0, Alpha(0, 0));
            Assert.Equal(0, Alpha(size - 1, size - 1));
            Assert.Equal(255, Alpha(size / 2, size / 2));

            // Wider near the bottom (row 0 is the bottom) than near the top.
            int Width(int y)
            {
                var count = 0;
                for (int x = 0; x < size; ++x)
                {
                    if (Alpha(x, y) > 0)
                        ++count;
                }
                return count;
            }
            Assert.True(Width(size / 4) > Width(3 * size / 4));
        }

        [Fact]
        public void Profile_is_closed_at_both_ends()
        {
            Assert.Equal(0d, EggPattern.EggHalfWidth(-1d));
            Assert.Equal(0d, EggPattern.EggHalfWidth(1d));
            Assert.True(EggPattern.EggHalfWidth(-0.5d) > EggPattern.EggHalfWidth(0.5d));
        }
    }

    public class SkinTintTests
    {
        private static byte[] Shift(byte r, byte g, byte b, double hue, double saturation = 1d)
        {
            SkinTint.Shift(ref r, ref g, ref b, hue, saturation);
            return new[] { r, g, b };
        }

        [Fact]
        public void A_lower_saturation_gives_a_paler_pink()
        {
            var strong = Shift(186, 218, 85, 335d);
            var pale = Shift(186, 218, 85, 335d, 0.7d);
            foreach (var pink in new[] { strong, pale })
            {
                Assert.Equal(218, pink[0]);
                Assert.True(pink[1] < pink[2] && pink[2] < pink[0], "not pink: " + string.Join(",", pink));
            }
            Assert.True(pale[1] > strong[1] + 20, "not paler: " + string.Join(",", pale));
        }

        [Fact]
        public void Skin_green_becomes_teal_and_keeps_its_brightness()
        {
            // Stock Kerbal skin, lit and in shade.
            var lit = Shift(186, 218, 85, 178d);
            Assert.True(lit[1] > lit[0] * 2 && lit[2] > lit[0] * 2, "not teal: " + string.Join(",", lit));
            Assert.True(Math.Abs(lit[1] - lit[2]) < 30);
            Assert.Equal(218, Math.Max(lit[0], Math.Max(lit[1], lit[2])));

            var shade = Shift(93, 109, 42, 178d);
            Assert.True(shade[2] > shade[0] * 2);
            Assert.Equal(109, Math.Max(shade[0], Math.Max(shade[1], shade[2])));
        }

        [Fact]
        public void Hair_mouth_and_greys_are_left_alone()
        {
            Assert.Equal(new byte[] { 90, 60, 30 }, Shift(90, 60, 30, 178d));
            Assert.Equal(new byte[] { 200, 80, 90 }, Shift(200, 80, 90, 178d));
            Assert.Equal(new byte[] { 128, 128, 128 }, Shift(128, 128, 128, 178d));
            Assert.Equal(new byte[] { 0, 0, 0 }, Shift(0, 0, 0, 178d));
            Assert.Equal(new byte[] { 255, 255, 255 }, Shift(255, 255, 255, 178d));
            Assert.Equal(new byte[] { 60, 80, 200 }, Shift(60, 80, 200, 178d));
        }
    }

    public class RibbonArtTests
    {
        private const int Scale = 2;

        private static byte[] Pixel(byte[] art, double x, double y)
        {
            var at = ((int)(y * Scale) * RibbonArt.Width * Scale + (int)(x * Scale)) * 4;
            return new[] { art[at], art[at + 1], art[at + 2], art[at + 3] };
        }

        [Fact]
        public void Every_picture_is_sixteen_by_sixteen()
        {
            Assert.NotEmpty(RibbonArt.GlyphNames);
            foreach (var name in RibbonArt.GlyphNames)
            {
                var rows = RibbonArt.Glyph(name);
                Assert.True(rows.Length == RibbonArt.GlyphSize, name);
                Assert.All(rows, row => Assert.True(row.Length == RibbonArt.GlyphSize && row.Trim('.', '#', '+').Length == 0, name + ": " + row));
            }
        }

        [Fact]
        public void Every_ribbon_has_a_picture_and_no_two_built_in_ones_share_it()
        {
            var builtIn = Ribbons.BuiltIn();
            Assert.All(builtIn, r => Assert.True(RibbonArt.Glyph(r.Icon) != null, r.Id));
            Assert.Equal(builtIn.Count, builtIn.Select(r => r.Icon).Distinct().Count());
            foreach (var prefix in new[] { RibbonIds.VisitPrefix, RibbonIds.LandPrefix, RibbonIds.FirstBornPrefix })
                Assert.NotNull(RibbonArt.Glyph(Ribbons.ForBody(prefix + "Mun").Icon));
        }

        [Fact]
        public void A_medal_is_cloth_with_a_badge_and_the_picture_on_it()
        {
            var def = new RibbonDef("X", "X", string.Empty, "d8477f:3,ffffff:1,d8477f:3", "heart");
            var art = RibbonArt.Render(def, Scale);
            Assert.Equal(RibbonArt.Width * Scale * RibbonArt.Height * Scale * 4, art.Length);

            // Clear in the corners, the white stripe down the middle of the cloth, the badge below.
            Assert.Equal(0, Pixel(art, 0.5d, 0.5d)[3]);
            Assert.Equal(0, Pixel(art, 23.5d, 33.5d)[3]);
            var cloth = Pixel(art, 12.25d, 5.5d);
            Assert.True(cloth[0] > 230 && cloth[1] > 230 && cloth[2] > 230 && cloth[3] == 255, string.Join(",", cloth));
            var badge = Pixel(art, 5.5d, 23.5d);
            Assert.True(badge[0] > badge[1] * 2 && badge[3] == 255, string.Join(",", badge));

            // The heart is drawn light on the dark badge; without a picture the badge is plain there.
            Assert.Equal(new byte[] { 255, 250, 232, 255 }, Pixel(art, RibbonArt.GlyphX + 8.5d, RibbonArt.GlyphY + 5.5d));
            var plain = RibbonArt.Render(new RibbonDef("X", "X", string.Empty, def.Stripes), Scale);
            Assert.True(Pixel(plain, RibbonArt.GlyphX + 8.5d, RibbonArt.GlyphY + 5.5d)[1] < 120);
        }

        [Fact]
        public void A_light_badge_gets_a_dark_picture()
        {
            var art = RibbonArt.Render(new RibbonDef("X", "X", string.Empty, "f2d24b", "heart"), Scale);
            var ink = Pixel(art, RibbonArt.GlyphX + 8.5d, RibbonArt.GlyphY + 5.5d);
            Assert.True(ink[0] < 60 && ink[1] < 60 && ink[2] < 60, string.Join(",", ink));
        }
    }

    public class DataNodeTests
    {
        [Fact]
        public void Values_that_would_break_a_config_file_are_sanitised()
        {
            var node = new DataNode("X");
            node.Add("text", "a {b}\n// c");
            Assert.Equal("a (b) / c", node.Get("text"));
        }

        [Fact]
        public void Numbers_round_trip_exactly()
        {
            var node = new DataNode("X");
            node.Add("value", 0.1d + 0.2d);
            Assert.Equal(0.1d + 0.2d, node.GetDouble("value", 0d));
            Assert.Equal(7d, node.GetDouble("missing", 7d));
        }
    }
}
