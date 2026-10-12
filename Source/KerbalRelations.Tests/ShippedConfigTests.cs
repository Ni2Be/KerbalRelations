using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KerbalRelations.Core;
using Xunit;

namespace KerbalRelations.Tests
{
    /// <summary>
    /// Checks the .cfg files that ship with the mod. A typo there would otherwise only show up as a
    /// warning in KSP.log.
    /// </summary>
    public class ShippedConfigTests
    {
        private static string ConfigDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "GameData", "KerbalRelations", "Config")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, "GameData", "KerbalRelations", "Config");
        }

        /// <summary>Just enough of the KSP config format: comments, "key = value" and one level of named blocks.</summary>
        private static List<(string Name, List<KeyValuePair<string, string>> Values)> Parse(string file)
        {
            var nodes = new List<(string, List<KeyValuePair<string, string>>)>();
            List<KeyValuePair<string, string>> current = null;
            string pendingName = null;
            foreach (var raw in File.ReadAllLines(Path.Combine(ConfigDirectory(), file)))
            {
                var comment = raw.IndexOf("//", StringComparison.Ordinal);
                var line = (comment >= 0 ? raw.Substring(0, comment) : raw).Trim();
                if (line.Length == 0)
                    continue;
                if (line == "{")
                {
                    Assert.NotNull(pendingName);
                    Assert.Null(current);
                    current = new List<KeyValuePair<string, string>>();
                    nodes.Add((pendingName, current));
                    pendingName = null;
                }
                else if (line == "}")
                {
                    Assert.NotNull(current);
                    current = null;
                }
                else if (current == null)
                {
                    pendingName = line;
                }
                else
                {
                    var eq = line.IndexOf('=');
                    Assert.True(eq > 0, file + ": not a 'key = value' line: " + line);
                    current.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
                }
            }
            Assert.Null(current);
            return nodes;
        }

        private static string Value(List<KeyValuePair<string, string>> values, string key)
        {
            return values.Where(v => v.Key == key).Select(v => v.Value).FirstOrDefault();
        }

        [Fact]
        public void Settings_only_use_known_names_and_match_the_built_in_defaults()
        {
            var visual = new[]
            {
                "ChildScale", "ChildHeadScale", "ChildEyeScale", "ChildSkinHue", "ChildSkinHueFemale", "ChildSkinSaturationFemale", "EggSize",
                "EggBallSize", "YoungOnEva"
            };
            var node = Assert.Single(Parse("Settings.cfg"));
            Assert.Equal("KR_SETTINGS", node.Name);
            var defaults = new SimConfig();
            foreach (var value in node.Values)
            {
                if (visual.Contains(value.Key))
                    continue;
                var field = typeof(SimConfig).GetField(value.Key);
                Assert.True(field != null, "Unknown setting " + value.Key);
                var expected = Convert.ToString(field.GetValue(defaults), CultureInfo.InvariantCulture);
                Assert.Equal(expected.ToLowerInvariant(), value.Value.ToLowerInvariant());
            }
        }

        [Fact]
        public void Events_are_well_formed()
        {
            var events = Parse("Events.cfg");
            Assert.True(events.Count >= 25);
            Assert.All(events, e => Assert.Equal("KR_EVENT", e.Name));

            var ids = events.Select(e => Value(e.Values, "id")).ToList();
            Assert.DoesNotContain(null, ids);
            Assert.Equal(ids.Count, ids.Distinct().Count());

            foreach (var e in events)
            {
                var id = Value(e.Values, "id");
                var text = Value(e.Values, "text");
                Assert.False(string.IsNullOrEmpty(text), id + " has no text");
                Assert.True(double.TryParse(Value(e.Values, "amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out _), id + " has no amount");

                var condition = EventCondition.Any;
                var conditionText = Value(e.Values, "condition");
                if (conditionText != null)
                    Assert.True(Enum.TryParse(conditionText, true, out condition), id + ": unknown condition " + conditionText);
                var stageText = Value(e.Values, "stage");
                if (stageText != null)
                    Assert.True(Enum.TryParse<StageFilter>(stageText, true, out _), id + ": unknown stage " + stageText);

                // Only events that involve somebody else can name them or change the bond.
                var hasOther = condition == EventCondition.Company || condition == EventCondition.Couple || condition == EventCondition.Parent;
                if (!hasOther)
                {
                    Assert.False(text.Contains("<<2>>"), id + " mentions <<2>> but has no other Kerbal");
                    Assert.Null(Value(e.Values, "relationDelta"));
                    Assert.Null(Value(e.Values, "mutual"));
                }
            }
        }

        [Fact]
        public void Modifiers_cover_everything_the_simulation_applies()
        {
            var modifiers = Parse("Modifiers.cfg");
            Assert.All(modifiers, m => Assert.Equal("KR_MODIFIER", m.Name));
            var ids = modifiers.Select(m => Value(m.Values, "id")).ToList();
            Assert.Equal(new Definitions().Modifiers.Keys.OrderBy(k => k), ids.OrderBy(k => k));

            var defaults = new Definitions();
            foreach (var m in modifiers)
            {
                var def = defaults.Get(Value(m.Values, "id"));
                Assert.Equal(def.Text, Value(m.Values, "text"));
                Assert.Equal(def.Amount, double.Parse(Value(m.Values, "amount"), CultureInfo.InvariantCulture));
                Assert.Equal(def.DurationDays, double.Parse(Value(m.Values, "durationDays"), CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public void Ribbons_match_the_built_in_ones()
        {
            var ribbons = Parse("Ribbons.cfg");
            Assert.All(ribbons, r => Assert.Equal("KR_RIBBON", r.Name));
            var builtIn = Ribbons.BuiltIn();
            Assert.Equal(builtIn.Select(r => r.Id), ribbons.Select(r => Value(r.Values, "id")));
            for (int i = 0; i < builtIn.Count; ++i)
            {
                Assert.Equal(builtIn[i].Name, Value(ribbons[i].Values, "name"));
                Assert.Equal(builtIn[i].Description, Value(ribbons[i].Values, "description"));
                Assert.Equal(builtIn[i].Stripes, Value(ribbons[i].Values, "stripes"));
                Assert.Equal(builtIn[i].Icon, Value(ribbons[i].Values, "icon"));
            }
        }

        [Fact]
        public void Effects_define_the_three_built_in_curves()
        {
            var effects = Parse("Effects.cfg");
            Assert.Equal(
                new[] { EffectRegistry.Experience, EffectRegistry.Research, EffectRegistry.RunSpeed },
                effects.Select(e => Value(e.Values, "name")).OrderBy(n => n));
            Assert.All(effects, e => Assert.Equal(3, e.Values.Count(v => v.Key == "key")));
        }
    }
}
