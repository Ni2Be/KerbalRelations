using System;
using System.Collections.Generic;
using KerbalRelations.Core;
using KerbalRelations.Effects;
using KerbalRelations.Missions;
using UnityEngine;

namespace KerbalRelations
{
    /// <summary>Owns the simulation state of a save, persists it and advances it with game time.</summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class RelationsScenario : ScenarioModule
    {
        private const float TickInterval = 1f;
        private const int MaxMessagesPerTick = 3;

        public static RelationsScenario Instance { get; private set; }

        public RelationsData Data { get; private set; }
        public RelationsSim Sim { get; private set; }
        /// <summary>The groups of the most recent tick.</summary>
        public List<SimGroup> Groups = new List<SimGroup>();

        private float _nextTick;
        private int _tickCount;

        /// <summary>The mod is switched on for this save.</summary>
        public bool Active
        {
            get
            {
                var p = RelationsParameters.Current;
                return p != null && p.ModEnabled;
            }
        }

        /// <summary>The simulation has run in this scene, so everyone's whereabouts are known.</summary>
        public bool Ready
        {
            get { return _tickCount > 0; }
        }

        public bool ReproductionActive
        {
            get
            {
                var p = RelationsParameters.Current;
                return p != null && p.ModEnabled && p.Reproduction;
            }
        }

        public SimConfig Config
        {
            get { return ConfigLoader.Config; }
        }

        public static double Now
        {
            get { return Planetarium.GetUniversalTime(); }
        }

        public override void OnAwake()
        {
            base.OnAwake();
            ConfigLoader.EnsureLoaded();
            Instance = this;
            Data = new RelationsData();
            Sim = new RelationsSim(Data, ConfigLoader.Config, ConfigLoader.Defs, new SystemRandom());
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            try
            {
                Data.Load(node.HasNode(RelationsData.NodeName) ? ToDataNode(node.GetNode(RelationsData.NodeName)) : null);
                Log.Info(string.Format("Loaded {0} Kerbals and {1} relationships", Data.Kerbals.Count, Data.Relationships.Count));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load saved state", ex);
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            try
            {
                node.AddNode(ToConfigNode(Data.Save()));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to save state", ex);
            }
        }

        public void Start()
        {
            GameEvents.onVesselSOIChanged.Add(OnSoiChanged);
            GameEvents.onVesselSituationChange.Add(OnSituationChanged);
            GameEvents.onKerbalLevelUp.Add(OnLevelUp);
            GameEvents.onKerbalStatusChange.Add(OnStatusChange);
            GameEvents.onKerbalNameChanged.Add(OnNameChanged);
            GameEvents.onKerbalRemoved.Add(OnKerbalRemoved);
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);
        }

        public void OnDestroy()
        {
            GameEvents.onVesselSOIChanged.Remove(OnSoiChanged);
            GameEvents.onVesselSituationChange.Remove(OnSituationChanged);
            GameEvents.onKerbalLevelUp.Remove(OnLevelUp);
            GameEvents.onKerbalStatusChange.Remove(OnStatusChange);
            GameEvents.onKerbalNameChanged.Remove(OnNameChanged);
            GameEvents.onKerbalRemoved.Remove(OnKerbalRemoved);
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            if (Instance == this)
                Instance = null;
        }

        public void Update()
        {
            if (Time.unscaledTime < _nextTick)
                return;
            _nextTick = Time.unscaledTime + TickInterval;

            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log.Error("Simulation tick failed", ex);
            }
        }

        private void Tick()
        {
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null || FlightGlobals.fetch == null)
                return;
            if (HighLogic.LoadedSceneIsFlight && !FlightGlobals.ready)
                return;

            var parameters = RelationsParameters.Current;
            if (parameters == null || !parameters.ModEnabled)
            {
                // Keep the clock current so switching the mod back on does not replay the gap.
                Data.LastUT = Now;
                return;
            }

            var config = ConfigLoader.Config;
            var formatter = KSPUtil.dateTimeFormatter;
            if (formatter != null && formatter.Day > 0 && formatter.Year > 0)
            {
                config.SecondsPerDay = formatter.Day;
                config.SecondsPerYear = formatter.Year;
            }
            config.EnableEvents = parameters.RandomEvents;
            config.EnableReproduction = parameters.Reproduction;
            config.RelationshipSpeed = Math.Max(0.01d, parameters.RelationshipSpeed / 100d);
            config.GrowUpSpeed = Math.Max(1d, parameters.GrowUpSpeed);
            config.EventFrequency = Math.Max(0d, parameters.EventFrequency / 100d);
            var home = FlightGlobals.GetHomeBody();
            if (home != null)
                config.HomeBody = home.bodyName;

            ++_tickCount;
            if (_tickCount % 30 == 1)
                RemoveVanishedKerbals();

            var now = Now;
            Groups = GroupBuilder.Build(this);
            var events = Sim.Tick(now, Groups);

            EggManager.Process(this, events);
            Notify(events, parameters.Messages);

            if (_tickCount % 3 == 0)
                XpTracker.Update(this);

            KerbalRelationsAPI.RaiseHappinessChanges(Data);
            MissionDirector.Update(this);
        }

        /// <summary>Drops state for Kerbals that left the roster without an event (edited saves, other mods).</summary>
        private void RemoveVanishedKerbals()
        {
            var roster = HighLogic.CurrentGame.CrewRoster;
            List<string> gone = null;
            foreach (var name in Data.Kerbals.Keys)
            {
                var pcm = roster[name];
                if (pcm == null || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead)
                    (gone ?? (gone = new List<string>())).Add(name);
            }
            if (gone == null)
                return;
            for (int i = 0; i < gone.Count; ++i)
                Data.Remove(gone[i]);
        }

        private void Notify(List<SimEvent> events, bool showMessages)
        {
            if (!showMessages)
                return;
            var shown = NotifyRibbons(events);
            for (int i = 0; i < events.Count && shown < MaxMessagesPerTick; ++i)
            {
                var e = events[i];
                string text = null;
                switch (e.Type)
                {
                    case SimEventType.RandomEvent:
                        // Everyday events are only interesting for the crew you are looking at.
                        if (IsOnActiveVessel(e.A))
                            text = string.Format("{0}: {1} ({2}{3:0}%)", RelationsSim.ShortName(e.A), e.Text, e.Amount >= 0d ? "+" : string.Empty, e.Amount);
                        break;
                    case SimEventType.BecameCouple:
                    case SimEventType.BrokeUp:
                    case SimEventType.Hatched:
                    case SimEventType.GrewUp:
                        text = e.Text;
                        break;
                }
                if (text == null)
                    continue;
                ScreenMessages.PostScreenMessage(text, 6f, ScreenMessageStyle.UPPER_CENTER);
                ++shown;
            }
        }

        /// <summary>One message per ribbon, however many Kerbals earned it at the same moment.</summary>
        private int NotifyRibbons(List<SimEvent> events)
        {
            List<string> ribbons = null;
            for (int i = 0; i < events.Count; ++i)
            {
                if (events[i].Type == SimEventType.Ribbon && (ribbons == null || !ribbons.Contains(events[i].B)))
                    (ribbons ?? (ribbons = new List<string>())).Add(events[i].B);
            }

            var shown = 0;
            for (int r = 0; ribbons != null && r < ribbons.Count && shown < MaxMessagesPerTick; ++r)
            {
                var names = new List<string>();
                for (int i = 0; i < events.Count; ++i)
                {
                    if (events[i].Type == SimEventType.Ribbon && events[i].B == ribbons[r])
                        names.Add(RelationsSim.ShortName(events[i].A));
                }
                string who;
                if (names.Count == 1)
                    who = names[0];
                else if (names.Count <= 3)
                    who = string.Join(", ", names.ToArray(), 0, names.Count - 1) + " and " + names[names.Count - 1];
                else
                    who = names[0] + ", " + names[1] + " and " + (names.Count - 2) + " others";
                ScreenMessages.PostScreenMessage(who + " earned the " + ConfigLoader.Defs.ResolveRibbon(ribbons[r]).Name + " ribbon",
                    6f, ScreenMessageStyle.UPPER_CENTER);
                ++shown;
            }
            return shown;
        }

        private static bool IsOnActiveVessel(string kerbalName)
        {
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.ActiveVessel == null)
                return false;
            var crew = FlightGlobals.ActiveVessel.GetVesselCrew();
            for (int i = 0; i < crew.Count; ++i)
            {
                if (crew[i].name == kerbalName)
                    return true;
            }
            return false;
        }

        private void OnSoiChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> data)
        {
            if (!Active || data.host == null || data.to == null)
                return;
            var crew = data.host.GetVesselCrew();
            for (int i = 0; i < crew.Count; ++i)
                Sim.OnEnteredSoi(crew[i].name, data.to.bodyName, data.to.isHomeWorld, Now);
        }

        private void OnSituationChanged(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> data)
        {
            if (!Active || data.host == null || data.host.mainBody == null)
                return;
            if (data.to != Vessel.Situations.LANDED && data.to != Vessel.Situations.SPLASHED)
                return;
            if (data.from == Vessel.Situations.LANDED || data.from == Vessel.Situations.SPLASHED || data.from == Vessel.Situations.PRELAUNCH)
                return;
            var crew = data.host.GetVesselCrew();
            for (int i = 0; i < crew.Count; ++i)
                Sim.OnLanded(crew[i].name, data.host.mainBody.bodyName, data.host.mainBody.isHomeWorld, Now);
        }

        private void OnLevelUp(ProtoCrewMember pcm)
        {
            if (Active && pcm != null)
                Sim.OnLevelUp(pcm.name, Now);
        }

        private void OnStatusChange(ProtoCrewMember pcm, ProtoCrewMember.RosterStatus from, ProtoCrewMember.RosterStatus to)
        {
            if (pcm != null && to == ProtoCrewMember.RosterStatus.Dead)
                Sim.OnKerbalDied(pcm.name, Now);
        }

        private void OnNameChanged(ProtoCrewMember pcm, string from, string to)
        {
            Data.Rename(from, to);
        }

        private void OnKerbalRemoved(ProtoCrewMember pcm)
        {
            if (pcm != null)
                Data.Remove(pcm.name);
        }

        private void OnVesselRecovered(ProtoVessel vessel, bool quick)
        {
            if (!Active || vessel == null)
                return;
            var crew = vessel.GetVesselCrew();
            for (int i = 0; i < crew.Count; ++i)
                Sim.OnReturnedHome(crew[i].name, Now);
        }

        private static DataNode ToDataNode(ConfigNode node)
        {
            var result = new DataNode(node.name);
            for (int i = 0; i < node.values.Count; ++i)
                result.Add(node.values[i].name, node.values[i].value);
            for (int i = 0; i < node.nodes.Count; ++i)
                result.Nodes.Add(ToDataNode(node.nodes[i]));
            return result;
        }

        private static ConfigNode ToConfigNode(DataNode node)
        {
            var result = new ConfigNode(node.Name);
            for (int i = 0; i < node.Values.Count; ++i)
                result.AddValue(node.Values[i].Key, node.Values[i].Value);
            for (int i = 0; i < node.Nodes.Count; ++i)
                result.AddNode(ToConfigNode(node.Nodes[i]));
            return result;
        }
    }
}
