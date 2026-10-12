using System;
using System.Collections.Generic;
using Contracts;
using KerbalRelations.Core;
using UnityEngine;

namespace KerbalRelations.Missions
{
    /// <summary>
    /// One line of a mission. The mission itself decides when it is done; this only shows it and
    /// carries the state.
    /// </summary>
    public class KRGoal : ContractParameter
    {
        private string _text = string.Empty;
        private string _progress;

        public KRGoal()
        {
            // The goal can be lost again (company leaves, the mood drops) until the mission completes.
            disableOnStateChange = false;
        }

        public KRGoal(string text) : this()
        {
            _text = text;
        }

        protected override string GetTitle()
        {
            return _progress != null ? _text + " (" + _progress + ")" : _text;
        }

        protected override string GetHashString()
        {
            return _text;
        }

        protected override void OnSave(ConfigNode node)
        {
            node.AddValue("text", _text);
        }

        protected override void OnLoad(ConfigNode node)
        {
            _text = node.GetValue("text") ?? string.Empty;
            disableOnStateChange = false;
        }

        public void Set(bool complete)
        {
            if (complete)
                SetComplete();
            else
                SetIncomplete();
        }

        public void Fail()
        {
            SetFailed();
        }

        /// <summary>A note after the text, such as "12 of 30 days".</summary>
        public void SetProgress(string progress)
        {
            if (progress == _progress)
                return;
            _progress = progress;
            if (Root != null)
                GameEvents.Contract.onParameterChange.Fire(Root, this);
        }
    }

    /// <summary>
    /// Base of the mod's missions. A mission is about one or two Kerbals and maybe a body; it looks at
    /// the simulation once a second and ticks off its goals.
    /// </summary>
    /// <remarks>
    /// Not abstract on purpose: stock creates an instance of every contract class it finds when it
    /// looks for something to offer. This one always says no.
    /// </remarks>
    public class RelationsContract : Contract
    {
        protected string subject = string.Empty;
        protected string other = string.Empty;
        protected int bodyIndex = -1;
        /// <summary>UT since which a goal that has to last has been holding, or -1.</summary>
        protected double heldSince = -1d;

        private float _nextCheck;
        private float _nextWantedCheck;
        private bool _reportedError;

        /// <summary>What the mission is about; no two missions on offer share a key.</summary>
        public string Key
        {
            get { return KeyFor(GetType(), subject, other); }
        }

        public static string KeyFor(Type type, string subject, string other = "")
        {
            return type.Name + "/" + subject + "/" + other;
        }

        protected static RelationsScenario Scenario
        {
            get
            {
                var scenario = RelationsScenario.Instance;
                return scenario != null && scenario.Active && scenario.Ready ? scenario : null;
            }
        }

        protected CelestialBody Body
        {
            get
            {
                var bodies = FlightGlobals.Bodies;
                return bodies != null && bodyIndex >= 0 && bodyIndex < bodies.Count ? bodies[bodyIndex] : null;
            }
        }

        protected static string Short(string name)
        {
            return RelationsSim.ShortName(name);
        }

        protected KRGoal Goal(int index)
        {
            return index < ParameterCount ? GetParameter(index) as KRGoal : null;
        }

        public override bool MeetRequirements()
        {
            // Stock asks this for missions on offer and withdraws them when it turns false.
            if (ContractState == State.Active)
                return true;
            if (!MissionDirector.Enabled || GetType() == typeof(RelationsContract))
                return false;
            if (ContractState != State.Offered || Time.unscaledTime < _nextWantedCheck)
                return true;
            _nextWantedCheck = Time.unscaledTime + 5f;

            // In the editor the simulation is not running; nothing can be said there.
            var scenario = Scenario;
            if (scenario == null)
                return true;
            try
            {
                return StillWanted(scenario);
            }
            catch (Exception ex)
            {
                Report(ex);
                return true;
            }
        }

        /// <summary>False once the reason for an offered mission has gone away.</summary>
        protected virtual bool StillWanted(RelationsScenario scenario)
        {
            return true;
        }

        /// <summary>Looks at the simulation and sets the goals.</summary>
        protected virtual void Check(RelationsScenario scenario)
        {
        }

        protected override void OnUpdate()
        {
            if (ContractState != State.Active || Time.unscaledTime < _nextCheck)
                return;
            _nextCheck = Time.unscaledTime + 1f;
            var scenario = Scenario;
            if (scenario == null)
                return;
            try
            {
                Check(scenario);
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private void Report(Exception ex)
        {
            if (_reportedError)
                return;
            _reportedError = true;
            Log.Error("Mission '" + GetTitle() + "' could not be checked", ex);
        }

        /// <summary>
        /// For goals that have to last: complete once the condition has held for the given time
        /// without a break.
        /// </summary>
        protected void Hold(KRGoal goal, bool condition, double seconds)
        {
            if (goal == null)
                return;
            var now = RelationsScenario.Now;
            if (!condition)
            {
                heldSince = -1d;
                goal.SetProgress(null);
                goal.Set(false);
                return;
            }
            if (heldSince < 0d || heldSince > now)
                heldSince = now;
            var config = ConfigLoader.Config;
            var done = now - heldSince >= seconds;
            goal.SetProgress(done ? null : string.Format("{0:0} of {1:0} days",
                Math.Floor((now - heldSince) / config.SecondsPerDay), Math.Ceiling(seconds / config.SecondsPerDay)));
            goal.Set(done);
        }

        /// <summary>A mood boost for a Kerbal the completed mission was about.</summary>
        protected static void Cheer(RelationsScenario scenario, string name)
        {
            if (scenario != null && !string.IsNullOrEmpty(name))
                scenario.Sim.ApplyModifier(name, ModifierIds.MissionDone, RelationsScenario.Now);
        }

        protected static List<string> GroupOf(RelationsScenario scenario, KerbalState k)
        {
            var groups = scenario.Groups;
            return k != null && k.GroupIndex >= 0 && k.GroupIndex < groups.Count ? groups[k.GroupIndex].Members : null;
        }

        protected override string GetHashString()
        {
            return Key + "/" + MissionSeed;
        }

        public override bool CanBeCancelled()
        {
            return true;
        }

        public override bool CanBeDeclined()
        {
            return true;
        }

        protected override void OnDeclined()
        {
            MissionDirector.NoteDeclined(Key);
        }

        protected override void OnSave(ConfigNode node)
        {
            node.AddValue("subject", subject);
            node.AddValue("other", other);
            node.AddValue("body", bodyIndex);
            node.AddValue("heldSince", heldSince.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        protected override void OnLoad(ConfigNode node)
        {
            subject = node.GetValue("subject") ?? string.Empty;
            other = node.GetValue("other") ?? string.Empty;
            bodyIndex = ReadInt(node, "body", -1);
            heldSince = ReadDouble(node, "heldSince", -1d);
        }

        protected static int ReadInt(ConfigNode node, string name, int fallback)
        {
            int value;
            return int.TryParse(node.GetValue(name), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        protected static double ReadDouble(ConfigNode node, string name, double fallback)
        {
            double value;
            return double.TryParse(node.GetValue(name), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }
}
