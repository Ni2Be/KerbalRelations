using System.Collections.Generic;
using KerbalRelations.Core;
using KerbalRelations.Effects;
using KSP.Localization;

namespace KerbalRelations
{
    /// <summary>
    /// Works out who is together: the crew of a vessel, merged with nearby vessels (a base made of
    /// several landed craft, a Kerbal on EVA next to the ship), plus one group for everyone at the KSC.
    /// Works for unloaded vessels too.
    /// </summary>
    public static class GroupBuilder
    {
        public static List<SimGroup> Build(RelationsScenario scenario)
        {
            var groups = new List<SimGroup>();
            var data = scenario.Data;
            var baseHappiness = scenario.Config.BaseHappiness;

            // GetVesselCrew reads the saved crew of unloaded vessels; Vessel.GetCrewCount does not.
            var crewed = new List<Vessel>();
            var crews = new List<List<ProtoCrewMember>>();
            var vessels = FlightGlobals.Vessels;
            if (vessels != null)
            {
                for (int i = 0; i < vessels.Count; ++i)
                {
                    var v = vessels[i];
                    if (v == null || (!v.loaded && v.protoVessel == null))
                        continue;
                    var vesselCrew = v.GetVesselCrew();
                    if (vesselCrew == null || vesselCrew.Count == 0)
                        continue;
                    crewed.Add(v);
                    crews.Add(vesselCrew);
                }
            }

            // Union-find over vessels that are close to each other.
            var parent = new int[crewed.Count];
            for (int i = 0; i < parent.Length; ++i)
                parent[i] = i;
            for (int i = 0; i < crewed.Count; ++i)
            {
                for (int j = i + 1; j < crewed.Count; ++j)
                {
                    if (Distance(crewed[i], crewed[j]) < scenario.Config.TogetherRange)
                        parent[Find(parent, i)] = Find(parent, j);
                }
            }

            var byRoot = new Dictionary<int, SimGroup>();
            var seen = new HashSet<string>();
            for (int i = 0; i < crewed.Count; ++i)
            {
                var root = Find(parent, i);
                SimGroup group;
                if (!byRoot.TryGetValue(root, out group))
                {
                    group = new SimGroup { Tag = new List<Vessel>() };
                    byRoot.Add(root, group);
                    groups.Add(group);
                }
                var place = ((List<Vessel>)group.Tag).Count;
                ((List<Vessel>)group.Tag).Add(crewed[i]);

                var crew = crews[i];
                for (int c = 0; c < crew.Count; ++c)
                {
                    if (!seen.Add(crew[c].name))
                        continue;
                    Sync(data, crew[c], baseHappiness, crewed[i]);
                    group.Members.Add(crew[c].name);
                    group.Places.Add(place);
                }
            }

            for (int i = 0; i < groups.Count; ++i)
            {
                var together = (List<Vessel>)groups[i].Tag;
                groups[i].HasNest = NestFinder.AnyNest(together);

                // Bonds between vessels cannot grow as far, the less so the further they are apart.
                if (together.Count < 2)
                    continue;
                var distances = new double[together.Count, together.Count];
                for (int a = 0; a < together.Count; ++a)
                {
                    for (int b = a + 1; b < together.Count; ++b)
                        distances[a, b] = distances[b, a] = Distance(together[a], together[b]);
                }
                groups[i].PlaceDistances = distances;
            }

            // Everyone waiting at the KSC.
            var home = new SimGroup { AtHome = true, Tag = new List<Vessel>() };
            var roster = HighLogic.CurrentGame.CrewRoster;
            for (int i = 0; i < roster.Count; ++i)
            {
                var pcm = roster[i];
                if (pcm == null || pcm.rosterStatus != ProtoCrewMember.RosterStatus.Available)
                    continue;
                if (pcm.type != ProtoCrewMember.KerbalType.Crew && data.Get(pcm.name) == null)
                    continue;
                if (!seen.Add(pcm.name))
                    continue;
                Sync(data, pcm, baseHappiness, null);
                home.Members.Add(pcm.name);
            }
            if (home.Members.Count > 0)
                groups.Add(home);

            return groups;
        }

        /// <param name="vessel">The vessel the Kerbal is aboard, or null at the KSC.</param>
        private static void Sync(RelationsData data, ProtoCrewMember pcm, double baseHappiness, Vessel vessel)
        {
            var state = data.GetOrCreate(pcm.name, baseHappiness);
            state.Sex = pcm.gender == ProtoCrewMember.Gender.Female ? Sex.Female : Sex.Male;
            // Tourists (and crew USI-LS has turned into tourists) take part in social life but do not pair up.
            state.Eligible = pcm.type == ProtoCrewMember.KerbalType.Crew && state.Stage == LifeStage.Adult;
            state.Level = pcm.experienceLevel;
            EvaAccess.Sync(pcm, state, vessel);

            // An egg is born where it hatches, so its whereabouts are kept up to date until then.
            if (state.Stage == LifeStage.Egg && !state.Visitor)
            {
                var body = vessel != null ? vessel.mainBody : FlightGlobals.GetHomeBody();
                if (body != null)
                {
                    state.BirthBody = body.bodyName;
                    state.BirthPlace = Place(vessel, body);
                }
            }
        }

        /// <summary>"on Minmus, aboard Hab One", "in orbit around Duna, aboard Duna Station".</summary>
        private static string Place(Vessel vessel, CelestialBody body)
        {
            var name = Localizer.Format("<<1>>", body.displayName);
            if (vessel == null)
                return "on " + name;
            return (IsOnSurface(vessel) ? "on " : "in orbit around ") + name + ", aboard " + vessel.vesselName;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static bool IsOnSurface(Vessel v)
        {
            return v.situation == Vessel.Situations.LANDED
                || v.situation == Vessel.Situations.SPLASHED
                || v.situation == Vessel.Situations.PRELAUNCH;
        }

        /// <summary>
        /// Metres between two vessels; infinity when that cannot be told, which is the case for a vessel
        /// in flight that is not loaded.
        /// </summary>
        private static double Distance(Vessel a, Vessel b)
        {
            if (a.mainBody == null || a.mainBody != b.mainBody)
                return double.PositiveInfinity;

            if (IsOnSurface(a) && IsOnSurface(b))
            {
                // Latitude/longitude are valid for unloaded vessels as well.
                var pa = a.mainBody.GetWorldSurfacePosition(a.latitude, a.longitude, a.altitude);
                var pb = b.mainBody.GetWorldSurfacePosition(b.latitude, b.longitude, b.altitude);
                return (pa - pb).magnitude;
            }
            if (a.loaded && b.loaded)
                return (a.GetWorldPos3D() - b.GetWorldPos3D()).magnitude;
            return double.PositiveInfinity;
        }
    }
}
