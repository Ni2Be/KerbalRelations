using System;
using System.Globalization;
using System.Reflection;
using KerbalRelations.Core;

namespace KerbalRelations
{
    /// <summary>
    /// Reads KR_SETTINGS, KR_EFFECT, KR_MODIFIER, KR_EVENT and KR_RIBBON nodes from the game database. Any mod can
    /// add or patch these nodes; later nodes with the same name/id replace earlier ones.
    /// </summary>
    public static class ConfigLoader
    {
        public static SimConfig Config { get; private set; }
        public static Definitions Defs { get; private set; }
        public static EffectRegistry Effects { get; private set; }

        // Visual tuning, kept out of the simulation config.
        public static float ChildScale = 0.47f;
        public static float ChildHeadScale = 0.8f;
        public static float ChildEyeScale = 1.64f;
        public static float EggSize = 0.75f;
        public static float ChildSkinHue = 178f;
        public static float ChildSkinHueFemale = 335f;
        public static float ChildSkinSaturationFemale = 0.7f;
        public static float EggBallSize = 0.45f;
        /// <summary>Eggs and children of the space programme may leave a vessel.</summary>
        public static bool YoungOnEva = true;

        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (!_loaded)
                Load();
        }

        public static void Load()
        {
            Config = new SimConfig();
            Defs = new Definitions();
            Effects = new EffectRegistry();
            _loaded = true;

            try
            {
                foreach (var node in GameDatabase.Instance.GetConfigNodes("KR_SETTINGS"))
                    ReadSettings(node);
                foreach (var node in GameDatabase.Instance.GetConfigNodes("KR_EFFECT"))
                    ReadEffect(node);
                foreach (var node in GameDatabase.Instance.GetConfigNodes("KR_MODIFIER"))
                    ReadModifier(node);
                foreach (var node in GameDatabase.Instance.GetConfigNodes("KR_EVENT"))
                    ReadEvent(node);
                foreach (var node in GameDatabase.Instance.GetConfigNodes("KR_RIBBON"))
                    ReadRibbon(node);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to read configuration", ex);
            }

            Log.Info(string.Format("Loaded {0} modifiers, {1} random events and {2} ribbons", Defs.Modifiers.Count, Defs.Events.Count, Defs.Ribbons.Count));
        }

        private static void ReadSettings(ConfigNode node)
        {
            for (int i = 0; i < node.values.Count; ++i)
            {
                var value = node.values[i];
                float f;
                switch (value.name)
                {
                    case "ChildScale":
                        if (TryFloat(value.value, out f)) ChildScale = f;
                        continue;
                    case "ChildHeadScale":
                        if (TryFloat(value.value, out f)) ChildHeadScale = f;
                        continue;
                    case "ChildEyeScale":
                        if (TryFloat(value.value, out f)) ChildEyeScale = f;
                        continue;
                    case "EggSize":
                        if (TryFloat(value.value, out f)) EggSize = f;
                        continue;
                    case "ChildSkinHue":
                        if (TryFloat(value.value, out f)) ChildSkinHue = f;
                        continue;
                    case "ChildSkinHueFemale":
                        if (TryFloat(value.value, out f)) ChildSkinHueFemale = f;
                        continue;
                    case "ChildSkinSaturationFemale":
                        if (TryFloat(value.value, out f)) ChildSkinSaturationFemale = f;
                        continue;
                    case "EggBallSize":
                        if (TryFloat(value.value, out f)) EggBallSize = f;
                        continue;
                    case "YoungOnEva":
                    {
                        bool on;
                        if (bool.TryParse(value.value, out on)) YoungOnEva = on;
                        continue;
                    }
                }

                var field = typeof(SimConfig).GetField(value.name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field == null)
                {
                    Log.Warn("Unknown setting '" + value.name + "' in KR_SETTINGS");
                    continue;
                }
                double d;
                int n;
                bool b;
                if (field.FieldType == typeof(double) && TryDouble(value.value, out d))
                    field.SetValue(Config, d);
                else if (field.FieldType == typeof(int) && int.TryParse(value.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    field.SetValue(Config, n);
                else if (field.FieldType == typeof(bool) && bool.TryParse(value.value, out b))
                    field.SetValue(Config, b);
                else
                    Log.Warn("Cannot read setting '" + value.name + " = " + value.value + "'");
            }
        }

        private static void ReadEffect(ConfigNode node)
        {
            var name = node.GetValue("name");
            if (string.IsNullOrEmpty(name))
                return;
            var curve = new Curve();
            foreach (var key in node.GetValues("key"))
            {
                var parts = key.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                double x, y;
                if (parts.Length >= 2 && TryDouble(parts[0], out x) && TryDouble(parts[1], out y))
                    curve.Add(x, y);
            }
            if (curve.Count > 0)
                Effects.Set(name, curve);
        }

        private static void ReadModifier(ConfigNode node)
        {
            var id = node.GetValue("id");
            if (string.IsNullOrEmpty(id))
                return;
            var def = Defs.Get(id) ?? new ModifierDef { Id = id, Text = id, DurationDays = 10d };
            if (node.HasValue("text"))
                def.Text = node.GetValue("text");
            def.Amount = GetDouble(node, "amount", def.Amount);
            def.DurationDays = GetDouble(node, "durationDays", def.DurationDays);
            def.NoFade = GetBool(node, "noFade", def.NoFade);
            Defs.Set(def);
        }

        private static void ReadEvent(ConfigNode node)
        {
            var id = node.GetValue("id");
            var text = node.GetValue("text");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text))
                return;
            var def = new EventDef { Id = id, Text = text };
            def.Amount = GetDouble(node, "amount", 0d);
            def.DurationDays = GetDouble(node, "durationDays", def.DurationDays);
            def.RelationDelta = GetDouble(node, "relationDelta", 0d);
            def.Weight = GetDouble(node, "weight", 1d);
            def.Mutual = GetBool(node, "mutual", false);
            def.Condition = GetEnum(node, "condition", EventCondition.Any);
            def.Stage = GetEnum(node, "stage", StageFilter.Any);

            Defs.Events.RemoveAll(e => e.Id == id);
            Defs.Events.Add(def);
        }

        /// <summary>Changes the name, text or look of a ribbon, or adds one for another mod to hand out.</summary>
        private static void ReadRibbon(ConfigNode node)
        {
            var id = node.GetValue("id");
            if (string.IsNullOrEmpty(id))
                return;
            RibbonDef existing;
            Defs.Ribbons.TryGetValue(id, out existing);
            var def = new RibbonDef
            {
                Id = id,
                Name = node.GetValue("name") ?? (existing != null ? existing.Name : id),
                Description = node.GetValue("description") ?? (existing != null ? existing.Description : string.Empty),
                Stripes = node.GetValue("stripes") ?? (existing != null ? existing.Stripes : "777777"),
                Icon = node.GetValue("icon") ?? (existing != null ? existing.Icon : null)
            };
            if (Ribbons.ParseStripes(def.Stripes) == null)
            {
                Log.Warn("Cannot read the stripes of ribbon '" + id + "': " + def.Stripes);
                def.Stripes = existing != null ? existing.Stripes : "777777";
            }
            // A name with a slash is the path of a texture, which is only looked up when it is shown.
            if (!string.IsNullOrEmpty(def.Icon) && RibbonArt.Glyph(def.Icon) == null && def.Icon.IndexOf('/') < 0)
                Log.Warn("Unknown icon '" + def.Icon + "' on ribbon '" + id + "'");
            Defs.Set(def);
        }

        private static bool TryDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static double GetDouble(ConfigNode node, string name, double fallback)
        {
            double value;
            return node.HasValue(name) && TryDouble(node.GetValue(name), out value) ? value : fallback;
        }

        private static bool GetBool(ConfigNode node, string name, bool fallback)
        {
            bool value;
            return node.HasValue(name) && bool.TryParse(node.GetValue(name), out value) ? value : fallback;
        }

        private static T GetEnum<T>(ConfigNode node, string name, T fallback) where T : struct
        {
            T value;
            if (node.HasValue(name) && Enum.TryParse(node.GetValue(name), true, out value))
                return value;
            if (node.HasValue(name))
                Log.Warn("Unknown " + name + " '" + node.GetValue(name) + "' in " + node.name);
            return fallback;
        }
    }
}
