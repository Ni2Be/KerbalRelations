using System;
using System.Collections.Generic;

namespace KerbalRelations.Core
{
    /// <summary>Kerbals who are currently together (one vessel, or one base of nearby vessels).</summary>
    public class SimGroup
    {
        public readonly List<string> Members = new List<string>();
        /// <summary>The KSC: neutral happiness, relationships frozen.</summary>
        public bool AtHome;
        /// <summary>A free seat suitable for an egg exists.</summary>
        public bool HasNest;
        /// <summary>Opaque host data (the vessels of the group).</summary>
        public object Tag;
        /// <summary>The vessel each member is aboard, as a number, in the order of Members; empty when all share one.</summary>
        public readonly List<int> Places = new List<int>();
        /// <summary>Metres between two of those vessels, by their numbers; null when the group is one vessel.</summary>
        public double[,] PlaceDistances;

        public int PlaceOf(int member)
        {
            return member < Places.Count ? Places[member] : 0;
        }

        /// <summary>Metres between two vessels of the group; infinity when that is not known.</summary>
        public double Distance(int placeA, int placeB)
        {
            if (placeA == placeB)
                return 0d;
            if (PlaceDistances == null || placeA < 0 || placeB < 0
                || placeA >= PlaceDistances.GetLength(0) || placeB >= PlaceDistances.GetLength(1))
                return double.PositiveInfinity;
            return PlaceDistances[placeA, placeB];
        }
    }

    public enum EggStatus
    {
        NotACouple,
        /// <summary>Families are switched off.</summary>
        Disabled,
        BondTooLow,
        /// <summary>Too soon after the last egg.</summary>
        Cooldown,
        Apart,
        AtHome,
        /// <summary>Together and willing, but no free seat that counts as a nest.</summary>
        NoNest,
        /// <summary>Everything is in place; an egg comes after a random wait.</summary>
        Trying,
        /// <summary>Due, and waiting for the host to create it.</summary>
        Pending
    }

    public enum SimEventType
    {
        RandomEvent,
        BecameCouple,
        BrokeUp,
        /// <summary>The host must create the egg and then call <see cref="RelationsSim.RegisterEgg"/>.</summary>
        EggDue,
        Hatched,
        GrewUp,
        /// <summary>A earned the ribbon with id B.</summary>
        Ribbon
    }

    public class SimEvent
    {
        public SimEventType Type;
        public string A;
        public string B;
        public string Text;
        public double Amount;
        public object Tag;
    }

    public class RelationsSim
    {
        public readonly RelationsData Data;
        public readonly SimConfig Config;
        public readonly Definitions Defs;
        private readonly IRandom _random;

        private readonly List<string> _scratch = new List<string>();
        private readonly List<Relationship> _relScratch = new List<Relationship>();
        /// <summary>Events raised between ticks, by the host's notifications.</summary>
        private readonly List<SimEvent> _queued = new List<SimEvent>();
        private readonly Dictionary<string, int> _friendCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, double> _bestBonds = new Dictionary<string, double>();
        private readonly Dictionary<string, int> _childCounts = new Dictionary<string, int>();

        public RelationsSim(RelationsData data, SimConfig config, Definitions defs, IRandom random)
        {
            Data = data;
            Config = config;
            Defs = defs;
            _random = random;
        }

        public static string ShortName(string name)
        {
            const string suffix = " Kerman";
            if (name != null && name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name.Substring(0, name.Length - suffix.Length);
            return name ?? string.Empty;
        }

        public static string Format(string text, string self, string other)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text.Replace("<<1>>", ShortName(self)).Replace("<<2>>", ShortName(other));
        }

        /// <summary>How well two Kerbals get along: a fixed property of the pair.</summary>
        public double Compatibility(string a, string b)
        {
            return Config.CompatibilityMin
                + (Config.CompatibilityMax - Config.CompatibilityMin) * StableHash.Unit(PairKey.Make(a, b));
        }

        /// <summary>
        /// Advances the simulation to <paramref name="now"/>. Group membership is assumed constant over
        /// the interval; long intervals (time warp) are split into sub-steps.
        /// </summary>
        public List<SimEvent> Tick(double now, IList<SimGroup> groups)
        {
            var events = new List<SimEvent>();
            if (Data.LastUT < 0d || now <= Data.LastUT)
            {
                Data.LastUT = now;
                return events;
            }

            var dt = now - Data.LastUT;
            var maxStep = Math.Max(1d, Config.Days(Config.MaxStepDays));
            var steps = (int)Math.Ceiling(dt / maxStep);
            if (steps < 1)
                steps = 1;
            if (steps > Config.MaxSubSteps)
                steps = Math.Max(1, Config.MaxSubSteps);
            var h = dt / steps;
            var t = Data.LastUT;
            for (int i = 0; i < steps; ++i)
            {
                t += h;
                Step(t, h, groups, events);
            }
            Data.LastUT = now;

            if (Config.EnableRibbons)
                RibbonPass(now, events);
            events.AddRange(_queued);
            _queued.Clear();
            return events;
        }

        /// <summary>
        /// Age in years. Children age towards adulthood at the growing-up speed, so a child always
        /// comes of age at <see cref="SimConfig.ChildDurationYears"/>. -1 when unknown.
        /// </summary>
        public double AgeYears(KerbalState k, double now)
        {
            if (k == null || k.Stage == LifeStage.Egg)
                return -1d;
            if (k.Stage == LifeStage.Child)
            {
                var duration = Math.Max(1d, Config.ChildDuration);
                return Config.ChildDurationYears * Math.Max(0d, Math.Min(1d, (now - k.StageStartUT) / duration));
            }
            if (k.BornInGame)
                return Config.ChildDurationYears + Math.Max(0d, now - k.StageStartUT) / Config.SecondsPerYear;
            return k.BirthPlace != null ? Math.Max(0d, now - k.BirthUT) / Config.SecondsPerYear : -1d;
        }

        /// <summary>Hired Kerbals come with an age and a birthplace of their own.</summary>
        private void AssignBackstory(KerbalState k, double now)
        {
            var span = Math.Max(0d, Config.HireAgeMaxYears - Config.HireAgeMinYears);
            var age = Config.HireAgeMinYears + span * StableHash.Unit(k.Name + "/age");
            k.BirthUT = now - Config.Years(age);
            k.BirthPlace = "on " + Config.HomeBody;
            k.BirthBody = Config.HomeBody;
        }

        /// <summary>
        /// Adds a guest of a mission: a child or an egg that travels along, stays as it is and leaves
        /// again.
        /// </summary>
        public KerbalState RegisterVisitor(string name, Sex sex, LifeStage stage, double ageYears, double now)
        {
            var k = Data.GetOrCreate(name, Config.CompanyHappiness);
            k.Sex = sex;
            k.Stage = stage;
            k.Visitor = true;
            k.Eligible = false;
            k.BirthPlace = "on " + Config.HomeBody;
            k.BirthBody = Config.HomeBody;
            if (stage == LifeStage.Child)
            {
                var fraction = Math.Max(0d, Math.Min(1d, ageYears / Math.Max(1d, Config.ChildDurationYears)));
                k.StageStartUT = now - fraction * Config.ChildDuration;
            }
            else
            {
                k.StageStartUT = now;
            }
            k.BirthUT = k.StageStartUT;
            return k;
        }

        /// <summary>
        /// Gives a Kerbal a ribbon they do not have yet. Eggs and guests collect none.
        /// </summary>
        /// <param name="silent">No message and no mood boost: for things that happened long ago.</param>
        public bool Award(KerbalState k, string id, double now, bool silent = false)
        {
            return Award(k, id, now, _queued, silent);
        }

        private bool Award(KerbalState k, string id, double now, List<SimEvent> events, bool silent = false)
        {
            if (k == null || !Config.EnableRibbons || k.Stage == LifeStage.Egg || k.Visitor || k.HasRibbon(id))
                return false;
            k.Ribbons.Add(new RibbonAward { Id = id, UT = now });
            if (silent)
                return true;
            var def = Defs.ResolveRibbon(id);
            ApplyDef(k, ModifierIds.RibbonEarned, def.Name, now);
            events.Add(new SimEvent
            {
                Type = SimEventType.Ribbon,
                A = k.Name,
                B = id,
                Text = ShortName(k.Name) + " earned the " + def.Name + " ribbon"
            });
            return true;
        }

        /// <summary>
        /// Hands out the ribbons that depend on how things stand rather than on a single moment. The
        /// first pass on a save is silent: it catches up on what happened before ribbons existed.
        /// </summary>
        private void RibbonPass(double now, List<SimEvent> events)
        {
            var silent = !Data.RibbonsSeeded;

            _friendCounts.Clear();
            _bestBonds.Clear();
            _childCounts.Clear();
            foreach (var rel in Data.Relationships.Values)
            {
                NoteBond(rel.A, rel.Strength);
                NoteBond(rel.B, rel.Strength);
                if (rel.IsCouple && rel.CoupleSince >= 0d && now - rel.CoupleSince >= Config.Years(RibbonIds.AnniversaryYears))
                {
                    Award(Data.Get(rel.A), RibbonIds.Anniversary, now, events, silent);
                    Award(Data.Get(rel.B), RibbonIds.Anniversary, now, events, silent);
                }
            }

            foreach (var child in Data.Kerbals.Values)
            {
                if (child.Stage == LifeStage.Egg || child.Visitor || !child.BornInGame)
                    continue;
                NoteChild(child.ParentA);
                NoteChild(child.ParentB);
                var a = Data.Get(child.ParentA);
                var b = Data.Get(child.ParentB);
                if (child.Stage == LifeStage.Adult)
                {
                    Award(child, RibbonIds.CameOfAge, now, events, silent);
                    Award(a, RibbonIds.ProudParent, now, events, silent);
                    Award(b, RibbonIds.ProudParent, now, events, silent);
                }
                if (child.BirthBody != null && child.BirthBody != Config.HomeBody)
                    Award(child, RibbonIds.SpaceBorn, now, events, silent);
                AwardGrandparents(a, now, events, silent);
                AwardGrandparents(b, now, events, silent);
            }

            foreach (var k in Data.Kerbals.Values)
            {
                if (k.Stage == LifeStage.Egg || k.Visitor)
                    continue;

                double best;
                if (_bestBonds.TryGetValue(k.Name, out best))
                {
                    if (best >= RibbonIds.FriendBond)
                        Award(k, RibbonIds.FirstFriend, now, events, silent);
                    if (best >= RibbonIds.BestFriendBond)
                        Award(k, RibbonIds.BestFriends, now, events, silent);
                }
                int count;
                if (_friendCounts.TryGetValue(k.Name, out count) && count >= RibbonIds.ButterflyFriends)
                    Award(k, RibbonIds.SocialButterfly, now, events, silent);
                if (_childCounts.TryGetValue(k.Name, out count))
                {
                    Award(k, RibbonIds.Parent, now, events, silent);
                    if (count >= RibbonIds.BigFamilyChildren)
                        Award(k, RibbonIds.BigFamily, now, events, silent);
                }
                if (k.Partner != null)
                    Award(k, RibbonIds.Sweetheart, now, events, silent);

                foreach (var body in k.VisitedBodies)
                    Award(k, RibbonIds.VisitPrefix + body, now, events, silent);
                foreach (var body in k.LandedBodies)
                    Award(k, RibbonIds.LandPrefix + body, now, events, silent);
                if (k.VisitedBodies.Count >= RibbonIds.ExplorerBodies)
                    Award(k, RibbonIds.Explorer, now, events, silent);
                if (k.LandedBodies.Count >= RibbonIds.GrandTourBodies)
                    Award(k, RibbonIds.GrandTour, now, events, silent);

                if (k.Level >= RibbonIds.SeasonedLevel)
                    Award(k, RibbonIds.Seasoned, now, events, silent);
                if (k.Level >= RibbonIds.VeteranLevel)
                    Award(k, RibbonIds.Veteran, now, events, silent);
                if (k.Stage == LifeStage.Adult && AgeYears(k, now) >= RibbonIds.SilverAge)
                    Award(k, RibbonIds.SilverKerbal, now, events, silent);

                // The rest is about how the Kerbal is doing right now, away from home.
                if (k.GroupIndex < 0 || k.AtHome)
                    continue;
                if (k.AloneSince >= 0d && now - k.AloneSince >= Config.Days(RibbonIds.HermitDays))
                    Award(k, RibbonIds.Hermit, now, events, silent);
                if (k.AwaySince >= 0d && now - k.AwaySince >= Config.Years(RibbonIds.LongHaulYears))
                    Award(k, RibbonIds.LongHaul, now, events, silent);
                if (k.Happiness >= RibbonIds.CloudNineHappiness)
                    Award(k, RibbonIds.CloudNine, now, events, silent);
                if (k.Happiness <= RibbonIds.RoughPatchHappiness)
                    Award(k, RibbonIds.RoughPatch, now, events, silent);
            }

            Data.RibbonsSeeded = true;
        }

        private void NoteBond(string name, double strength)
        {
            if (strength >= RibbonIds.KnownBond)
            {
                int count;
                _friendCounts.TryGetValue(name, out count);
                _friendCounts[name] = count + 1;
            }
            double best;
            if (!_bestBonds.TryGetValue(name, out best) || strength > best)
                _bestBonds[name] = strength;
        }

        private void NoteChild(string parent)
        {
            if (parent == null)
                return;
            int count;
            _childCounts.TryGetValue(parent, out count);
            _childCounts[parent] = count + 1;
        }

        private void AwardGrandparents(KerbalState parent, double now, List<SimEvent> events, bool silent)
        {
            if (parent == null)
                return;
            Award(Data.Get(parent.ParentA), RibbonIds.Grandparent, now, events, silent);
            Award(Data.Get(parent.ParentB), RibbonIds.Grandparent, now, events, silent);
        }

        private void Step(double now, double h, IList<SimGroup> groups, List<SimEvent> events)
        {
            var days = h / Config.SecondsPerDay;

            // Who is where. Kerbals in no group (missing, or between vessels) are left untouched this step.
            foreach (var state in Data.Kerbals.Values)
                state.GroupIndex = -1;
            for (int g = 0; g < groups.Count; ++g)
            {
                var group = groups[g];
                for (int i = 0; i < group.Members.Count; ++i)
                {
                    var k = Data.GetOrCreate(group.Members[i], Config.BaseHappiness);
                    k.AtHome = group.AtHome;
                    k.GroupIndex = g;
                    k.Place = group.PlaceOf(i);
                    if (k.BirthPlace == null && !k.BornInGame && !k.Visitor && k.Stage == LifeStage.Adult)
                        AssignBackstory(k, now);
                    if (!group.AtHome)
                    {
                        if (k.AwaySince < 0d)
                            k.AwaySince = now - h;
                    }
                    else if (k.AwaySince >= 0d)
                    {
                        if (now - k.AwaySince >= Config.Years(RibbonIds.HomecomingYears))
                            Award(k, RibbonIds.Homecoming, now, events);
                        k.AwaySince = -1d;
                    }
                }
            }

            // Time together builds relationships.
            for (int g = 0; g < groups.Count; ++g)
            {
                var group = groups[g];
                if (group.AtHome)
                    continue;
                CollectSocial(group, _scratch);
                for (int i = 0; i < _scratch.Count; ++i)
                {
                    for (int j = i + 1; j < _scratch.Count; ++j)
                    {
                        var rel = Data.GetOrCreateRelationship(_scratch[i], _scratch[j], now);
                        var growth = Config.GrowthPerDay * days * Compatibility(rel.A, rel.B) * Config.RelationshipSpeed;
                        // A bond that got further than the cap aboard one vessel keeps what it has.
                        var cap = BondCap(group, Data.Get(_scratch[i]).Place, Data.Get(_scratch[j]).Place);
                        rel.Strength = Math.Max(rel.Strength, Math.Min(cap, rel.Strength + growth));
                        rel.LastTogetherUT = now;
                    }
                }
            }

            // Time apart slowly erodes them.
            foreach (var rel in Data.Relationships.Values)
            {
                if (rel.LastTogetherUT >= now || rel.Family)
                    continue;
                var a = Data.Get(rel.A);
                var b = Data.Get(rel.B);
                if (a == null || b == null || (a.AtHome && b.AtHome))
                    continue;
                if (now - rel.LastTogetherUT > Config.Days(Config.DecayGraceDays))
                    rel.Strength = Math.Max(0d, rel.Strength - Config.DecayPerDay * days * Config.RelationshipSpeed);
            }

            UpdateCouples(now, groups, events);

            for (int g = 0; g < groups.Count; ++g)
            {
                var group = groups[g];
                for (int i = 0; i < group.Members.Count; ++i)
                    UpdateKerbal(Data.Get(group.Members[i]), group, now, h, events);
            }

            if (Config.EnableReproduction)
                RollEggs(now, h, groups, events);
        }

        /// <summary>Members that take part in social life (everyone but eggs).</summary>
        /// <summary>
        /// How far the bond of two Kerbals of a group can grow where they are: all the way aboard the
        /// same vessel, less far in different vessels, and less again the further those are apart.
        /// </summary>
        public double BondCap(SimGroup group, int placeA, int placeB)
        {
            if (placeA == placeB)
                return 100d;
            var distance = group.Distance(placeA, placeB);
            var far = Config.TogetherRange > 0d && distance >= 0d ? Math.Min(1d, distance / Config.TogetherRange) : 1d;
            return Math.Max(0d, Math.Min(100d, Config.OtherVesselBondCap + (Config.FarBondCap - Config.OtherVesselBondCap) * far));
        }

        private void CollectSocial(SimGroup group, List<string> result)
        {
            result.Clear();
            for (int i = 0; i < group.Members.Count; ++i)
            {
                var k = Data.Get(group.Members[i]);
                if (k != null && k.Stage != LifeStage.Egg)
                    result.Add(k.Name);
            }
        }

        private void UpdateCouples(double now, IList<SimGroup> groups, List<SimEvent> events)
        {
            // Break-ups first, so the newly single can pair up again later, not in the same step.
            _relScratch.Clear();
            foreach (var rel in Data.Relationships.Values)
            {
                if (rel.IsCouple && rel.Strength < Config.BreakupThreshold)
                    _relScratch.Add(rel);
            }
            for (int i = 0; i < _relScratch.Count; ++i)
                BreakUp(_relScratch[i], now, events);

            for (int g = 0; g < groups.Count; ++g)
            {
                var group = groups[g];
                if (group.AtHome)
                    continue;
                CollectSocial(group, _scratch);

                _relScratch.Clear();
                for (int i = 0; i < _scratch.Count; ++i)
                {
                    for (int j = i + 1; j < _scratch.Count; ++j)
                    {
                        var rel = Data.GetRelationship(_scratch[i], _scratch[j]);
                        if (rel != null && CanCouple(rel))
                            _relScratch.Add(rel);
                    }
                }
                // Strongest bonds pair up first; names break ties so the outcome is deterministic.
                _relScratch.Sort(CompareByStrength);
                for (int i = 0; i < _relScratch.Count; ++i)
                {
                    var rel = _relScratch[i];
                    if (!CanCouple(rel))
                        continue;
                    var a = Data.Get(rel.A);
                    var b = Data.Get(rel.B);
                    rel.IsCouple = true;
                    rel.CoupleSince = now;
                    a.Partner = b.Name;
                    b.Partner = a.Name;
                    a.PartnerApartSince = -1d;
                    b.PartnerApartSince = -1d;
                    ApplyDef(a, ModifierIds.BecameCouple, b.Name, now);
                    ApplyDef(b, ModifierIds.BecameCouple, a.Name, now);
                    events.Add(new SimEvent
                    {
                        Type = SimEventType.BecameCouple,
                        A = a.Name,
                        B = b.Name,
                        Text = ShortName(a.Name) + " and " + ShortName(b.Name) + " are now a couple"
                    });
                }
            }
        }

        private static int CompareByStrength(Relationship x, Relationship y)
        {
            var c = y.Strength.CompareTo(x.Strength);
            if (c != 0)
                return c;
            c = string.CompareOrdinal(x.A, y.A);
            return c != 0 ? c : string.CompareOrdinal(x.B, y.B);
        }

        private bool CanCouple(Relationship rel)
        {
            if (rel.IsCouple || rel.Family || rel.Strength < Config.CoupleThreshold)
                return false;
            var a = Data.Get(rel.A);
            var b = Data.Get(rel.B);
            if (a == null || b == null)
                return false;
            return a.Stage == LifeStage.Adult && b.Stage == LifeStage.Adult
                && a.Eligible && b.Eligible
                && a.Partner == null && b.Partner == null
                && a.Sex != b.Sex
                && !Data.IsKin(a.Name, b.Name);
        }

        private void BreakUp(Relationship rel, double now, List<SimEvent> events)
        {
            var a = Data.Get(rel.A);
            var b = Data.Get(rel.B);
            rel.IsCouple = false;
            rel.CoupleSince = -1d;
            rel.EggPendingSince = -1d;
            if (a != null)
            {
                a.Partner = null;
                a.PartnerApartSince = -1d;
                RemoveModifier(a, ModifierIds.MissesPartner);
                ApplyDef(a, ModifierIds.BrokeUp, rel.B, now);
                Award(a, RibbonIds.BrokenHeart, now, events);
            }
            if (b != null)
            {
                b.Partner = null;
                b.PartnerApartSince = -1d;
                RemoveModifier(b, ModifierIds.MissesPartner);
                ApplyDef(b, ModifierIds.BrokeUp, rel.A, now);
                Award(b, RibbonIds.BrokenHeart, now, events);
            }
            events.Add(new SimEvent
            {
                Type = SimEventType.BrokeUp,
                A = rel.A,
                B = rel.B,
                Text = ShortName(rel.A) + " and " + ShortName(rel.B) + " broke up"
            });
        }

        private void UpdateKerbal(KerbalState k, SimGroup group, double now, double h, List<SimEvent> events)
        {
            if (k == null)
                return;

            if (!k.Visitor)
            {
                if (k.Stage == LifeStage.Egg && now - k.StageStartUT >= Config.EggDuration)
                    Hatch(k, now, events);
                if (k.Stage == LifeStage.Child && now - k.StageStartUT >= Config.ChildDuration)
                    GrowUp(k, now, events);
            }

            if (k.Stage == LifeStage.Egg)
            {
                k.Modifiers.Clear();
                return;
            }

            // Company and bonds.
            double target;
            k.BestCompany = null;
            if (group.AtHome)
            {
                target = Config.BaseHappiness;
                k.AloneSince = -1d;
            }
            else
            {
                var best = -1d;
                for (int i = 0; i < group.Members.Count; ++i)
                {
                    var other = Data.Get(group.Members[i]);
                    if (other == null || other == k || other.Stage == LifeStage.Egg)
                        continue;
                    var rel = Data.GetRelationship(k.Name, other.Name);
                    var strength = rel != null ? rel.Strength : 0d;
                    if (strength > best)
                    {
                        best = strength;
                        k.BestCompany = other.Name;
                    }
                }
                if (best >= 0d)
                {
                    target = Config.CompanyHappiness + Config.BondBonusMax * best / 100d;
                    k.AloneSince = -1d;
                }
                else
                {
                    if (k.AloneSince < 0d)
                        k.AloneSince = now - h;
                    target = now - k.AloneSince > Config.Days(Config.AloneGraceDays)
                        ? Config.BaseHappiness - Config.LonelyPenalty
                        : Config.BaseHappiness;
                }
            }
            var tau = Math.Max(1d, Config.Days(Config.SocialEaseDays));
            k.Social += (target - k.Social) * (1d - Math.Exp(-h / tau));

            UpdatePartnerAbsence(k, group, now, h, events);

            for (int i = k.Modifiers.Count - 1; i >= 0; --i)
            {
                if (now >= k.Modifiers[i].ExpiresUT)
                    k.Modifiers.RemoveAt(i);
            }

            if (Config.EnableEvents && Defs.Events.Count > 0 && Config.EventFrequency > 0d)
            {
                var mean = Config.Days(Config.EventMeanIntervalDays) / Config.EventFrequency;
                if (mean > 0d && _random.NextDouble() < 1d - Math.Exp(-h / mean))
                    RollEvent(k, group, now, events);
            }

            RecomputeHappiness(k, now);
        }

        private void UpdatePartnerAbsence(KerbalState k, SimGroup group, double now, double h, List<SimEvent> events)
        {
            if (k.Partner == null)
            {
                k.PartnerApartSince = -1d;
                return;
            }
            var partner = Data.Get(k.Partner);
            var together = partner != null && partner.GroupIndex == k.GroupIndex && group.Members.Contains(k.Partner);
            if (together)
            {
                if (k.PartnerApartSince >= 0d && now - k.PartnerApartSince >= Config.Years(RibbonIds.ReunitedYears))
                    Award(k, RibbonIds.Reunited, now, events);
                k.PartnerApartSince = -1d;
                RemoveModifier(k, ModifierIds.MissesPartner);
                return;
            }
            if (k.PartnerApartSince < 0d)
                k.PartnerApartSince = now - h;
            if (now - k.PartnerApartSince <= Config.Days(Config.MissPartnerGraceDays))
                return;
            var def = Defs.Get(ModifierIds.MissesPartner);
            if (def == null)
                return;
            // Refreshed every step while apart, so it lapses on its own shortly after a reunion.
            var existing = k.FindModifier(ModifierIds.MissesPartner);
            if (existing == null)
            {
                existing = new ActiveModifier { Id = def.Id, StartUT = now, NoFade = true };
                k.Modifiers.Add(existing);
                k.AddLog(now, Format(def.Text, k.Name, k.Partner), def.Amount);
            }
            existing.Text = Format(def.Text, k.Name, k.Partner);
            existing.Amount = def.Amount;
            existing.ExpiresUT = now + h + Config.Days(Math.Max(1d, def.DurationDays));
        }

        public void RecomputeHappiness(KerbalState k, double now)
        {
            var total = k.Social;
            for (int i = 0; i < k.Modifiers.Count; ++i)
                total += k.Modifiers[i].EffectiveAmount(now);
            k.Happiness = Math.Max(Config.MinHappiness, Math.Min(Config.MaxHappiness, total));
        }

        private void RollEvent(KerbalState k, SimGroup group, double now, List<SimEvent> events)
        {
            var eligible = new List<EventDef>();
            var totalWeight = 0d;
            for (int i = 0; i < Defs.Events.Count; ++i)
            {
                var def = Defs.Events[i];
                string other;
                if (def.Weight > 0d && ConditionMet(def, k, group, out other, false))
                {
                    eligible.Add(def);
                    totalWeight += def.Weight;
                }
            }
            if (eligible.Count == 0)
                return;

            var roll = _random.NextDouble() * totalWeight;
            var chosen = eligible[eligible.Count - 1];
            for (int i = 0; i < eligible.Count; ++i)
            {
                roll -= eligible[i].Weight;
                if (roll < 0d)
                {
                    chosen = eligible[i];
                    break;
                }
            }

            string otherName;
            ConditionMet(chosen, k, group, out otherName, true);

            var text = Format(chosen.Text, k.Name, otherName);
            AddModifier(k, chosen.Id, text, chosen.Amount, Config.Days(chosen.DurationDays), now, false);
            var otherState = Data.Get(otherName);
            if (otherState != null)
            {
                if (chosen.RelationDelta != 0d)
                {
                    var rel = Data.GetOrCreateRelationship(k.Name, otherName, now);
                    rel.Strength = Math.Max(0d, Math.Min(100d, rel.Strength + chosen.RelationDelta));
                }
                if (chosen.Mutual && otherState.Stage != LifeStage.Egg)
                {
                    AddModifier(otherState, chosen.Id, Format(chosen.Text, otherName, k.Name), chosen.Amount,
                        Config.Days(chosen.DurationDays), now, false);
                }
            }
            events.Add(new SimEvent
            {
                Type = SimEventType.RandomEvent,
                A = k.Name,
                B = otherName,
                Text = text,
                Amount = chosen.Amount
            });
        }

        /// <param name="pick">When several Kerbals qualify as the "other", choose one at random.</param>
        private bool ConditionMet(EventDef def, KerbalState k, SimGroup group, out string other, bool pick)
        {
            other = null;
            if (def.Stage == StageFilter.Adult && k.Stage != LifeStage.Adult)
                return false;
            if (def.Stage == StageFilter.Child && k.Stage != LifeStage.Child)
                return false;

            switch (def.Condition)
            {
                case EventCondition.Any:
                    return true;

                case EventCondition.Away:
                    return !group.AtHome;

                case EventCondition.Alone:
                    if (group.AtHome)
                        return false;
                    CollectSocial(group, _scratch);
                    return _scratch.Count == 1;

                case EventCondition.Company:
                    if (group.AtHome)
                        return false;
                    CollectSocial(group, _scratch);
                    _scratch.Remove(k.Name);
                    if (_scratch.Count == 0)
                        return false;
                    other = pick ? _scratch[(int)(_random.NextDouble() * _scratch.Count) % _scratch.Count] : _scratch[0];
                    return true;

                case EventCondition.Couple:
                    if (group.AtHome || k.Partner == null || !group.Members.Contains(k.Partner))
                        return false;
                    other = k.Partner;
                    return true;

                case EventCondition.Parent:
                    if (group.AtHome)
                        return false;
                    _scratch.Clear();
                    for (int i = 0; i < group.Members.Count; ++i)
                    {
                        var child = Data.Get(group.Members[i]);
                        if (child != null && child.Stage != LifeStage.Adult && child.HasParent(k.Name))
                            _scratch.Add(child.Name);
                    }
                    if (_scratch.Count == 0)
                        return false;
                    other = pick ? _scratch[(int)(_random.NextDouble() * _scratch.Count) % _scratch.Count] : _scratch[0];
                    return true;
            }
            return false;
        }

        /// <summary>Where a pair stands on having an egg; the first thing in the way wins.</summary>
        public EggStatus GetEggStatus(Relationship rel, double now, IList<SimGroup> groups)
        {
            if (rel == null || !rel.IsCouple)
                return EggStatus.NotACouple;
            if (!Config.EnableReproduction)
                return EggStatus.Disabled;
            if (rel.EggPendingSince >= 0d)
                return EggStatus.Pending;
            if (rel.Strength < Config.EggThreshold)
                return EggStatus.BondTooLow;
            if (rel.LastEggUT >= 0d && now - rel.LastEggUT < Config.Years(Config.EggCooldownYears))
                return EggStatus.Cooldown;
            var a = Data.Get(rel.A);
            var b = Data.Get(rel.B);
            if (a == null || b == null || a.GroupIndex != b.GroupIndex || a.GroupIndex < 0 || a.GroupIndex >= groups.Count)
                return EggStatus.Apart;
            var group = groups[a.GroupIndex];
            if (!group.Members.Contains(a.Name) || !group.Members.Contains(b.Name))
                return EggStatus.Apart;
            if (group.AtHome)
                return EggStatus.AtHome;
            if (a.Stage != LifeStage.Adult || b.Stage != LifeStage.Adult || !a.Eligible || !b.Eligible)
                return EggStatus.NotACouple;
            return group.HasNest ? EggStatus.Trying : EggStatus.NoNest;
        }

        private void RollEggs(double now, double h, IList<SimGroup> groups, List<SimEvent> events)
        {
            var meanWait = Math.Max(1d, Config.Days(Config.EggMeanWaitDays));
            foreach (var rel in Data.Relationships.Values)
            {
                if (GetEggStatus(rel, now, groups) != EggStatus.Trying)
                    continue;
                if (_random.NextDouble() >= 1d - Math.Exp(-h / meanWait))
                    continue;

                rel.EggPendingSince = now;
                events.Add(new SimEvent { Type = SimEventType.EggDue, A = rel.A, B = rel.B, Tag = groups[Data.Get(rel.A).GroupIndex].Tag });
            }
        }

        /// <summary>Called by the host once the egg exists as a crew member.</summary>
        public KerbalState RegisterEgg(string eggName, Sex sex, string parentA, string parentB, double laidUT, double now)
        {
            var egg = Data.GetOrCreate(eggName, Config.BaseHappiness);
            egg.Sex = sex;
            egg.Stage = LifeStage.Egg;
            egg.StageStartUT = laidUT;
            egg.BirthUT = laidUT;
            egg.ParentA = parentA;
            egg.ParentB = parentB;
            egg.Eligible = false;

            var rel = Data.GetRelationship(parentA, parentB);
            if (rel != null)
            {
                rel.LastEggUT = laidUT;
                rel.EggPendingSince = -1d;
            }
            var a = Data.Get(parentA);
            var b = Data.Get(parentB);
            if (a != null)
                ApplyDef(a, ModifierIds.EggLaid, parentB, now);
            if (b != null)
                ApplyDef(b, ModifierIds.EggLaid, parentA, now);
            return egg;
        }

        public void CancelPendingEgg(string parentA, string parentB)
        {
            var rel = Data.GetRelationship(parentA, parentB);
            if (rel != null)
                rel.EggPendingSince = -1d;
        }

        private void Hatch(KerbalState k, double now, List<SimEvent> events)
        {
            k.Stage = LifeStage.Child;
            k.StageStartUT += Config.EggDuration;
            k.BirthUT = k.StageStartUT;
            k.Social = Config.CompanyHappiness;
            k.Happiness = k.Social;
            // The host keeps an egg's whereabouts up to date, so this is where it hatched.
            if (k.BirthBody != null && k.BirthBody != Config.HomeBody && Data.BirthBodies.Add(k.BirthBody))
                Award(k, RibbonIds.FirstBornPrefix + k.BirthBody, now, events);
            LinkToParent(k, k.ParentA, now);
            LinkToParent(k, k.ParentB, now);
            events.Add(new SimEvent
            {
                Type = SimEventType.Hatched,
                A = k.Name,
                Text = ShortName(k.Name) + " has hatched"
            });
        }

        private void LinkToParent(KerbalState child, string parentName, double now)
        {
            var parent = Data.Get(parentName);
            if (parent == null)
                return;
            var rel = Data.GetOrCreateRelationship(child.Name, parentName, now);
            rel.Family = true;
            rel.Strength = Math.Max(rel.Strength, Config.ParentChildStartStrength);
            ApplyDef(parent, ModifierIds.ChildHatched, child.Name, now);
        }

        private void GrowUp(KerbalState k, double now, List<SimEvent> events)
        {
            k.Stage = LifeStage.Adult;
            k.StageStartUT += Config.ChildDuration;
            var a = Data.Get(k.ParentA);
            var b = Data.Get(k.ParentB);
            if (a != null)
                ApplyDef(a, ModifierIds.ChildGrewUp, k.Name, now);
            if (b != null)
                ApplyDef(b, ModifierIds.ChildGrewUp, k.Name, now);
            events.Add(new SimEvent
            {
                Type = SimEventType.GrewUp,
                A = k.Name,
                Text = ShortName(k.Name) + " is now an adult"
            });
        }

        /// <summary>Adds a modifier, replacing any existing one with the same id.</summary>
        public void AddModifier(KerbalState k, string id, string text, double amount, double durationSeconds, double now, bool noFade,
            string subject = null)
        {
            if (k == null || k.Stage == LifeStage.Egg)
                return;
            RemoveModifier(k, id);
            k.Modifiers.Add(new ActiveModifier
            {
                Id = id,
                Text = text,
                Subject = subject,
                Amount = amount,
                StartUT = now,
                ExpiresUT = now + Math.Max(0d, durationSeconds),
                NoFade = noFade
            });
            k.AddLog(now, text, amount);
            RecomputeHappiness(k, now);
        }

        private static void RemoveModifier(KerbalState k, string id)
        {
            for (int i = k.Modifiers.Count - 1; i >= 0; --i)
            {
                if (k.Modifiers[i].Id == id)
                    k.Modifiers.RemoveAt(i);
            }
        }

        private void ApplyDef(KerbalState k, string id, string other, double now, double scale = 1d)
        {
            var def = Defs.Get(id);
            if (def == null || k == null)
                return;
            AddModifier(k, id, Format(def.Text, k.Name, other), def.Amount * scale, Config.Days(def.DurationDays), now, def.NoFade, other);
        }

        /// <summary>Applies one of the defined modifiers (KR_MODIFIER) to a Kerbal.</summary>
        public void ApplyModifier(string name, string modifierId, double now, string other = null)
        {
            ApplyDef(Data.Get(name), modifierId, other, now);
        }

        /// <summary>
        /// True if one of the two modifiers was applied for the same subject within the last day. Vessels
        /// bounce on landing and wobble across SOI borders; that should count once.
        /// </summary>
        private bool AppliedRecently(KerbalState k, string idA, string idB, string subject, double now)
        {
            for (int i = 0; i < k.Modifiers.Count; ++i)
            {
                var m = k.Modifiers[i];
                if ((m.Id == idA || m.Id == idB) && m.Subject == subject && now - m.StartUT < Config.SecondsPerDay)
                    return true;
            }
            return false;
        }

        public void OnEnteredSoi(string name, string body, bool isHome, double now)
        {
            var k = Data.Get(name);
            if (k == null || isHome || string.IsNullOrEmpty(body))
                return;
            if (AppliedRecently(k, ModifierIds.NewSoiFirst, ModifierIds.NewSoi, body, now))
                return;
            ApplyDef(k, k.VisitedBodies.Add(body) ? ModifierIds.NewSoiFirst : ModifierIds.NewSoi, body, now);
            Award(k, RibbonIds.VisitPrefix + body, now);
        }

        public void OnLanded(string name, string body, bool isHome, double now)
        {
            var k = Data.Get(name);
            if (k == null || isHome || string.IsNullOrEmpty(body))
                return;
            if (AppliedRecently(k, ModifierIds.LandedFirst, ModifierIds.Landed, body, now))
                return;
            k.VisitedBodies.Add(body);
            ApplyDef(k, k.LandedBodies.Add(body) ? ModifierIds.LandedFirst : ModifierIds.Landed, body, now);
            Award(k, RibbonIds.VisitPrefix + body, now);
            Award(k, RibbonIds.LandPrefix + body, now);
        }

        public void OnReturnedHome(string name, double now)
        {
            ApplyDef(Data.Get(name), ModifierIds.ReturnedHome, null, now);
        }

        public void OnLevelUp(string name, double now)
        {
            ApplyDef(Data.Get(name), ModifierIds.LevelUp, null, now);
        }

        /// <summary>Friends and family grieve in proportion to the bond; the Kerbal is then forgotten.</summary>
        public void OnKerbalDied(string name, double now)
        {
            if (Data.Get(name) == null)
                return;
            var rels = Data.RelationshipsOf(name);
            for (int i = 0; i < rels.Count; ++i)
            {
                var rel = rels[i];
                if (rel.Strength < 20d)
                    continue;
                var scale = 1d + rel.Strength / 50d;
                if (rel.IsCouple)
                    scale += 1d;
                var friend = Data.Get(rel.Other(name));
                ApplyDef(friend, ModifierIds.Grief, name, now, scale);
                if (rel.Strength >= RibbonIds.MournedBond)
                    Award(friend, RibbonIds.InMemory, now);
            }
            Data.Remove(name);
        }
    }
}
