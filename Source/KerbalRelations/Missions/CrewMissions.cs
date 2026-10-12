using System.Collections.Generic;
using Contracts;
using FinePrint.Utilities;
using KerbalRelations.Core;
using KerbalRelations.Effects;

namespace KerbalRelations.Missions
{
    /// <summary>"Jeb is lonely": send someone to keep a Kerbal company.</summary>
    public class KRCompanyContract : RelationsContract
    {
        private const double CompanyDays = 20d;

        private static bool IsLonely(RelationsScenario scenario, KerbalState k, double now)
        {
            return k.Stage == LifeStage.Adult && !k.Visitor && !k.AtHome && k.GroupIndex >= 0
                && k.AloneSince >= 0d && now - k.AloneSince > scenario.Config.Days(scenario.Config.AloneGraceDays)
                && MissionDirector.IsCrew(k.Name);
        }

        protected override bool Generate()
        {
            var scenario = Scenario;
            if (scenario == null || !MissionDirector.HasRoom<KRCompanyContract>(2) || PartsOnHand.BestSeats() < 1)
                return false;

            var now = RelationsScenario.Now;
            var lonely = new List<KerbalState>();
            foreach (var k in scenario.Data.Kerbals.Values)
            {
                if (IsLonely(scenario, k, now) && !MissionDirector.Taken(KeyFor(typeof(KRCompanyContract), k.Name)))
                    lonely.Add(k);
            }
            if (lonely.Count == 0)
                return false;

            var pick = lonely[UnityEngine.Random.Range(0, lonely.Count)];
            var body = MissionDirector.BodyOf(scenario, pick);
            subject = pick.Name;
            bodyIndex = body.flightGlobalsIndex;
            prestige = MissionDirector.PrestigeFor(body);

            AddParameter(new KRGoal(string.Format("Keep {0} company for {1:0} days, or bring {0} home", Short(subject), CompanyDays)));
            SetExpiry();
            SetDeadlineYears(4f);
            SetFunds(6000f, 26000f, 6000f, body);
            SetReputation(10f, 5f, body);
            return true;
        }

        protected override bool StillWanted(RelationsScenario scenario)
        {
            var k = scenario.Data.Get(subject);
            return k != null && !k.AtHome && (k.GroupIndex < 0 || k.BestCompany == null);
        }

        protected override void Check(RelationsScenario scenario)
        {
            var goal = Goal(0);
            var k = scenario.Data.Get(subject);
            if (goal == null)
                return;
            if (k == null)
            {
                goal.Fail();
                return;
            }
            if (k.AtHome)
            {
                goal.SetProgress(null);
                goal.Set(true);
                return;
            }
            // Between vessels nobody is tracked for a moment; that is not a reason to start over.
            if (k.GroupIndex >= 0)
                Hold(goal, k.BestCompany != null, scenario.Config.Days(CompanyDays));
        }

        protected override void OnCompleted()
        {
            var scenario = Scenario;
            if (scenario == null)
                return;
            Cheer(scenario, subject);
            var k = scenario.Data.Get(subject);
            var group = k != null && !k.AtHome ? GroupOf(scenario, k) : null;
            for (int i = 0; group != null && i < group.Count; ++i)
            {
                if (group[i] != subject)
                    scenario.Sim.Award(scenario.Data.Get(group[i]), RibbonIds.GoodCompany, RelationsScenario.Now);
            }
        }

        protected override string GetTitle()
        {
            return "Keep " + subject + " company";
        }

        protected override string GetSynopsys()
        {
            return string.Format("{0} is all alone at {1}. Send somebody to keep {0} company.", Short(subject), MissionDirector.Name(Body));
        }

        protected override string GetDescription()
        {
            return string.Format("{0} has had nobody to talk to for far too long and has started naming the switches on the "
                + "control panel. The switches do not answer. Please send company to {1}, or bring {0} home, before the switches do.",
                Short(subject), MissionDirector.Name(Body));
        }

        protected override string MessageCompleted()
        {
            return Short(subject) + " is no longer alone, and the switches have their names back.";
        }
    }

    /// <summary>"Bill and Val miss each other": bring a separated couple back together.</summary>
    public class KRReunionContract : RelationsContract
    {
        private static bool Together(KerbalState a, KerbalState b)
        {
            return a.GroupIndex >= 0 && a.GroupIndex == b.GroupIndex;
        }

        protected override bool Generate()
        {
            var scenario = Scenario;
            if (scenario == null || !MissionDirector.HasRoom<KRReunionContract>(2) || PartsOnHand.BestSeats() < 1)
                return false;

            var config = scenario.Config;
            var now = RelationsScenario.Now;
            var apart = new List<Relationship>();
            foreach (var rel in scenario.Data.Relationships.Values)
            {
                if (!rel.IsCouple)
                    continue;
                var a = scenario.Data.Get(rel.A);
                var b = scenario.Data.Get(rel.B);
                if (a == null || b == null || a.GroupIndex < 0 || b.GroupIndex < 0 || Together(a, b))
                    continue;
                if (a.PartnerApartSince < 0d || now - a.PartnerApartSince <= config.Days(config.MissPartnerGraceDays))
                    continue;
                if (!MissionDirector.IsCrew(rel.A) || !MissionDirector.IsCrew(rel.B))
                    continue;
                if (!MissionDirector.Taken(KeyFor(typeof(KRReunionContract), rel.A, rel.B)))
                    apart.Add(rel);
            }
            if (apart.Count == 0)
                return false;

            var pick = apart[UnityEngine.Random.Range(0, apart.Count)];
            subject = pick.A;
            other = pick.B;
            // The one who is further from home decides how hard this is.
            var bodyA = MissionDirector.BodyOf(scenario, scenario.Data.Get(subject));
            var bodyB = MissionDirector.BodyOf(scenario, scenario.Data.Get(other));
            var body = MissionDirector.PrestigeFor(bodyA) >= MissionDirector.PrestigeFor(bodyB) ? bodyA : bodyB;
            bodyIndex = body.flightGlobalsIndex;
            prestige = MissionDirector.PrestigeFor(body);

            AddParameter(new KRGoal(string.Format("Bring {0} and {1} back together", Short(subject), Short(other))));
            SetExpiry();
            SetDeadlineYears(4f);
            SetFunds(7000f, 30000f, 7000f, body);
            SetReputation(12f, 6f, body);
            return true;
        }

        protected override bool StillWanted(RelationsScenario scenario)
        {
            var a = scenario.Data.Get(subject);
            var b = scenario.Data.Get(other);
            var rel = scenario.Data.GetRelationship(subject, other);
            return a != null && b != null && rel != null && rel.IsCouple && !Together(a, b);
        }

        protected override void Check(RelationsScenario scenario)
        {
            var goal = Goal(0);
            if (goal == null)
                return;
            var a = scenario.Data.Get(subject);
            var b = scenario.Data.Get(other);
            var rel = scenario.Data.GetRelationship(subject, other);
            if (a == null || b == null || rel == null || !rel.IsCouple)
            {
                // One of them is gone, or they gave up on each other.
                goal.Fail();
                return;
            }
            goal.Set(Together(a, b));
        }

        protected override void OnCompleted()
        {
            Cheer(Scenario, subject);
            Cheer(Scenario, other);
        }

        protected override string GetTitle()
        {
            return "Reunite " + Short(subject) + " and " + Short(other);
        }

        protected override string GetSynopsys()
        {
            return string.Format("{0} and {1} are a couple and have been apart for too long. Bring them to the same place.", Short(subject), Short(other));
        }

        protected override string GetDescription()
        {
            return string.Format("{0} keeps looking at a photo of {1}. {1} keeps looking at a photo of {0}. Productivity is suffering and "
                + "so is everyone who has to watch. Put the two of them on the same vessel or base, or have them meet at the KSC.",
                Short(subject), Short(other));
        }

        protected override string MessageCompleted()
        {
            return Short(subject) + " and " + Short(other) + " are together again.";
        }

        protected override string MessageFailed()
        {
            return "It is too late for " + Short(subject) + " and " + Short(other) + ".";
        }
    }

    /// <summary>"Bob wants to go home": bring back a Kerbal who has been away for years.</summary>
    public class KRHomecomingContract : RelationsContract
    {
        protected override bool Generate()
        {
            var scenario = Scenario;
            if (scenario == null || !MissionDirector.HasRoom<KRHomecomingContract>(2) || PartsOnHand.BestSeats() < 1)
                return false;

            var config = scenario.Config;
            var now = RelationsScenario.Now;
            var homesick = new List<KerbalState>();
            foreach (var k in scenario.Data.Kerbals.Values)
            {
                if (k.Stage != LifeStage.Adult || k.Visitor || k.AtHome || k.GroupIndex < 0 || k.AwaySince < 0d)
                    continue;
                if (now - k.AwaySince < config.Years(config.HomesickYears) || !MissionDirector.IsCrew(k.Name))
                    continue;
                if (!MissionDirector.Taken(KeyFor(typeof(KRHomecomingContract), k.Name)))
                    homesick.Add(k);
            }
            if (homesick.Count == 0)
                return false;

            var pick = homesick[UnityEngine.Random.Range(0, homesick.Count)];
            var body = MissionDirector.BodyOf(scenario, pick);
            subject = pick.Name;
            bodyIndex = body.flightGlobalsIndex;
            prestige = MissionDirector.PrestigeFor(body);

            AddParameter(new KRGoal(string.Format("Bring {0} home to {1}", Short(subject), MissionDirector.Name(FlightGlobals.GetHomeBody()))));
            SetExpiry();
            SetDeadlineYears(5f);
            SetFunds(8000f, 34000f, 8000f, body);
            SetReputation(12f, 6f, body);
            return true;
        }

        protected override bool StillWanted(RelationsScenario scenario)
        {
            var k = scenario.Data.Get(subject);
            return k != null && !k.AtHome;
        }

        protected override void Check(RelationsScenario scenario)
        {
            var goal = Goal(0);
            var k = scenario.Data.Get(subject);
            if (goal == null)
                return;
            if (k == null)
                goal.Fail();
            else
                goal.Set(k.AtHome);
        }

        protected override void OnCompleted()
        {
            Cheer(Scenario, subject);
        }

        protected override string GetTitle()
        {
            return "Bring " + subject + " home";
        }

        protected override string GetSynopsys()
        {
            return string.Format("{0} has been away from home for years. Recover {0} on {1}.", Short(subject), MissionDirector.Name(FlightGlobals.GetHomeBody()));
        }

        protected override string GetDescription()
        {
            var k = Scenario != null ? Scenario.Data.Get(subject) : null;
            if (k != null && k.BornInGame)
            {
                return string.Format("{0} was born out there and has only ever seen {1} in pictures. {0} would very much like to "
                    + "find out what rain is. Bring {0} to the KSC.", Short(subject), MissionDirector.Name(FlightGlobals.GetHomeBody()));
            }
            return string.Format("{0} has been at {1} for so long that the houseplants back home have been declared a nature reserve. "
                + "It is time for a holiday. Bring {0} back to the KSC.", Short(subject), MissionDirector.Name(Body));
        }

        protected override string MessageCompleted()
        {
            return Short(subject) + " is home at last.";
        }
    }

    /// <summary>"Keep a crew of five above 120% for a year."</summary>
    public class KRHappyCrewContract : RelationsContract
    {
        private int _crew = 3;
        private double _happiness = 110d;
        private double _days = 60d;

        protected override bool Generate()
        {
            var scenario = Scenario;
            if (scenario == null || !MissionDirector.HasRoom<KRHappyCrewContract>(1) || MissionDirector.Taken(KeyFor(typeof(KRHappyCrewContract), string.Empty)))
                return false;

            // Only for space programmes that already keep a crew somewhere.
            var largest = 0;
            for (int g = 0; g < scenario.Groups.Count; ++g)
            {
                if (!scenario.Groups[g].AtHome)
                    largest = System.Math.Max(largest, CountHappy(scenario, scenario.Groups[g], 0d));
            }
            if (largest < 2)
                return false;

            if (largest >= 4 && UnityEngine.Random.value < 0.5f)
            {
                prestige = ContractPrestige.Exceptional;
                _crew = 5;
                _happiness = 130d;
                _days = 400d;
            }
            else if (largest >= 3 && UnityEngine.Random.value < 0.6f)
            {
                prestige = ContractPrestige.Significant;
                _crew = 4;
                _happiness = 120d;
                _days = 150d;
            }
            else
            {
                prestige = ContractPrestige.Trivial;
            }
            if (PartsOnHand.BestSeats() < 1 || HighLogic.CurrentGame.CrewRoster.GetActiveCrewCount() < _crew)
                return false;

            AddParameter(new KRGoal(string.Format("Have {0} Kerbals together away from home, each at {1:0}% happiness or more, for {2:0} days",
                _crew, _happiness, _days)));
            SetExpiry();
            SetDeadlineYears(6f);
            SetFunds(9000f, 36000f, 9000f);
            SetReputation(14f, 7f);
            return true;
        }

        private static int CountHappy(RelationsScenario scenario, SimGroup group, double happiness)
        {
            var count = 0;
            for (int i = 0; i < group.Members.Count; ++i)
            {
                var k = scenario.Data.Get(group.Members[i]);
                if (k != null && k.Stage != LifeStage.Egg && !k.Visitor && k.Happiness >= happiness)
                    ++count;
            }
            return count;
        }

        protected override void Check(RelationsScenario scenario)
        {
            var happy = false;
            for (int g = 0; g < scenario.Groups.Count && !happy; ++g)
                happy = !scenario.Groups[g].AtHome && CountHappy(scenario, scenario.Groups[g], _happiness) >= _crew;
            Hold(Goal(0), happy, scenario.Config.Days(_days));
        }

        protected override string GetTitle()
        {
            return string.Format("Keep a crew of {0} happy", _crew);
        }

        protected override string GetSynopsys()
        {
            return string.Format("Show that a crew of {0} can stay at {1:0}% happiness or more for {2:0} days away from home.", _crew, _happiness, _days);
        }

        protected override string GetDescription()
        {
            return "The psychology department has a theory that Kerbals in space can be happy for longer than a weekend, and a lot of "
                + "paperwork riding on it. Friends, partners and good news all help. So does not leaving anyone behind.";
        }

        protected override string MessageCompleted()
        {
            return "The psychology department thanks you. The theory holds.";
        }

        protected override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.AddValue("crew", _crew);
            node.AddValue("happiness", _happiness.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            node.AddValue("days", _days.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            _crew = ReadInt(node, "crew", _crew);
            _happiness = ReadDouble(node, "happiness", _happiness);
            _days = ReadDouble(node, "days", _days);
        }
    }

    /// <summary>"Raise the first child born at the Mun."</summary>
    public class KRFirstChildContract : RelationsContract
    {
        protected override bool Generate()
        {
            var scenario = Scenario;
            if (scenario == null || !scenario.ReproductionActive || !MissionDirector.HasRoom<KRFirstChildContract>(1) || !PartsOnHand.Nest())
                return false;

            // Somewhere Kerbals have already been in orbit, and where nobody was born yet.
            var bodies = ProgressUtilities.GetBodiesProgress(ProgressType.ORBIT, true, MannedStatus.MANNED,
                b => b != null && !b.isHomeWorld && b != Planetarium.fetch.Sun
                    && !scenario.Data.BirthBodies.Contains(b.bodyName)
                    && !MissionDirector.Taken(KeyFor(typeof(KRFirstChildContract), b.bodyName)));
            if (bodies == null || bodies.Count == 0)
                return false;

            var body = bodies[UnityEngine.Random.Range(0, bodies.Count)];
            subject = body.bodyName;
            bodyIndex = body.flightGlobalsIndex;
            prestige = MissionDirector.PrestigeFor(body);

            AddParameter(new KRGoal("Have an egg hatch at " + MissionDirector.Name(body)));
            SetExpiry();
            SetDeadlineYears(8f);
            SetFunds(14000f, 60000f, 14000f, body);
            SetReputation(20f, 8f, body);
            SetScience(8f, body);
            return true;
        }

        protected override bool StillWanted(RelationsScenario scenario)
        {
            return scenario.ReproductionActive && !scenario.Data.BirthBodies.Contains(subject);
        }

        private KerbalState FindChild(RelationsScenario scenario)
        {
            foreach (var k in scenario.Data.Kerbals.Values)
            {
                if (k.BornInGame && !k.Visitor && k.Stage != LifeStage.Egg && k.BirthBody == subject && k.BirthUT >= DateAccepted)
                    return k;
            }
            return null;
        }

        protected override void Check(RelationsScenario scenario)
        {
            var goal = Goal(0);
            if (goal != null)
                goal.Set(FindChild(scenario) != null);
        }

        protected override void OnCompleted()
        {
            var scenario = Scenario;
            var child = scenario != null ? FindChild(scenario) : null;
            if (child == null)
                return;
            Cheer(scenario, child.ParentA);
            Cheer(scenario, child.ParentB);
        }

        protected override string GetTitle()
        {
            return "First child of " + MissionDirector.Name(Body);
        }

        protected override string GetSynopsys()
        {
            return string.Format("Have a couple lay an egg at {0} and keep it there until it hatches.", MissionDirector.Name(Body));
        }

        protected override string GetDescription()
        {
            return string.Format("Nobody has ever been born at {0}. The history books have a page ready and the publisher is getting "
                + "impatient. A couple with a strong bond, {1}, and nine months of patience are all it takes.", MissionDirector.Name(Body),
                HarmonyBootstrap.GrowthModulePatched ? "a free seat in a part with Kolony Growth switched on" : "a free seat");
        }

        protected override string MessageCompleted()
        {
            return "A child has hatched at " + MissionDirector.Name(Body) + ". The history books have their page.";
        }
    }

    /// <summary>"Have a family of four on one base."</summary>
    public class KRFamilyContract : RelationsContract
    {
        private const int Children = 2;

        protected override bool Generate()
        {
            var scenario = Scenario;
            if (scenario == null || !scenario.ReproductionActive || !MissionDirector.HasRoom<KRFamilyContract>(1) || !PartsOnHand.Nest())
                return false;
            if (MissionDirector.Taken(KeyFor(typeof(KRFamilyContract), string.Empty)) || FindFamily(scenario) != null)
                return false;

            // Wait until there is a couple who could do it.
            var anyCouple = false;
            foreach (var rel in scenario.Data.Relationships.Values)
                anyCouple |= rel.IsCouple;
            if (!anyCouple)
                return false;

            prestige = ContractPrestige.Significant;
            AddParameter(new KRGoal(string.Format("Have two parents and {0} of their children together away from home", Children)));
            SetExpiry();
            SetDeadlineYears(10f);
            SetFunds(16000f, 70000f, 16000f);
            SetReputation(24f, 8f);
            return true;
        }

        /// <summary>A parent of a family that is together somewhere away from home, or null.</summary>
        private static KerbalState FindFamily(RelationsScenario scenario)
        {
            for (int g = 0; g < scenario.Groups.Count; ++g)
            {
                var group = scenario.Groups[g];
                if (group.AtHome)
                    continue;
                for (int i = 0; i < group.Members.Count; ++i)
                {
                    var parent = scenario.Data.Get(group.Members[i]);
                    if (parent == null || parent.Partner == null || !group.Members.Contains(parent.Partner))
                        continue;
                    var children = 0;
                    for (int c = 0; c < group.Members.Count; ++c)
                    {
                        var child = scenario.Data.Get(group.Members[c]);
                        if (child != null && child.Stage != LifeStage.Egg && child.HasParent(parent.Name) && child.HasParent(parent.Partner))
                            ++children;
                    }
                    if (children >= Children)
                        return parent;
                }
            }
            return null;
        }

        protected override void Check(RelationsScenario scenario)
        {
            var goal = Goal(0);
            if (goal != null)
                goal.Set(FindFamily(scenario) != null);
        }

        protected override void OnCompleted()
        {
            var scenario = Scenario;
            var parent = scenario != null ? FindFamily(scenario) : null;
            if (parent == null)
                return;
            Cheer(scenario, parent.Name);
            Cheer(scenario, parent.Partner);
        }

        protected override string GetTitle()
        {
            return "A family of four";
        }

        protected override string GetSynopsys()
        {
            return "Have a couple and two of their hatched children living on the same vessel or base, away from home.";
        }

        protected override string GetDescription()
        {
            return "A travel magazine wants to run a story called \"Space: fun for the whole family\" and needs a whole family in "
                + "space to photograph. Two parents and two of their children in one place will do. Eggs do not count: they do not "
                + "smile for the camera.";
        }

        protected override string MessageCompleted()
        {
            return "The photo is on the cover. Everybody blinked.";
        }
    }
}
