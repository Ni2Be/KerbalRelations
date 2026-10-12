using System;
using KerbalRelations.Core;

namespace KerbalRelations
{
    /// <summary>
    /// Entry points for other mods. Everything is static and uses plain types, so a mod can call these
    /// by reflection without referencing this assembly:
    ///
    ///   var api = Type.GetType("KerbalRelations.KerbalRelationsAPI, KerbalRelations");   // null if not installed
    ///   var multiplier = (double)api.GetMethod("GetMultiplier").Invoke(null, new object[] { "Jebediah Kerman", "MyEffect" });
    ///
    /// To give an effect its own curve, add a config node:
    ///
    ///   KR_EFFECT { name = MyEffect  key = 0 0.8  key = 100 1  key = 200 1.3 }
    ///
    /// Mods can also add KR_MODIFIER and KR_EVENT nodes, see GameData/KerbalRelations/Config.
    /// </summary>
    public static class KerbalRelationsAPI
    {
        /// <summary>Raised when a Kerbal's happiness has changed by at least one point: (name, happiness).</summary>
        public static event Action<string, double> OnHappinessChanged;

        /// <summary>True when a save is loaded and the mod is switched on for it.</summary>
        public static bool IsActive
        {
            get
            {
                var scenario = RelationsScenario.Instance;
                return scenario != null && scenario.Active;
            }
        }

        private static KerbalState Find(string kerbalName)
        {
            var scenario = RelationsScenario.Instance;
            if (scenario == null || !scenario.Active || kerbalName == null)
                return null;
            var state = scenario.Data.Get(kerbalName);
            return state != null && state.Stage != LifeStage.Egg ? state : null;
        }

        /// <summary>Happiness in percent, 0-200. Returns 100 (neutral) for unknown Kerbals or when inactive.</summary>
        public static double GetHappiness(string kerbalName)
        {
            var state = Find(kerbalName);
            return state != null ? state.Happiness : 100d;
        }

        /// <summary>
        /// The multiplier a Kerbal's happiness gives to the named effect: 0.5 at 0 %, 1 at 100 %, 2 at
        /// 200 % unless a KR_EFFECT node defines another curve. Returns 1 for unknown Kerbals or when inactive.
        /// </summary>
        public static double GetMultiplier(string kerbalName, string effect)
        {
            var state = Find(kerbalName);
            return state != null ? ConfigLoader.Effects.GetMultiplier(effect, state.Happiness) : 1d;
        }

        /// <summary>The average multiplier of the crew in a part; 1 if it is empty.</summary>
        public static double GetCrewMultiplier(Part part, string effect)
        {
            if (part == null || part.protoModuleCrew == null || part.protoModuleCrew.Count == 0)
                return 1d;
            var sum = 0d;
            for (int i = 0; i < part.protoModuleCrew.Count; ++i)
                sum += GetMultiplier(part.protoModuleCrew[i].name, effect);
            return sum / part.protoModuleCrew.Count;
        }

        /// <summary>
        /// Adds a temporary happiness bonus or penalty (in percentage points). A modifier with the same id
        /// replaces the previous one. Returns false if the Kerbal is not tracked.
        /// </summary>
        public static bool AddModifier(string kerbalName, string id, string text, double amount, double durationSeconds)
        {
            var state = Find(kerbalName);
            if (state == null || string.IsNullOrEmpty(id))
                return false;
            RelationsScenario.Instance.Sim.AddModifier(state, id, text ?? id, amount, durationSeconds, RelationsScenario.Now, false);
            return true;
        }

        /// <summary>Relationship strength in percent, or -1 if the two have never met.</summary>
        public static double GetRelationship(string kerbalA, string kerbalB)
        {
            var scenario = RelationsScenario.Instance;
            if (scenario == null)
                return -1d;
            var rel = scenario.Data.GetRelationship(kerbalA, kerbalB);
            return rel != null ? rel.Strength : -1d;
        }

        /// <summary>The partner's name, or null.</summary>
        public static string GetPartner(string kerbalName)
        {
            var scenario = RelationsScenario.Instance;
            var state = scenario != null ? scenario.Data.Get(kerbalName) : null;
            return state != null ? state.Partner : null;
        }

        /// <summary>"Adult", "Egg" or "Child".</summary>
        public static string GetLifeStage(string kerbalName)
        {
            var scenario = RelationsScenario.Instance;
            var state = scenario != null ? scenario.Data.Get(kerbalName) : null;
            return (state != null ? state.Stage : LifeStage.Adult).ToString();
        }

        /// <summary>Age in years, or -1 for eggs and unknown Kerbals.</summary>
        public static double GetAge(string kerbalName)
        {
            var scenario = RelationsScenario.Instance;
            var state = scenario != null ? scenario.Data.Get(kerbalName) : null;
            return state != null ? scenario.Sim.AgeYears(state, RelationsScenario.Now) : -1d;
        }

        /// <summary>The body a Kerbal was born on or around, or null if unknown.</summary>
        public static string GetBirthBody(string kerbalName)
        {
            var scenario = RelationsScenario.Instance;
            var state = scenario != null ? scenario.Data.Get(kerbalName) : null;
            return state != null ? state.BirthBody : null;
        }

        /// <summary>True if the Kerbal has earned the ribbon with this id.</summary>
        public static bool HasRibbon(string kerbalName, string ribbonId)
        {
            var scenario = RelationsScenario.Instance;
            var state = scenario != null ? scenario.Data.Get(kerbalName) : null;
            return state != null && ribbonId != null && state.HasRibbon(ribbonId);
        }

        /// <summary>
        /// Gives a Kerbal a ribbon, with a message and a small mood boost. Define the ribbon in a
        /// KR_RIBBON config node. Returns false if the Kerbal already has it, is an egg or is not tracked.
        /// </summary>
        public static bool AwardRibbon(string kerbalName, string ribbonId)
        {
            var state = Find(kerbalName);
            if (state == null || string.IsNullOrEmpty(ribbonId))
                return false;
            return RelationsScenario.Instance.Sim.Award(state, ribbonId, RelationsScenario.Now);
        }

        internal static void RaiseHappinessChanges(RelationsData data)
        {
            var handler = OnHappinessChanged;
            foreach (var state in data.Kerbals.Values)
            {
                if (state.Stage == LifeStage.Egg)
                    continue;
                if (!double.IsNaN(state.NotifiedHappiness) && Math.Abs(state.Happiness - state.NotifiedHappiness) < 1d)
                    continue;
                state.NotifiedHappiness = state.Happiness;
                if (handler == null)
                    continue;
                try
                {
                    handler(state.Name, state.Happiness);
                }
                catch (Exception ex)
                {
                    Log.Error("OnHappinessChanged listener failed", ex);
                }
            }
        }
    }
}
