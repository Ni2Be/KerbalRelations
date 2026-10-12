using System.Collections.Generic;

namespace KerbalRelations.Core
{
    /// <summary>A named happiness modifier. In texts, &lt;&lt;1&gt;&gt; is the Kerbal and &lt;&lt;2&gt;&gt; the other Kerbal or the body.</summary>
    public class ModifierDef
    {
        public string Id;
        public string Text;
        public double Amount;
        public double DurationDays;
        public bool NoFade;

        public ModifierDef()
        {
        }

        public ModifierDef(string id, string text, double amount, double durationDays, bool noFade = false)
        {
            Id = id;
            Text = text;
            Amount = amount;
            DurationDays = durationDays;
            NoFade = noFade;
        }
    }

    public enum EventCondition
    {
        /// <summary>Can happen anywhere, including at the KSC.</summary>
        Any,
        /// <summary>Anywhere but the KSC.</summary>
        Away,
        /// <summary>The only Kerbal around, away from home.</summary>
        Alone,
        /// <summary>At least one other Kerbal present; &lt;&lt;2&gt;&gt; is one of them.</summary>
        Company,
        /// <summary>Partner present; &lt;&lt;2&gt;&gt; is the partner.</summary>
        Couple,
        /// <summary>Own egg or child present; &lt;&lt;2&gt;&gt; is that child.</summary>
        Parent
    }

    public enum StageFilter
    {
        Any,
        Adult,
        Child
    }

    public class EventDef
    {
        public string Id;
        public string Text;
        public double Amount;
        public double DurationDays = 10d;
        public EventCondition Condition = EventCondition.Any;
        public StageFilter Stage = StageFilter.Any;
        /// <summary>Change to the relationship with the other Kerbal.</summary>
        public double RelationDelta;
        /// <summary>The other Kerbal gets the same modifier.</summary>
        public bool Mutual;
        public double Weight = 1d;
    }

    /// <summary>Ids of the modifiers the simulation applies itself.</summary>
    public static class ModifierIds
    {
        public const string NewSoiFirst = "NewSoiFirst";
        public const string NewSoi = "NewSoi";
        public const string LandedFirst = "LandedFirst";
        public const string Landed = "Landed";
        public const string ReturnedHome = "ReturnedHome";
        public const string LevelUp = "LevelUp";
        public const string BecameCouple = "BecameCouple";
        public const string BrokeUp = "BrokeUp";
        public const string EggLaid = "EggLaid";
        public const string ChildHatched = "ChildHatched";
        public const string ChildGrewUp = "ChildGrewUp";
        public const string Grief = "Grief";
        public const string MissesPartner = "MissesPartner";
        public const string RibbonEarned = "RibbonEarned";
        public const string MissionDone = "MissionDone";
    }

    public class Definitions
    {
        public readonly Dictionary<string, ModifierDef> Modifiers = new Dictionary<string, ModifierDef>();
        public readonly List<EventDef> Events = new List<EventDef>();
        public readonly Dictionary<string, RibbonDef> Ribbons = new Dictionary<string, RibbonDef>();
        /// <summary>Ribbon ids in display order.</summary>
        public readonly List<string> RibbonOrder = new List<string>();

        public Definitions()
        {
            // Built-in defaults; config nodes with the same id replace them.
            Set(new ModifierDef(ModifierIds.NewSoiFirst, "Reached <<2>> for the first time", 20d, 30d));
            Set(new ModifierDef(ModifierIds.NewSoi, "Arrived at <<2>>", 8d, 10d));
            Set(new ModifierDef(ModifierIds.LandedFirst, "First landing on <<2>>", 25d, 40d));
            Set(new ModifierDef(ModifierIds.Landed, "Landed on <<2>>", 10d, 15d));
            Set(new ModifierDef(ModifierIds.ReturnedHome, "Back home", 20d, 30d));
            Set(new ModifierDef(ModifierIds.LevelUp, "Got promoted", 10d, 20d));
            Set(new ModifierDef(ModifierIds.BecameCouple, "Fell in love with <<2>>", 15d, 60d));
            Set(new ModifierDef(ModifierIds.BrokeUp, "Broke up with <<2>>", -20d, 60d));
            Set(new ModifierDef(ModifierIds.EggLaid, "Laid an egg with <<2>>", 20d, 60d));
            Set(new ModifierDef(ModifierIds.ChildHatched, "<<2>> hatched", 25d, 90d));
            Set(new ModifierDef(ModifierIds.ChildGrewUp, "<<2>> is all grown up", 15d, 60d));
            Set(new ModifierDef(ModifierIds.Grief, "Mourning <<2>>", -10d, 90d));
            Set(new ModifierDef(ModifierIds.MissesPartner, "Misses <<2>>", -10d, 1d, true));
            Set(new ModifierDef(ModifierIds.RibbonEarned, "Earned the <<2>> ribbon", 5d, 10d));
            Set(new ModifierDef(ModifierIds.MissionDone, "Proud of a mission well done", 12d, 30d));

            foreach (var ribbon in Core.Ribbons.BuiltIn())
                Set(ribbon);
        }

        public void Set(RibbonDef def)
        {
            if (!Ribbons.ContainsKey(def.Id))
                RibbonOrder.Add(def.Id);
            Ribbons[def.Id] = def;
        }

        /// <summary>The definition of a ribbon, including the per-body ones; never null.</summary>
        public RibbonDef ResolveRibbon(string id)
        {
            RibbonDef def;
            if (Ribbons.TryGetValue(id, out def))
                return def;
            return Core.Ribbons.ForBody(id) ?? new RibbonDef(id, id, string.Empty, "777777");
        }

        public void Set(ModifierDef def)
        {
            Modifiers[def.Id] = def;
        }

        public ModifierDef Get(string id)
        {
            ModifierDef def;
            return Modifiers.TryGetValue(id, out def) ? def : null;
        }
    }
}
