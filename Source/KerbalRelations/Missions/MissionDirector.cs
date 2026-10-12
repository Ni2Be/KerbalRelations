using System;
using System.Collections.Generic;
using Contracts;
using KerbalRelations.Core;
using KerbalRelations.Effects;
using KSP.Localization;

namespace KerbalRelations.Missions
{
    /// <summary>
    /// Offers the mod's missions in Mission Control when their moment has come: a Kerbal has become
    /// lonely, a couple has been separated, and so on. Stock also picks contract types at random, which
    /// would leave a lonely Kerbal waiting for a long time; both ways go through the same limits.
    /// </summary>
    public static class MissionDirector
    {
        private const int MaxOffered = 3;
        private const int CheckEveryTicks = 30;
        private const double DeclinedForDays = 40d;

        private static readonly Type[] Types =
        {
            typeof(KRCompanyContract),
            typeof(KRReunionContract),
            typeof(KRHomecomingContract),
            typeof(KRHappyCrewContract),
            typeof(KRFirstChildContract),
            typeof(KRFamilyContract),
            typeof(KRSchoolTripContract)
        };

        /// <summary>Missions the player turned down, by subject, and when (UT).</summary>
        private static readonly Dictionary<string, double> Declined = new Dictionary<string, double>();
        private static int _ticks;

        /// <summary>Missions are switched on for this save; they need a career game.</summary>
        public static bool Enabled
        {
            get
            {
                var game = HighLogic.CurrentGame;
                var p = RelationsParameters.Current;
                return game != null && game.Mode == Game.Modes.CAREER && p != null && p.ModEnabled && p.Missions;
            }
        }

        public static void Update(RelationsScenario scenario)
        {
            if (++_ticks % CheckEveryTicks != 0)
                return;
            if (!Enabled || ContractSystem.Instance == null || !ContractSystem.loaded || !scenario.Ready)
                return;
            if (Count<RelationsContract>(Contract.State.Offered) < MaxOffered)
                Offer();
        }

        /// <summary>For the debug panel: offers a mission now and says what happened.</summary>
        public static string OfferNow(RelationsScenario scenario)
        {
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
                return "Missions need a career game";
            if (!Enabled)
                return "Missions are switched off in the difficulty settings";
            if (ContractSystem.Instance == null || !ContractSystem.loaded || !scenario.Ready)
                return "Mission Control is not ready yet";
            var contract = Offer();
            return contract != null ? "Offered in Mission Control: " + contract.Title : "No mission fits the situation right now";
        }

        /// <summary>Offers the first kind of mission, starting from a random one, that fits the situation.</summary>
        private static Contract Offer()
        {
            // Generating a contract reseeds Unity's random numbers; put them back afterwards.
            var random = UnityEngine.Random.state;
            try
            {
                var start = UnityEngine.Random.Range(0, Types.Length);
                var seed = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
                for (int i = 0; i < Types.Length; ++i)
                {
                    var type = Types[(start + i) % Types.Length];
                    var contract = Contract.Generate(type, Contract.ContractPrestige.Trivial, unchecked(seed + i), Contract.State.Generated);
                    if (contract == null)
                        continue;
                    contract.Offer();
                    ContractSystem.Instance.Contracts.Add(contract);
                    GameEvents.Contract.onContractsListChanged.Fire();
                    Log.Info("Offered mission: " + contract.Title);
                    return contract;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Offering a mission failed", ex);
            }
            finally
            {
                UnityEngine.Random.state = random;
            }
            return null;
        }

        /// <summary>How many of the mod's missions of this kind are in the given state.</summary>
        public static int Count<T>(Contract.State state) where T : RelationsContract
        {
            var count = 0;
            var contracts = ContractSystem.Instance != null ? ContractSystem.Instance.Contracts : null;
            for (int i = 0; contracts != null && i < contracts.Count; ++i)
            {
                if (contracts[i] is T && contracts[i].ContractState == state)
                    ++count;
            }
            return count;
        }

        /// <summary>True while a mission of this kind may be generated: one on offer, a few running.</summary>
        public static bool HasRoom<T>(int maxActive) where T : RelationsContract
        {
            return Count<T>(Contract.State.Offered) < 1 && Count<T>(Contract.State.Active) < maxActive;
        }

        /// <summary>True if a mission on offer or under way is already about this subject.</summary>
        public static bool Taken(string key)
        {
            var contracts = ContractSystem.Instance != null ? ContractSystem.Instance.Contracts : null;
            for (int i = 0; contracts != null && i < contracts.Count; ++i)
            {
                var ours = contracts[i] as RelationsContract;
                if (ours == null || ours.Key != key)
                    continue;
                if (ours.ContractState == Contract.State.Offered || ours.ContractState == Contract.State.Active)
                    return true;
            }

            double when;
            return Declined.TryGetValue(key, out when)
                && RelationsScenario.Now >= when
                && RelationsScenario.Now - when < ConfigLoader.Config.Days(DeclinedForDays);
        }

        /// <summary>Remembers that a mission was turned down, so it is not offered again right away.</summary>
        public static void NoteDeclined(string key)
        {
            Declined[key] = RelationsScenario.Now;
        }

        /// <summary>The body a Kerbal is at, or the home world.</summary>
        public static CelestialBody BodyOf(RelationsScenario scenario, KerbalState k)
        {
            var groups = scenario.Groups;
            if (k != null && k.GroupIndex >= 0 && k.GroupIndex < groups.Count)
            {
                var vessels = groups[k.GroupIndex].Tag as List<Vessel>;
                if (vessels != null && vessels.Count > 0 && vessels[0] != null && vessels[0].mainBody != null)
                    return vessels[0].mainBody;
            }
            return FlightGlobals.GetHomeBody();
        }

        /// <summary>"the Mun", "Minmus".</summary>
        public static string Name(CelestialBody body)
        {
            return body != null ? Localizer.Format("<<1>>", body.displayName) : "space";
        }

        /// <summary>Missions near home are small ones; other planets are a big deal.</summary>
        public static Contract.ContractPrestige PrestigeFor(CelestialBody body)
        {
            if (body == null || body.isHomeWorld)
                return Contract.ContractPrestige.Trivial;
            return body.referenceBody != null && body.referenceBody.isHomeWorld
                ? Contract.ContractPrestige.Significant
                : Contract.ContractPrestige.Exceptional;
        }

        /// <summary>A member of the active crew, as opposed to a tourist or an applicant.</summary>
        public static bool IsCrew(string name)
        {
            var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            var pcm = roster != null && roster.Exists(name) ? roster[name] : null;
            return pcm != null && pcm.type == ProtoCrewMember.KerbalType.Crew
                && pcm.rosterStatus != ProtoCrewMember.RosterStatus.Dead
                && pcm.rosterStatus != ProtoCrewMember.RosterStatus.Missing;
        }
    }

    /// <summary>What the player can build with, so missions only ask for what is possible.</summary>
    public static class PartsOnHand
    {
        private const string GrowthModule = "USILS_KolonyGrowthModule";

        private static bool Usable(AvailablePart part)
        {
            return part != null && part.partPrefab != null
                && !part.name.StartsWith("kerbalEVA", StringComparison.Ordinal)
                && ResearchAndDevelopment.PartTechAvailable(part)
                && ResearchAndDevelopment.PartModelPurchased(part);
        }

        /// <summary>Seats in the roomiest crewed part that is researched.</summary>
        public static int BestSeats()
        {
            var best = 0;
            var parts = PartLoader.LoadedPartsList;
            for (int i = 0; parts != null && i < parts.Count; ++i)
            {
                if (Usable(parts[i]) && parts[i].partPrefab.CrewCapacity > best)
                    best = parts[i].partPrefab.CrewCapacity;
            }
            return best;
        }

        /// <summary>
        /// A part an egg can be laid in is researched: with USI-LS one with the Kolony Growth toggle,
        /// otherwise one with room for two parents and an egg.
        /// </summary>
        public static bool Nest()
        {
            if (!HarmonyBootstrap.GrowthModulePatched)
                return BestSeats() >= 3;
            var parts = PartLoader.LoadedPartsList;
            for (int i = 0; parts != null && i < parts.Count; ++i)
            {
                if (Usable(parts[i]) && parts[i].partPrefab.Modules.Contains(GrowthModule))
                    return true;
            }
            return false;
        }
    }
}
