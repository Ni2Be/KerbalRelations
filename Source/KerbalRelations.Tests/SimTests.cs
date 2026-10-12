using System;
using System.Collections.Generic;
using System.Linq;
using KerbalRelations.Core;
using Xunit;

namespace KerbalRelations.Tests
{
    /// <summary>Returns a fixed value, so tests decide whether random rolls succeed.</summary>
    internal class FixedRandom : IRandom
    {
        public double Value;

        public FixedRandom(double value)
        {
            Value = value;
        }

        public double NextDouble()
        {
            return Value;
        }
    }

    public class SimTests
    {
        private const double Day = 21600d;
        private const double Year = 9203400d;

        private readonly RelationsData _data = new RelationsData();
        private readonly SimConfig _config = new SimConfig { EnableEvents = false, EnableRibbons = false };
        private readonly Definitions _defs = new Definitions();
        private readonly FixedRandom _random = new FixedRandom(0.999999d);
        private readonly RelationsSim _sim;
        private double _now;

        public SimTests()
        {
            // Same compatibility for every pair keeps the arithmetic in the tests exact.
            _config.CompatibilityMin = 1d;
            _config.CompatibilityMax = 1d;
            _sim = new RelationsSim(_data, _config, _defs, _random);
            _sim.Tick(_now, new List<SimGroup>());
        }

        private KerbalState Add(string name, Sex sex)
        {
            var k = _data.GetOrCreate(name, _config.BaseHappiness);
            k.Sex = sex;
            return k;
        }

        private static SimGroup Group(params string[] members)
        {
            var group = new SimGroup();
            group.Members.AddRange(members);
            return group;
        }

        private List<SimEvent> Advance(double days, params SimGroup[] groups)
        {
            var events = new List<SimEvent>();
            // Tick once per day, as the game would at high warp.
            var end = _now + days * Day;
            while (_now < end - 1e-6)
            {
                _now = Math.Min(end, _now + Day);
                events.AddRange(_sim.Tick(_now, groups));
            }
            return events;
        }

        [Fact]
        public void New_kerbal_starts_at_base_happiness()
        {
            var k = Add("Jeb Kerman", Sex.Male);
            Assert.Equal(70d, k.Happiness, 6);
        }

        [Fact]
        public void Being_alone_too_long_sinks_happiness_by_the_lonely_penalty()
        {
            var k = Add("Jeb Kerman", Sex.Male);
            Advance(5, Group("Jeb Kerman"));
            Assert.Equal(70d, k.Happiness, 3);

            Advance(60, Group("Jeb Kerman"));
            Assert.Equal(50d, k.Happiness, 1);
        }

        [Fact]
        public void Happiness_sinks_gradually_not_instantly()
        {
            var k = Add("Jeb Kerman", Sex.Male);
            Advance(11, Group("Jeb Kerman"));
            Assert.InRange(k.Happiness, 55d, 69.9d);
        }

        [Fact]
        public void Company_without_a_bond_lifts_happiness_to_100()
        {
            _config.GrowthPerDay = 0d;
            var jeb = Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            Advance(40, Group("Jeb Kerman", "Bill Kerman"));
            Assert.Equal(100d, jeb.Happiness, 1);
            Assert.Equal(0d, _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength, 6);
        }

        [Fact]
        public void Bonds_between_vessels_stop_at_a_cap_that_sinks_with_distance()
        {
            foreach (var name in new[] { "Jeb Kerman", "Bill Kerman", "Val Kerman", "Bob Kerman", "Kim Kerman" })
                Add(name, Sex.Male);
            // Jeb and Bill share a vessel; Val is right next to it, Bob 1 km from her and 2 km from them,
            // and Kim is the far end of a chain of vessels.
            var group = Group("Jeb Kerman", "Bill Kerman", "Val Kerman", "Bob Kerman", "Kim Kerman");
            group.Places.AddRange(new[] { 0, 0, 1, 2, 3 });
            group.PlaceDistances = new double[4, 4];
            Action<int, int, double> set = (a, b, metres) => group.PlaceDistances[a, b] = group.PlaceDistances[b, a] = metres;
            set(0, 1, 0d);
            set(0, 2, 2000d);
            set(1, 2, 1000d);
            set(0, 3, 3500d);
            set(1, 3, 3500d);
            set(2, 3, double.PositiveInfinity);
            Func<string, string, double> bond = (a, b) => _data.GetRelationship(a + " Kerman", b + " Kerman").Strength;

            // Everybody gets to know everybody at the same speed.
            Advance(40, group);
            Assert.Equal(10d, bond("Jeb", "Bill"), 6);
            Assert.Equal(10d, bond("Jeb", "Val"), 6);
            Assert.Equal(10d, bond("Jeb", "Bob"), 6);
            Assert.Equal(10d, bond("Bob", "Kim"), 6);

            Advance(360, group);
            Assert.Equal(100d, bond("Jeb", "Bill"), 6);
            Assert.Equal(60d, bond("Jeb", "Val"), 6);
            Assert.Equal(60d, bond("Bill", "Val"), 6);
            Assert.Equal(20d, bond("Jeb", "Bob"), 6);
            Assert.Equal(40d, bond("Val", "Bob"), 6);
            // Further than the range, or at an unknown distance, counts as the edge of the range.
            Assert.Equal(20d, bond("Jeb", "Kim"), 6);
            Assert.Equal(20d, bond("Bob", "Kim"), 6);
        }

        [Fact]
        public void A_bond_above_the_cap_keeps_what_it_has()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Male);
            Advance(1, Group("Jeb Kerman", "Val Kerman"));
            var rel = _data.GetRelationship("Jeb Kerman", "Val Kerman");
            rel.Strength = 90d;

            var group = Group("Jeb Kerman", "Val Kerman");
            group.Places.AddRange(new[] { 0, 1 });
            group.PlaceDistances = new double[2, 2];
            Advance(100, group);
            Assert.Equal(90d, rel.Strength, 6);

            Advance(10, Group("Jeb Kerman", "Val Kerman"));
            Assert.Equal(92.5d, rel.Strength, 6);
        }

        [Fact]
        public void A_group_without_places_is_one_vessel()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            Advance(40, Group("Jeb Kerman", "Bill Kerman"));
            Assert.Equal(10d, _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength, 6);
        }

        [Fact]
        public void A_full_bond_allows_150()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            Advance(1, Group("Jeb Kerman", "Bill Kerman"));
            _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength = 100d;
            Advance(40, Group("Jeb Kerman", "Bill Kerman"));
            Assert.Equal(150d, jeb.Happiness, 1);
        }

        [Fact]
        public void Modifiers_stack_on_top_and_happiness_is_clamped_to_0_and_200()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            Advance(1, Group("Jeb Kerman", "Bill Kerman"));
            _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength = 100d;
            Advance(40, Group("Jeb Kerman", "Bill Kerman"));

            _sim.AddModifier(jeb, "Test", "A good day", 30d, 100 * Day, _now, false);
            Assert.Equal(180d, jeb.Happiness, 1);

            _sim.AddModifier(jeb, "Test2", "A great day", 80d, 100 * Day, _now, false);
            Assert.Equal(200d, jeb.Happiness, 6);

            _sim.AddModifier(jeb, "Test3", "A terrible day", -500d, 100 * Day, _now, false);
            Assert.Equal(0d, jeb.Happiness, 6);
        }

        [Fact]
        public void Modifiers_fade_out_and_expire()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            _sim.AddModifier(jeb, "Test", "Present", 10d, 20 * Day, _now, false);
            Assert.Equal(80d, jeb.Happiness, 6);

            // Fading over the last quarter: half-way through it, half the amount is left.
            Assert.Equal(5d, jeb.Modifiers[0].EffectiveAmount(_now + 17.5 * Day), 6);

            Advance(21, Group("Jeb Kerman"));
            Assert.Empty(jeb.Modifiers);
        }

        [Fact]
        public void A_modifier_with_the_same_id_replaces_the_old_one()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            _sim.AddModifier(jeb, "Test", "One", 10d, 20 * Day, _now, false);
            _sim.AddModifier(jeb, "Test", "Two", 10d, 20 * Day, _now, false);
            Assert.Single(jeb.Modifiers);
            Assert.Equal(80d, jeb.Happiness, 6);
        }

        [Fact]
        public void Kerbals_who_never_met_have_no_relationship()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Female);
            Advance(10, Group("Jeb Kerman"), Group("Val Kerman"));
            Assert.Null(_data.GetRelationship("Jeb Kerman", "Val Kerman"));
        }

        [Fact]
        public void Time_together_builds_a_relationship()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            Advance(100, Group("Jeb Kerman", "Bill Kerman"));
            Assert.Equal(25d, _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength, 3);
        }

        [Fact]
        public void A_large_time_step_gives_the_same_growth_as_many_small_ones()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            _sim.Tick(_now, new[] { Group("Jeb Kerman", "Bill Kerman") });
            _now += 100 * Day;
            _sim.Tick(_now, new[] { Group("Jeb Kerman", "Bill Kerman") });
            Assert.Equal(25d, _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength, 3);
        }

        [Fact]
        public void Relationships_decay_when_apart_after_a_grace_period()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            Advance(100, Group("Jeb Kerman", "Bill Kerman"));

            Advance(30, Group("Jeb Kerman"), Group("Bill Kerman"));
            Assert.Equal(25d, _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength, 3);

            Advance(100, Group("Jeb Kerman"), Group("Bill Kerman"));
            Assert.Equal(20d, _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength, 1);
        }

        [Fact]
        public void Relationships_are_frozen_at_home()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            Advance(100, Group("Jeb Kerman", "Bill Kerman"));

            var home = Group("Jeb Kerman", "Bill Kerman");
            home.AtHome = true;
            Advance(500, home);
            Assert.Equal(25d, _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength, 3);
            Assert.Equal(70d, _data.Get("Jeb Kerman").Happiness, 1);
        }

        [Fact]
        public void Opposite_sex_pair_becomes_a_couple_at_70()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Female);
            var group = Group("Jeb Kerman", "Val Kerman");
            Advance(1, group);
            var rel = _data.GetRelationship("Jeb Kerman", "Val Kerman");

            rel.Strength = 69d;
            Assert.DoesNotContain(Advance(1, group), e => e.Type == SimEventType.BecameCouple);
            Assert.False(rel.IsCouple);

            rel.Strength = 70d;
            Assert.Contains(Advance(1, group), e => e.Type == SimEventType.BecameCouple);
            Assert.True(rel.IsCouple);
            Assert.Equal("Val Kerman", _data.Get("Jeb Kerman").Partner);
            Assert.Equal("Jeb Kerman", _data.Get("Val Kerman").Partner);
            Assert.NotNull(_data.Get("Jeb Kerman").FindModifier(ModifierIds.BecameCouple));
        }

        [Fact]
        public void Same_sex_pairs_stay_friends()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            var group = Group("Jeb Kerman", "Bill Kerman");
            Advance(1, group);
            _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength = 95d;
            Advance(5, group);
            Assert.False(_data.GetRelationship("Jeb Kerman", "Bill Kerman").IsCouple);
        }

        [Fact]
        public void A_kerbal_has_only_one_partner_and_the_strongest_bond_wins()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Female);
            Add("Tana Kerman", Sex.Female);
            var group = Group("Jeb Kerman", "Val Kerman", "Tana Kerman");
            Advance(1, group);
            _data.GetRelationship("Jeb Kerman", "Val Kerman").Strength = 75d;
            _data.GetRelationship("Jeb Kerman", "Tana Kerman").Strength = 90d;
            Advance(1, group);
            Assert.Equal("Tana Kerman", _data.Get("Jeb Kerman").Partner);
            Assert.Null(_data.Get("Val Kerman").Partner);
        }

        [Fact]
        public void A_couple_breaks_up_below_50()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Female);
            var group = Group("Jeb Kerman", "Val Kerman");
            Advance(1, group);
            var rel = _data.GetRelationship("Jeb Kerman", "Val Kerman");
            rel.Strength = 80d;
            Advance(1, group);
            Assert.True(rel.IsCouple);

            _config.GrowthPerDay = 0d;
            rel.Strength = 40d;
            Assert.Contains(Advance(1, group), e => e.Type == SimEventType.BrokeUp);
            Assert.False(rel.IsCouple);
            Assert.Null(_data.Get("Jeb Kerman").Partner);
            Assert.Null(_data.Get("Val Kerman").Partner);
        }

        [Fact]
        public void A_kerbal_misses_an_absent_partner()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Female);
            var group = Group("Jeb Kerman", "Val Kerman");
            Advance(1, group);
            _data.GetRelationship("Jeb Kerman", "Val Kerman").Strength = 100d;
            Advance(1, group);

            Advance(20, Group("Jeb Kerman"), Group("Val Kerman"));
            Assert.Null(jeb.FindModifier(ModifierIds.MissesPartner));
            Advance(20, Group("Jeb Kerman"), Group("Val Kerman"));
            Assert.NotNull(jeb.FindModifier(ModifierIds.MissesPartner));

            Advance(2, group);
            Assert.Null(jeb.FindModifier(ModifierIds.MissesPartner));
        }

        private Relationship MakeCouple(double strength)
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Female);
            var group = Group("Jeb Kerman", "Val Kerman");
            Advance(1, group);
            var rel = _data.GetRelationship("Jeb Kerman", "Val Kerman");
            rel.Strength = Math.Max(strength, 70d);
            Advance(1, group);
            rel.Strength = strength;
            return rel;
        }

        [Fact]
        public void A_couple_over_85_with_a_nest_lays_an_egg()
        {
            var rel = MakeCouple(90d);
            var group = Group("Jeb Kerman", "Val Kerman");
            group.HasNest = true;
            _random.Value = 0d;
            var events = Advance(1, group);

            var due = Assert.Single(events, e => e.Type == SimEventType.EggDue);
            Assert.Equal("Jeb Kerman", due.A);
            Assert.Equal("Val Kerman", due.B);
            Assert.True(rel.EggPendingSince >= 0d);

            // No second egg while one is pending.
            Assert.DoesNotContain(Advance(5, group), e => e.Type == SimEventType.EggDue);
        }

        [Fact]
        public void No_egg_below_85_or_without_a_nest_or_when_disabled()
        {
            MakeCouple(80d);
            _config.GrowthPerDay = 0d;
            _random.Value = 0d;
            var nest = Group("Jeb Kerman", "Val Kerman");
            nest.HasNest = true;
            Assert.DoesNotContain(Advance(5, nest), e => e.Type == SimEventType.EggDue);

            _data.GetRelationship("Jeb Kerman", "Val Kerman").Strength = 95d;
            Assert.DoesNotContain(Advance(5, Group("Jeb Kerman", "Val Kerman")), e => e.Type == SimEventType.EggDue);

            _config.EnableReproduction = false;
            Assert.DoesNotContain(Advance(5, nest), e => e.Type == SimEventType.EggDue);
        }

        [Fact]
        public void Egg_status_names_what_is_in_the_way()
        {
            var rel = MakeCouple(80d);
            _config.GrowthPerDay = 0d;
            var noNest = Group("Jeb Kerman", "Val Kerman");
            var nest = Group("Jeb Kerman", "Val Kerman");
            nest.HasNest = true;

            Advance(1, nest);
            Assert.Equal(EggStatus.BondTooLow, _sim.GetEggStatus(rel, _now, new[] { nest }));

            rel.Strength = 95d;
            Advance(1, noNest);
            Assert.Equal(EggStatus.NoNest, _sim.GetEggStatus(rel, _now, new[] { noNest }));

            var home = Group("Jeb Kerman", "Val Kerman");
            home.AtHome = true;
            Advance(1, home);
            Assert.Equal(EggStatus.AtHome, _sim.GetEggStatus(rel, _now, new[] { home }));

            var jeb = Group("Jeb Kerman");
            var val = Group("Val Kerman");
            val.HasNest = true;
            Advance(1, jeb, val);
            Assert.Equal(EggStatus.Apart, _sim.GetEggStatus(rel, _now, new[] { jeb, val }));

            Advance(1, nest);
            Assert.Equal(EggStatus.Trying, _sim.GetEggStatus(rel, _now, new[] { nest }));

            _random.Value = 0d;
            Advance(1, nest);
            Assert.Equal(EggStatus.Pending, _sim.GetEggStatus(rel, _now, new[] { nest }));

            _sim.RegisterEgg("Eggbert Kerman", Sex.Male, "Jeb Kerman", "Val Kerman", _now, _now);
            Assert.Equal(EggStatus.Cooldown, _sim.GetEggStatus(rel, _now, new[] { nest }));

            _config.EnableReproduction = false;
            Assert.Equal(EggStatus.Disabled, _sim.GetEggStatus(rel, _now, new[] { nest }));
            Assert.Equal(EggStatus.NotACouple, _sim.GetEggStatus(_data.GetRelationship("Jeb Kerman", "Eggbert Kerman"), _now, new[] { nest }));
        }

        [Fact]
        public void Egg_hatches_after_nine_months_and_child_grows_up_after_fourteen_years()
        {
            var rel = MakeCouple(95d);
            var group = Group("Jeb Kerman", "Val Kerman", "Eggbert Kerman");
            var laid = _now;
            var egg = _sim.RegisterEgg("Eggbert Kerman", Sex.Male, "Jeb Kerman", "Val Kerman", laid, _now);

            Assert.Equal(LifeStage.Egg, egg.Stage);
            Assert.Equal(laid, rel.LastEggUT, 6);
            Assert.NotNull(_data.Get("Val Kerman").FindModifier(ModifierIds.EggLaid));

            var nineMonths = 0.75d * Year / Day;
            Assert.DoesNotContain(Advance(nineMonths - 2, group), e => e.Type == SimEventType.Hatched);
            Assert.Equal(LifeStage.Egg, egg.Stage);
            // An egg is not company and has no mood.
            Assert.Null(_data.GetRelationship("Jeb Kerman", "Eggbert Kerman"));

            Assert.Contains(Advance(3, group), e => e.Type == SimEventType.Hatched);
            Assert.Equal(LifeStage.Child, egg.Stage);
            var withParent = _data.GetRelationship("Jeb Kerman", "Eggbert Kerman");
            Assert.True(withParent.Family);
            Assert.True(withParent.Strength >= 50d);
            Assert.NotNull(_data.Get("Jeb Kerman").FindModifier(ModifierIds.ChildHatched));

            _sim.Tick(_now, new[] { group });
            _now = laid + (0.75d + 14d) * Year - 2 * Day;
            Assert.DoesNotContain(_sim.Tick(_now, new[] { group }), e => e.Type == SimEventType.GrewUp);
            Assert.Equal(LifeStage.Child, egg.Stage);

            Assert.Contains(Advance(3, group), e => e.Type == SimEventType.GrewUp);
            Assert.Equal(LifeStage.Adult, egg.Stage);
        }

        [Fact]
        public void Grow_up_speed_shortens_both_stages()
        {
            MakeCouple(95d);
            _config.GrowUpSpeed = 10d;
            var group = Group("Jeb Kerman", "Val Kerman", "Eggbert Kerman");
            var egg = _sim.RegisterEgg("Eggbert Kerman", Sex.Male, "Jeb Kerman", "Val Kerman", _now, _now);
            Advance(0.075d * Year / Day + 2, group);
            Assert.Equal(LifeStage.Child, egg.Stage);
        }

        [Fact]
        public void Family_never_becomes_a_couple()
        {
            MakeCouple(95d);
            var group = Group("Jeb Kerman", "Val Kerman", "Kid Kerman");
            var kid = _sim.RegisterEgg("Kid Kerman", Sex.Female, "Jeb Kerman", "Val Kerman", _now, _now);
            kid.Stage = LifeStage.Adult;
            kid.Eligible = true;
            // Jeb is single again, and very close to his daughter.
            var couple = _data.GetRelationship("Jeb Kerman", "Val Kerman");
            couple.IsCouple = false;
            _data.Get("Jeb Kerman").Partner = null;
            _data.Get("Val Kerman").Partner = null;
            couple.Strength = 0d;
            _config.GrowthPerDay = 0d;

            Advance(1, group);
            _data.GetRelationship("Jeb Kerman", "Kid Kerman").Strength = 100d;
            Advance(3, group);
            Assert.False(_data.GetRelationship("Jeb Kerman", "Kid Kerman").IsCouple);
            Assert.Null(kid.Partner);
        }

        [Fact]
        public void Ineligible_kerbals_do_not_pair_up()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Tourist Kerman", Sex.Female).Eligible = false;
            var group = Group("Jeb Kerman", "Tourist Kerman");
            Advance(1, group);
            _data.GetRelationship("Jeb Kerman", "Tourist Kerman").Strength = 95d;
            Advance(3, group);
            Assert.Null(_data.Get("Jeb Kerman").Partner);
        }

        [Fact]
        public void Milestones_are_bigger_the_first_time()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            _sim.OnEnteredSoi("Jeb Kerman", "Mun", false, _now);
            Assert.Equal(20d, jeb.FindModifier(ModifierIds.NewSoiFirst).Amount, 6);
            Assert.Equal("Reached Mun for the first time", jeb.FindModifier(ModifierIds.NewSoiFirst).Text);

            _sim.OnEnteredSoi("Jeb Kerman", "Mun", false, _now + 5 * Day);
            Assert.Equal(8d, jeb.FindModifier(ModifierIds.NewSoi).Amount, 6);

            _sim.OnLanded("Jeb Kerman", "Mun", false, _now);
            Assert.NotNull(jeb.FindModifier(ModifierIds.LandedFirst));
            _sim.OnLanded("Jeb Kerman", "Mun", false, _now + 5 * Day);
            Assert.NotNull(jeb.FindModifier(ModifierIds.Landed));
        }

        [Fact]
        public void A_bouncing_lander_counts_as_one_landing()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            _sim.OnLanded("Jeb Kerman", "Mun", false, _now);
            _sim.OnLanded("Jeb Kerman", "Mun", false, _now + 10d);
            _sim.OnLanded("Jeb Kerman", "Mun", false, _now + 20d);
            Assert.Single(jeb.Modifiers);
            Assert.Single(jeb.Log);

            // A different body is a new landing; the fresh bonus replaces the older one of the same kind.
            _sim.OnLanded("Jeb Kerman", "Minmus", false, _now + 30d);
            Assert.Equal("Minmus", Assert.Single(jeb.Modifiers).Subject);
            Assert.Equal(2, jeb.Log.Count);
        }

        [Fact]
        public void The_home_world_is_not_a_milestone()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            _sim.OnEnteredSoi("Jeb Kerman", "Kerbin", true, _now);
            _sim.OnLanded("Jeb Kerman", "Kerbin", true, _now);
            Assert.Empty(jeb.Modifiers);
        }

        [Fact]
        public void Friends_grieve_and_the_dead_are_forgotten()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            Add("Bill Kerman", Sex.Male);
            Advance(1, Group("Jeb Kerman", "Bill Kerman"));
            _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength = 100d;

            _sim.OnKerbalDied("Bill Kerman", _now);
            Assert.Equal(-30d, jeb.FindModifier(ModifierIds.Grief).Amount, 6);
            Assert.Null(_data.Get("Bill Kerman"));
            Assert.Empty(_data.Relationships);
        }

        [Fact]
        public void Random_events_apply_a_modifier_and_report_the_text()
        {
            _config.EnableEvents = true;
            _defs.Events.Add(new EventDef
            {
                Id = "Fight",
                Text = "Had a fight with <<2>> over which planet looks nicest",
                Amount = -5d,
                DurationDays = 10d,
                Condition = EventCondition.Company,
                RelationDelta = -3d,
                Mutual = true
            });
            var jeb = Add("Jeb Kerman", Sex.Male);
            var bill = Add("Bill Kerman", Sex.Male);
            var group = Group("Jeb Kerman", "Bill Kerman");
            _config.GrowthPerDay = 0d;
            Advance(1, group);
            _data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength = 50d;

            _random.Value = 0d;
            var events = _sim.Tick(_now += Day, new[] { group });
            _random.Value = 0.999999d;

            Assert.Contains(events, e => e.Type == SimEventType.RandomEvent
                && e.A == "Jeb Kerman" && e.Text == "Had a fight with Bill over which planet looks nicest");
            Assert.Equal(-5d, jeb.FindModifier("Fight").Amount, 6);
            Assert.Equal(-5d, bill.FindModifier("Fight").Amount, 6);
            Assert.True(_data.GetRelationship("Jeb Kerman", "Bill Kerman").Strength < 50d);
            Assert.Contains(jeb.Log, l => l.Text.Contains("fight"));
        }

        [Fact]
        public void Events_respect_their_conditions()
        {
            _config.EnableEvents = true;
            _defs.Events.Add(new EventDef { Id = "Company", Text = "x", Condition = EventCondition.Company });
            _defs.Events.Add(new EventDef { Id = "Couple", Text = "x", Condition = EventCondition.Couple });
            _defs.Events.Add(new EventDef { Id = "ChildOnly", Text = "x", Stage = StageFilter.Child });
            var jeb = Add("Jeb Kerman", Sex.Male);
            _random.Value = 0d;
            Advance(5, Group("Jeb Kerman"));
            Assert.Empty(jeb.Modifiers);

            _defs.Events.Add(new EventDef { Id = "Alone", Text = "Talked to the plants", Amount = 2d, Condition = EventCondition.Alone });
            Advance(1, Group("Jeb Kerman"));
            Assert.NotNull(jeb.FindModifier("Alone"));
        }

        [Fact]
        public void Disabled_events_never_fire()
        {
            _defs.Events.Add(new EventDef { Id = "Any", Text = "x", Amount = 5d });
            var jeb = Add("Jeb Kerman", Sex.Male);
            _random.Value = 0d;
            Advance(50, Group("Jeb Kerman"));
            Assert.Empty(jeb.Modifiers);
        }

        [Fact]
        public void State_survives_a_save_and_load()
        {
            var rel = MakeCouple(95d);
            _sim.RegisterEgg("Eggbert Kerman", Sex.Female, "Jeb Kerman", "Val Kerman", _now, _now);
            var jeb = _data.Get("Jeb Kerman");
            _sim.OnLanded("Jeb Kerman", "Mun", false, _now);
            jeb.LastBaseXp = 12.5d;
            rel.EggPendingSince = 123d;

            var loaded = new RelationsData();
            loaded.Load(_data.Save());

            Assert.Equal(_data.LastUT, loaded.LastUT, 6);
            Assert.Equal(3, loaded.Kerbals.Count);
            var jeb2 = loaded.Get("Jeb Kerman");
            Assert.Equal(jeb.Happiness, jeb2.Happiness, 9);
            Assert.Equal(jeb.Social, jeb2.Social, 9);
            Assert.Equal("Val Kerman", jeb2.Partner);
            Assert.Equal(12.5d, jeb2.LastBaseXp, 9);
            Assert.Contains("Mun", jeb2.LandedBodies);
            Assert.Contains("Mun", jeb2.VisitedBodies);
            Assert.Equal(jeb.Modifiers.Count, jeb2.Modifiers.Count);
            Assert.Equal(jeb.Modifiers.Select(m => m.Id), jeb2.Modifiers.Select(m => m.Id));
            Assert.Equal(jeb.Log.Select(l => l.Text), jeb2.Log.Select(l => l.Text));

            var egg = loaded.Get("Eggbert Kerman");
            Assert.Equal(LifeStage.Egg, egg.Stage);
            Assert.Equal(Sex.Female, egg.Sex);
            Assert.Equal("Jeb Kerman", egg.ParentA);
            Assert.Equal("Val Kerman", egg.ParentB);

            var rel2 = loaded.GetRelationship("Val Kerman", "Jeb Kerman");
            Assert.True(rel2.IsCouple);
            Assert.Equal(95d, rel2.Strength, 9);
            Assert.Equal(rel.LastEggUT, rel2.LastEggUT, 6);
            Assert.Equal(123d, rel2.EggPendingSince, 6);
        }

        [Fact]
        public void Loading_drops_dangling_partner_links()
        {
            MakeCouple(95d);
            var node = _data.Save();
            node.Nodes.RemoveAll(n => n.Name == "RELATIONSHIP");
            var loaded = new RelationsData();
            loaded.Load(node);
            Assert.Null(loaded.Get("Jeb Kerman").Partner);
            Assert.Null(loaded.Get("Val Kerman").Partner);
        }

        [Fact]
        public void Renaming_keeps_relationships_and_links()
        {
            MakeCouple(95d);
            _sim.RegisterEgg("Eggbert Kerman", Sex.Male, "Jeb Kerman", "Val Kerman", _now, _now);
            _data.Rename("Jeb Kerman", "Zed Kerman");

            Assert.Null(_data.Get("Jeb Kerman"));
            Assert.Equal("Val Kerman", _data.Get("Zed Kerman").Partner);
            Assert.Equal("Zed Kerman", _data.Get("Val Kerman").Partner);
            Assert.Equal("Zed Kerman", _data.Get("Eggbert Kerman").ParentA);
            Assert.True(_data.GetRelationship("Zed Kerman", "Val Kerman").IsCouple);
            Assert.Null(_data.GetRelationship("Jeb Kerman", "Val Kerman"));
        }

        [Fact]
        public void Time_going_backwards_does_not_simulate()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            Advance(5, Group("Jeb Kerman"));
            Assert.Empty(_sim.Tick(_now - 100 * Day, new[] { Group("Jeb Kerman") }));
            Assert.Equal(_now - 100 * Day, _data.LastUT, 6);
            Assert.Equal(70d, jeb.Happiness, 3);
        }
    }
}
