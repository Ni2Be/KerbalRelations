using System;
using System.Collections.Generic;
using KerbalRelations.Core;

namespace KerbalRelations
{
    /// <summary>
    /// Turns the simulation's family events into crew members. Eggs and children are tourist-type
    /// Kerbals: they take a seat and can be transferred, but cannot go on EVA or work, and USI-LS leaves
    /// tourists alone. Their life stage is tracked by this mod, not by a trait, because stock forces
    /// tourists to the Tourist trait and hands any other custom trait to random new hires.
    /// </summary>
    public static class EggManager
    {
        private static readonly Random Random = new Random();

        public static void Process(RelationsScenario scenario, List<SimEvent> events)
        {
            for (int i = 0; i < events.Count; ++i)
            {
                if (events[i].Type == SimEventType.GrewUp)
                    GrowUp(scenario, events[i].A);
            }
            ResolvePendingEggs(scenario);
        }

        /// <summary>
        /// An egg becomes due in the simulation, which also runs for unloaded vessels. It is laid in a
        /// nest of the parents' vessel or base, loaded or not, or dropped if no nest is left.
        /// </summary>
        private static void ResolvePendingEggs(RelationsScenario scenario)
        {
            List<Relationship> pending = null;
            foreach (var rel in scenario.Data.Relationships.Values)
            {
                if (rel.EggPendingSince >= 0d)
                    (pending ?? (pending = new List<Relationship>())).Add(rel);
            }
            if (pending == null)
                return;

            for (int i = 0; i < pending.Count; ++i)
            {
                var rel = pending[i];
                var a = scenario.Data.Get(rel.A);
                var b = scenario.Data.Get(rel.B);
                var groups = scenario.Groups;
                var together = a != null && b != null && rel.IsCouple
                    && a.GroupIndex == b.GroupIndex && a.GroupIndex >= 0 && a.GroupIndex < groups.Count
                    && !groups[a.GroupIndex].AtHome
                    && groups[a.GroupIndex].Members.Contains(a.Name) && groups[a.GroupIndex].Members.Contains(b.Name);

                if (!together || !scenario.ReproductionActive)
                {
                    rel.EggPendingSince = -1d;
                    continue;
                }

                var vessels = (List<Vessel>)groups[a.GroupIndex].Tag;
                var nest = NestFinder.FindLoadedNest(vessels);
                var savedNest = nest == null ? NestFinder.FindSavedNest(vessels) : null;
                if (nest != null || savedNest != null)
                {
                    Lay(scenario, rel, nest, savedNest);
                }
                else
                {
                    Log.Info(string.Format("Egg of {0} and {1} cancelled: no nest left", rel.A, rel.B));
                    rel.EggPendingSince = -1d;
                }
            }
        }

        /// <summary>Exactly one of the two nests is given: a part of a loaded vessel or of a saved one.</summary>
        private static void Lay(RelationsScenario scenario, Relationship rel, Part nest, ProtoPartSnapshot savedNest)
        {
            var roster = HighLogic.CurrentGame.CrewRoster;
            var partTitle = nest != null ? nest.partInfo.title : savedNest.partInfo.title;
            var vesselName = nest != null ? nest.vessel.vesselName : savedNest.pVesselRef.vesselName;
            ProtoCrewMember egg = null;
            var seated = false;
            try
            {
                egg = roster.GetNewKerbal(ProtoCrewMember.KerbalType.Tourist);
                egg.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                if (nest != null)
                {
                    seated = nest.AddCrewmember(egg);
                    if (seated)
                    {
                        if (egg.seat != null)
                            egg.seat.SpawnCrew();
                        Vessel.CrewWasModified(nest.vessel);
                    }
                }
                else
                {
                    // Stock writes the part's crew from these two lists when it saves the vessel. The
                    // cabin gives a Kerbal without a seat index the next free seat when the vessel loads.
                    egg.seatIdx = -1;
                    savedNest.protoModuleCrew.Add(egg);
                    savedNest.protoCrewNames.Add(egg.name);
                    savedNest.pVesselRef.AddCrew(egg);
                    seated = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not lay an egg in " + partTitle + " of " + vesselName, ex);
            }

            if (!seated)
            {
                if (egg != null)
                {
                    if (savedNest != null)
                    {
                        savedNest.protoModuleCrew.Remove(egg);
                        savedNest.protoCrewNames.Remove(egg.name);
                    }
                    if (nest == null || !nest.protoModuleCrew.Contains(egg))
                        roster.Remove(egg);
                }
                rel.EggPendingSince = -1d;
                return;
            }

            var laid = rel.EggPendingSince;
            var sex = egg.gender == ProtoCrewMember.Gender.Female ? Sex.Female : Sex.Male;
            scenario.Sim.RegisterEgg(egg.name, sex, rel.A, rel.B, laid, RelationsScenario.Now);

            var p = RelationsParameters.Current;
            if (p != null && p.Messages)
            {
                ScreenMessages.PostScreenMessage(
                    string.Format("{0} and {1} laid an egg aboard {2}", RelationsSim.ShortName(rel.A), RelationsSim.ShortName(rel.B), vesselName),
                    8f, ScreenMessageStyle.UPPER_CENTER);
            }
            Log.Info(string.Format("{0} and {1} laid egg {2} in {3} of {4}{5}", rel.A, rel.B, egg.name, partTitle, vesselName,
                nest != null ? string.Empty : " (unloaded)"));
        }

        /// <summary>A child that has come of age joins the crew with a profession.</summary>
        private static void GrowUp(RelationsScenario scenario, string name)
        {
            var roster = HighLogic.CurrentGame.CrewRoster;
            var pcm = roster[name];
            var state = scenario.Data.Get(name);
            if (pcm == null || state == null)
                return;

            try
            {
                pcm.type = ProtoCrewMember.KerbalType.Crew;
                KerbalRoster.SetExperienceTrait(pcm, PickProfession(roster, state));
                Log.Info(name + " grew up and became a " + pcm.trait);

                // Refresh portraits and skill registration if the Kerbal is aboard a loaded vessel.
                var vessels = FlightGlobals.VesselsLoaded;
                for (int v = 0; vessels != null && v < vessels.Count; ++v)
                {
                    if (vessels[v].GetVesselCrew().Contains(pcm))
                    {
                        Vessel.CrewWasModified(vessels[v]);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not turn " + name + " into an adult", ex);
            }
        }

        /// <summary>Half the time a child follows one of its parents; otherwise any profession.</summary>
        private static string PickProfession(KerbalRoster roster, KerbalState state)
        {
            var traits = GameDatabase.Instance.ExperienceConfigs.TraitNamesNoTourist;
            if (Random.NextDouble() < 0.5d)
            {
                var parentName = Random.Next(2) == 0 ? state.ParentA : state.ParentB;
                var parent = parentName != null ? roster[parentName] : null;
                if (parent != null && parent.type == ProtoCrewMember.KerbalType.Crew && traits.Contains(parent.trait))
                    return parent.trait;
            }
            return traits[Random.Next(traits.Count)];
        }
    }
}
