using System.Collections.Generic;
using Contracts;
using FinePrint.Contracts.Parameters;
using FinePrint.Utilities;
using KerbalRelations.Core;

namespace KerbalRelations.Missions
{
    /// <summary>
    /// "Show school class 3b Minmus": a class of children, sometimes with the class egg, wants to see
    /// a place and come home again. The children are tourists, handled the way stock handles the
    /// tourists of its own contracts; this mod makes them look and count as children and eggs.
    /// </summary>
    public class KRSchoolTripContract : RelationsContract
    {
        private const float FarePerChild = 14000f;

        private readonly List<string> _class = new List<string>();
        private string _label = "3b";
        private string _egg = string.Empty;
        private bool _landing;

        protected override bool Generate()
        {
            var scenario = Scenario;
            if (scenario == null || !MissionDirector.HasRoom<KRSchoolTripContract>(2))
                return false;
            if (ProgressTracking.Instance == null || !ProgressTracking.Instance.NodeComplete("ReachedSpace"))
                return false;

            // Room for a pilot and at least one child in a single part, to keep the class together.
            var seats = PartsOnHand.BestSeats();
            if (seats < 2)
                return false;

            // Places Kerbals have orbited before, so the trip is known to be possible.
            var bodies = ProgressUtilities.GetBodiesProgress(ProgressType.ORBIT, true, MannedStatus.MANNED,
                b => b != null && b != Planetarium.fetch.Sun
                    && !MissionDirector.Taken(KeyFor(typeof(KRSchoolTripContract), b.bodyName)));
            if (bodies == null || bodies.Count == 0)
                return false;
            var body = bodies[UnityEngine.Random.Range(0, bodies.Count)];

            subject = body.bodyName;
            bodyIndex = body.flightGlobalsIndex;
            prestige = MissionDirector.PrestigeFor(body);
            _landing = !body.isHomeWorld && body.hasSolidSurface
                && ProgressUtilities.GetBodyProgress(ProgressType.LANDING, body, MannedStatus.MANNED)
                && UnityEngine.Random.value < 0.4f;
            _label = UnityEngine.Random.Range(1, 7) + "abc".Substring(UnityEngine.Random.Range(0, 3), 1);

            var children = prestige == ContractPrestige.Trivial ? 2 : prestige == ContractPrestige.Significant ? 3 : 4;
            var withEgg = UnityEngine.Random.value < 0.35f;
            var destination = _landing ? FlightLog.EntryType.Land : FlightLog.EntryType.Orbit;
            var roster = HighLogic.CurrentGame.CrewRoster;
            var now = RelationsScenario.Now;

            for (int i = 0; i < children + (withEgg ? 1 : 0); ++i)
            {
                // The same steps stock takes for the tourists of its contracts.
                var pcm = roster.GetNewKerbal(ProtoCrewMember.KerbalType.Unowned);
                pcm.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                pcm.trait = KerbalRoster.touristTrait;
                pcm.seat = null;
                pcm.seatIdx = -1;
                _class.Add(pcm.name);

                var isEgg = i >= children;
                if (isEgg)
                    _egg = pcm.name;
                var sex = pcm.gender == ProtoCrewMember.Gender.Female ? Sex.Female : Sex.Male;
                scenario.Sim.RegisterVisitor(pcm.name, sex, isEgg ? LifeStage.Egg : LifeStage.Child, UnityEngine.Random.Range(6f, 12f), now);

                var tour = AddParameter(new KerbalTourParameter(pcm.name, pcm.gender));
                tour.AddParameter(new KerbalDestinationParameter(body, destination, pcm.name));
                tour.SetFunds(FarePerChild, body);
            }

            SetExpiry();
            SetDeadlineYears(body.isHomeWorld ? 1f : 3f, body);
            SetFunds(4000f, 6000f, 4000f, body);
            SetReputation(10f, 12f, body);
            return true;
        }

        private static ProtoCrewMember Find(string name)
        {
            var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            return roster != null && roster.Exists(name) ? roster[name] : null;
        }

        protected override void OnAccepted()
        {
            for (int i = 0; i < _class.Count; ++i)
            {
                var pcm = Find(_class[i]);
                if (pcm == null)
                    continue;
                pcm.type = ProtoCrewMember.KerbalType.Tourist;
                KerbalRoster.SetExperienceTrait(pcm, KerbalRoster.touristTrait);
                pcm.rosterStatus = ProtoCrewMember.RosterStatus.Available;
            }
        }

        protected override void OnCompleted()
        {
            // Whoever got to know the children on the trip was their chaperone.
            var scenario = Scenario;
            if (scenario == null)
                return;
            var now = RelationsScenario.Now;
            for (int i = 0; i < _class.Count; ++i)
            {
                var rels = scenario.Data.RelationshipsOf(_class[i]);
                for (int r = 0; r < rels.Count; ++r)
                {
                    var adult = scenario.Data.Get(rels[r].Other(_class[i]));
                    if (adult == null || adult.Visitor)
                        continue;
                    if (scenario.Sim.Award(adult, RibbonIds.Chaperone, now))
                        Cheer(scenario, adult.Name);
                }
            }
        }

        /// <summary>The trip is over: children still on a vessel leave when it is recovered, the rest go now.</summary>
        private void SendHome()
        {
            for (int i = 0; i < _class.Count; ++i)
            {
                var pcm = Find(_class[i]);
                if (pcm == null)
                    continue;
                if (pcm.type == ProtoCrewMember.KerbalType.Tourist && pcm.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                    pcm.hasToured = true;
                else
                    HighLogic.CurrentGame.CrewRoster.Remove(pcm.name);
            }
        }

        /// <summary>The trip never started: the children were only ever on paper.</summary>
        private void Dismiss()
        {
            for (int i = 0; i < _class.Count; ++i)
            {
                var pcm = Find(_class[i]);
                if (pcm != null)
                    HighLogic.CurrentGame.CrewRoster.Remove(pcm.name);
                // Removal is normally noticed through a game event; make sure in any case.
                if (RelationsScenario.Instance != null)
                    RelationsScenario.Instance.Data.Remove(_class[i]);
            }
        }

        protected override void OnFinished()
        {
            SendHome();
        }

        protected override void OnGenerateFailed()
        {
            Dismiss();
        }

        protected override void OnWithdrawn()
        {
            Dismiss();
        }

        protected override void OnDeclined()
        {
            base.OnDeclined();
            Dismiss();
        }

        protected override void OnOfferExpired()
        {
            Dismiss();
        }

        protected override string GetTitle()
        {
            return string.Format("Show school class {0} {1}", _label, MissionDirector.Name(Body));
        }

        protected override string GetSynopsys()
        {
            return string.Format("Take the {0} children of class {1} {2} and bring them all home again.",
                _class.Count - (_egg.Length > 0 ? 1 : 0), _label,
                _landing ? "to the surface of " + MissionDirector.Name(Body) : "into orbit around " + MissionDirector.Name(Body));
        }

        protected override string GetDescription()
        {
            return string.Format("Class {0} has been learning about {1} all term and the teacher has run out of posters. A field trip "
                + "is the obvious next step. The children are small, curious and will press any button within reach, so somebody "
                + "responsible should come along. They board like tourists.", _label, MissionDirector.Name(Body));
        }

        protected override string GetNotes()
        {
            return _egg.Length > 0
                ? RelationsSim.ShortName(_egg) + " is the class egg and comes along in a seat of its own. Please do not scramble it."
                : null;
        }

        protected override string MessageCompleted()
        {
            return string.Format("Class {0} is back from {1} and has not stopped talking about it.", _label, MissionDirector.Name(Body));
        }

        protected override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.AddValue("label", _label);
            node.AddValue("egg", _egg);
            node.AddValue("landing", _landing);
            for (int i = 0; i < _class.Count; ++i)
                node.AddValue("child", _class[i]);
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            _label = node.GetValue("label") ?? _label;
            _egg = node.GetValue("egg") ?? string.Empty;
            bool landing;
            _landing = bool.TryParse(node.GetValue("landing"), out landing) && landing;
            _class.Clear();
            _class.AddRange(node.GetValues("child"));
        }
    }
}
