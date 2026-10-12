using System;
using System.Collections.Generic;
using System.Linq;
using KerbalRelations.Core;
using Xunit;

namespace KerbalRelations.Tests
{
    /// <summary>Ribbons, ages, birthplaces and mission guests.</summary>
    public class RibbonTests
    {
        private const double Day = 21600d;
        private const double Year = 9203400d;

        private readonly RelationsData _data = new RelationsData();
        private readonly SimConfig _config = new SimConfig { EnableEvents = false, CompatibilityMin = 1d, CompatibilityMax = 1d };
        private readonly Definitions _defs = new Definitions();
        private readonly RelationsSim _sim;
        private double _now;

        public RibbonTests()
        {
            _sim = new RelationsSim(_data, _config, _defs, new FixedRandom(0.999999d));
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

        private static SimGroup Home(params string[] members)
        {
            var group = Group(members);
            group.AtHome = true;
            return group;
        }

        private List<SimEvent> Advance(double days, params SimGroup[] groups)
        {
            var events = new List<SimEvent>();
            var end = _now + days * Day;
            while (_now < end - 1e-6)
            {
                _now = Math.Min(end, _now + Day);
                events.AddRange(_sim.Tick(_now, groups));
            }
            return events;
        }

        private static List<string> RibbonsIn(List<SimEvent> events, string kerbal)
        {
            return events.Where(e => e.Type == SimEventType.Ribbon && e.A == kerbal).Select(e => e.B).ToList();
        }

        [Fact]
        public void A_growing_bond_earns_the_friendship_ribbons()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            Add("Bob Kerman", Sex.Male);
            var group = Group("Jeb Kerman", "Bob Kerman");
            Assert.Empty(RibbonsIn(Advance(2, group), "Jeb Kerman"));

            _data.GetRelationship("Jeb Kerman", "Bob Kerman").Strength = 49.9d;
            var events = Advance(1, group);
            Assert.Equal(new[] { RibbonIds.FirstFriend }, RibbonsIn(events, "Jeb Kerman"));
            Assert.Equal(new[] { RibbonIds.FirstFriend }, RibbonsIn(events, "Bob Kerman"));
            Assert.Contains("First Friend", events.First(e => e.Type == SimEventType.Ribbon).Text);
            Assert.Equal(5d, jeb.FindModifier(ModifierIds.RibbonEarned).Amount);

            _data.GetRelationship("Jeb Kerman", "Bob Kerman").Strength = 99.9d;
            Assert.Equal(new[] { RibbonIds.BestFriends }, RibbonsIn(Advance(1, group), "Jeb Kerman"));

            // Once only.
            Assert.Empty(RibbonsIn(Advance(5, group), "Jeb Kerman"));
            Assert.Equal(2, jeb.Ribbons.Count);
        }

        [Fact]
        public void What_happened_before_ribbons_existed_is_caught_up_on_quietly()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            jeb.VisitedBodies.Add("Mun");
            jeb.VisitedBodies.Add("Minmus");
            jeb.LandedBodies.Add("Mun");
            jeb.Level = 5;

            var events = Advance(1, Group("Jeb Kerman"));
            Assert.Empty(events);
            Assert.Null(jeb.FindModifier(ModifierIds.RibbonEarned));
            Assert.True(jeb.HasRibbon("Visit:Mun"));
            Assert.True(jeb.HasRibbon("Visit:Minmus"));
            Assert.True(jeb.HasRibbon("Land:Mun"));
            Assert.True(jeb.HasRibbon(RibbonIds.Seasoned));
            Assert.True(jeb.HasRibbon(RibbonIds.Veteran));
            Assert.False(jeb.HasRibbon(RibbonIds.Explorer));
        }

        [Fact]
        public void Landing_somewhere_earns_the_ribbons_of_that_body()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            var group = Group("Jeb Kerman");
            Advance(1, group);

            _sim.OnLanded("Jeb Kerman", "Duna", false, _now);
            Assert.Equal(new[] { "Visit:Duna", "Land:Duna" }, RibbonsIn(Advance(1, group), "Jeb Kerman"));
            Assert.Equal("Duna Lander", _defs.ResolveRibbon("Land:Duna").Name);

            _sim.OnEnteredSoi("Jeb Kerman", "Ike", false, _now);
            _sim.OnEnteredSoi("Jeb Kerman", "Mun", false, _now);
            var events = Advance(1, group);
            Assert.Contains(RibbonIds.Explorer, RibbonsIn(events, "Jeb Kerman"));
            Assert.Equal(4, jeb.Ribbons.Count(r => r.Id.StartsWith("Visit:") || r.Id.StartsWith("Land:")));
        }

        [Fact]
        public void A_family_collects_its_ribbons()
        {
            Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Female);
            var parents = Group("Jeb Kerman", "Val Kerman");
            Advance(1, parents);
            _data.GetRelationship("Jeb Kerman", "Val Kerman").Strength = 95d;
            var events = Advance(1, parents);
            Assert.Contains(RibbonIds.Sweetheart, RibbonsIn(events, "Val Kerman"));

            var egg = _sim.RegisterEgg("Eggbert Kerman", Sex.Male, "Jeb Kerman", "Val Kerman", _now, _now);
            egg.BirthBody = "Minmus";
            egg.BirthPlace = "on Minmus, aboard Hab One";
            var family = Group("Jeb Kerman", "Val Kerman", "Eggbert Kerman");
            Assert.Empty(RibbonsIn(Advance(10, family), "Eggbert Kerman"));
            Assert.False(_data.Get("Jeb Kerman").HasRibbon(RibbonIds.Parent));

            events = Advance(0.75d * Year / Day, family);
            Assert.Equal(LifeStage.Child, egg.Stage);
            Assert.Contains(RibbonIds.Parent, RibbonsIn(events, "Jeb Kerman"));
            Assert.Contains(RibbonIds.Parent, RibbonsIn(events, "Val Kerman"));
            Assert.Contains(RibbonIds.SpaceBorn, RibbonsIn(events, "Eggbert Kerman"));
            Assert.Contains("FirstBorn:Minmus", RibbonsIn(events, "Eggbert Kerman"));
            Assert.Equal("First Born of Minmus", _defs.ResolveRibbon("FirstBorn:Minmus").Name);

            // The second child there is not the first.
            var second = _sim.RegisterEgg("Shelly Kerman", Sex.Female, "Jeb Kerman", "Val Kerman", _now, _now);
            second.BirthBody = "Minmus";
            family.Members.Add("Shelly Kerman");
            Advance(0.75d * Year / Day + 2, family);
            Assert.Equal(LifeStage.Child, second.Stage);
            Assert.True(second.HasRibbon(RibbonIds.SpaceBorn));
            Assert.False(second.HasRibbon("FirstBorn:Minmus"));

            _now = egg.StageStartUT + 14d * Year - Day;
            _sim.Tick(_now, new[] { family });
            events = Advance(3, family);
            Assert.Equal(LifeStage.Adult, egg.Stage);
            Assert.Contains(RibbonIds.CameOfAge, RibbonsIn(events, "Eggbert Kerman"));
            Assert.Contains(RibbonIds.ProudParent, RibbonsIn(events, "Jeb Kerman"));
        }

        [Fact]
        public void Hired_kerbals_get_an_age_and_were_born_at_home()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            Assert.Equal(-1d, _sim.AgeYears(jeb, _now));
            Advance(1, Home("Jeb Kerman"));

            Assert.Equal("on Kerbin", jeb.BirthPlace);
            Assert.Equal("Kerbin", jeb.BirthBody);
            var age = _sim.AgeYears(jeb, _now);
            Assert.InRange(age, 18d, 45.01d);

            _now += 3d * Year;
            Assert.Equal(age + 3d, _sim.AgeYears(jeb, _now), 6);

            // The same Kerbal is always the same age.
            var other = new RelationsData();
            var sim = new RelationsSim(other, _config, _defs, new FixedRandom(0.5d));
            sim.Tick(0d, new List<SimGroup>());
            sim.Tick(Day, new[] { Home("Jeb Kerman") });
            Assert.Equal(age, sim.AgeYears(other.Get("Jeb Kerman"), Day), 2);
        }

        [Fact]
        public void A_child_comes_of_age_at_fourteen_whatever_the_growing_up_speed()
        {
            _config.GrowUpSpeed = 7d;
            Add("Jeb Kerman", Sex.Male);
            Add("Val Kerman", Sex.Female);
            var egg = _sim.RegisterEgg("Eggbert Kerman", Sex.Male, "Jeb Kerman", "Val Kerman", _now, _now);
            var family = Group("Jeb Kerman", "Val Kerman", "Eggbert Kerman");
            Assert.Equal(-1d, _sim.AgeYears(egg, _now));

            Advance(0.75d * Year / 7d / Day + 1, family);
            Assert.Equal(LifeStage.Child, egg.Stage);
            Assert.InRange(_sim.AgeYears(egg, _now), 0d, 0.1d);
            Assert.Null(egg.BirthPlace);

            _now = egg.StageStartUT + Year;
            Assert.Equal(7d, _sim.AgeYears(egg, _now), 6);

            _now = egg.StageStartUT + 2d * Year - Day;
            _sim.Tick(_now, new[] { family });
            Advance(2, family);
            Assert.Equal(LifeStage.Adult, egg.Stage);
            Assert.InRange(_sim.AgeYears(egg, _now), 14d, 14.01d);
            _now += 2d * Year;
            Assert.InRange(_sim.AgeYears(egg, _now), 16d, 16.01d);
        }

        [Fact]
        public void Mission_guests_stay_as_they_are_and_collect_nothing()
        {
            Add("Jeb Kerman", Sex.Male);
            var child = _sim.RegisterVisitor("Pip Kerman", Sex.Female, LifeStage.Child, 7d, _now);
            var egg = _sim.RegisterVisitor("Dot Kerman", Sex.Male, LifeStage.Egg, 0d, _now);
            Assert.Equal(7d, _sim.AgeYears(child, _now), 6);

            var group = Group("Jeb Kerman", "Pip Kerman", "Dot Kerman");
            _sim.OnLanded("Pip Kerman", "Mun", false, _now);
            Advance(20, group);
            _now += 20d * Year;
            Advance(2, group);

            Assert.Equal(LifeStage.Child, child.Stage);
            Assert.Equal(LifeStage.Egg, egg.Stage);
            Assert.Empty(child.Ribbons);
            Assert.Empty(egg.Ribbons);
            // They are company all the same.
            Assert.NotNull(_data.GetRelationship("Jeb Kerman", "Pip Kerman"));
            Assert.Null(_data.Get("Jeb Kerman").Partner);
        }

        [Fact]
        public void Coming_home_after_a_long_time_and_meeting_again_are_remembered()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            var val = Add("Val Kerman", Sex.Female);
            var together = Group("Jeb Kerman", "Val Kerman");
            Advance(1, together);
            _data.GetRelationship("Jeb Kerman", "Val Kerman").Strength = 100d;
            Advance(1, together);
            Assert.Equal("Val Kerman", jeb.Partner);
            Assert.True(jeb.AwaySince >= 0d);

            // Val goes home, Jeb stays out for more than a year.
            _config.DecayPerDay = 0d;
            Advance(5, Group("Jeb Kerman"), Home("Val Kerman"));
            Assert.Equal(-1d, val.AwaySince);
            _now += 1.2d * Year;
            Advance(2, Group("Jeb Kerman"), Home("Val Kerman"));
            Assert.False(jeb.HasRibbon(RibbonIds.Homecoming));

            var events = Advance(1, Home("Jeb Kerman", "Val Kerman"));
            Assert.Contains(RibbonIds.Homecoming, RibbonsIn(events, "Jeb Kerman"));
            Assert.Contains(RibbonIds.Reunited, RibbonsIn(events, "Jeb Kerman"));
            Assert.Contains(RibbonIds.Reunited, RibbonsIn(events, "Val Kerman"));
            Assert.False(val.HasRibbon(RibbonIds.Homecoming));
            Assert.Equal(-1d, jeb.AwaySince);
        }

        [Fact]
        public void Ribbons_can_be_switched_off()
        {
            _config.EnableRibbons = false;
            var jeb = Add("Jeb Kerman", Sex.Male);
            Add("Bob Kerman", Sex.Male);
            var group = Group("Jeb Kerman", "Bob Kerman");
            Advance(1, group);
            _data.GetRelationship("Jeb Kerman", "Bob Kerman").Strength = 100d;
            _sim.OnLanded("Jeb Kerman", "Mun", false, _now);
            Assert.DoesNotContain(Advance(3, group), e => e.Type == SimEventType.Ribbon);
            Assert.Empty(jeb.Ribbons);
        }

        [Fact]
        public void New_state_survives_a_save_and_load()
        {
            var jeb = Add("Jeb Kerman", Sex.Male);
            var group = Group("Jeb Kerman");
            Advance(1, group);
            _sim.OnLanded("Jeb Kerman", "Mun", false, _now);
            Advance(1, group);
            _sim.RegisterVisitor("Pip Kerman", Sex.Female, LifeStage.Child, 9d, _now);
            _data.BirthBodies.Add("Minmus");

            var loaded = new RelationsData();
            loaded.Load(_data.Save());

            var copy = loaded.Get("Jeb Kerman");
            Assert.Equal(jeb.Ribbons.Select(r => r.Id), copy.Ribbons.Select(r => r.Id));
            Assert.Equal(jeb.Ribbons[0].UT, copy.Ribbons[0].UT, 6);
            Assert.Equal("on Kerbin", copy.BirthPlace);
            Assert.Equal("Kerbin", copy.BirthBody);
            Assert.Equal(jeb.BirthUT, copy.BirthUT, 6);
            Assert.Equal(jeb.AwaySince, copy.AwaySince, 6);
            Assert.False(copy.Visitor);
            Assert.True(loaded.Get("Pip Kerman").Visitor);
            Assert.True(loaded.RibbonsSeeded);
            Assert.Contains("Minmus", loaded.BirthBodies);
        }

        [Fact]
        public void Every_ribbon_has_a_name_a_description_and_a_picture()
        {
            var ribbons = Ribbons.BuiltIn();
            Assert.True(ribbons.Count >= 25);
            Assert.Equal(ribbons.Count, ribbons.Select(r => r.Id).Distinct().Count());
            Assert.Equal(ribbons.Count, ribbons.Select(r => r.Name).Distinct().Count());
            Assert.Equal(ribbons.Count, ribbons.Select(r => r.Stripes).Distinct().Count());
            foreach (var ribbon in ribbons.Concat(new[] { "Visit:Mun", "Land:Mun", "FirstBorn:Mun" }.Select(Ribbons.ForBody)))
            {
                Assert.False(string.IsNullOrEmpty(ribbon.Name), ribbon.Id);
                Assert.False(string.IsNullOrEmpty(ribbon.Description), ribbon.Id);
                var stripes = Ribbons.ParseStripes(ribbon.Stripes);
                Assert.True(stripes != null && stripes.Count >= 3, ribbon.Id + ": " + ribbon.Stripes);
            }

            // Bodies differ in colour, and a visit differs from a landing.
            Assert.NotEqual(Ribbons.ForBody("Visit:Mun").Stripes, Ribbons.ForBody("Visit:Duna").Stripes);
            Assert.NotEqual(Ribbons.ForBody("Visit:Mun").Stripes, Ribbons.ForBody("Land:Mun").Stripes);
            Assert.Null(Ribbons.ForBody("Nonsense"));
            Assert.Null(Ribbons.ParseStripes("12345:1"));
            Assert.Null(Ribbons.ParseStripes("ffffff:0"));
            Assert.Equal("Whatever", new Definitions().ResolveRibbon("Whatever").Name);
        }
    }
}
