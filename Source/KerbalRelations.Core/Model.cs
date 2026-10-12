using System;
using System.Collections.Generic;

namespace KerbalRelations.Core
{
    public enum LifeStage
    {
        Adult,
        Egg,
        Child
    }

    public enum Sex
    {
        Male,
        Female
    }

    /// <summary>A temporary happiness bonus or penalty.</summary>
    public class ActiveModifier
    {
        public string Id;
        public string Text;
        /// <summary>What the modifier is about (a body or another Kerbal), if anything.</summary>
        public string Subject;
        public double Amount;
        public double StartUT;
        public double ExpiresUT;
        /// <summary>Condition-style modifiers are refreshed while the condition holds and never fade.</summary>
        public bool NoFade;

        /// <summary>Full strength for the first three quarters of its life, then fades out linearly.</summary>
        public double EffectiveAmount(double now)
        {
            if (now >= ExpiresUT)
                return 0d;
            if (NoFade)
                return Amount;
            var fade = (ExpiresUT - StartUT) * 0.25d;
            if (fade <= 0d || now < ExpiresUT - fade)
                return Amount;
            return Amount * (ExpiresUT - now) / fade;
        }
    }

    public class LogEntry
    {
        public double UT;
        public string Text;
        public double Amount;
    }

    /// <summary>A ribbon a Kerbal has earned, and when.</summary>
    public class RibbonAward
    {
        public string Id;
        public double UT;
    }

    public class KerbalState
    {
        public const int MaxLogEntries = 8;

        public string Name;
        public Sex Sex;
        public LifeStage Stage = LifeStage.Adult;
        /// <summary>Current happiness, 0-200.</summary>
        public double Happiness;
        /// <summary>The slowly changing part of happiness that comes from company and bonds.</summary>
        public double Social;
        /// <summary>UT since which the Kerbal has had no company, or -1.</summary>
        public double AloneSince = -1d;
        /// <summary>UT since which the Kerbal has been away from their partner, or -1.</summary>
        public double PartnerApartSince = -1d;
        /// <summary>UT the current life stage began (egg laid / child hatched).</summary>
        public double StageStartUT;
        /// <summary>UT of birth (hatching). Only meaningful once <see cref="BirthPlace"/> is set.</summary>
        public double BirthUT = -1d;
        /// <summary>Where the Kerbal was born, to follow "Born": "on Minmus, aboard Hab One". Null until known.</summary>
        public string BirthPlace;
        /// <summary>The body the Kerbal was born on or around.</summary>
        public string BirthBody;
        /// <summary>UT since which the Kerbal has been away from the KSC, or -1.</summary>
        public double AwaySince = -1d;
        /// <summary>A guest brought along by a mission: stays an egg or a child and collects nothing.</summary>
        public bool Visitor;
        public string ParentA;
        public string ParentB;
        public string Partner;
        /// <summary>Log-derived XP last seen for this Kerbal, or -1 when not yet observed.</summary>
        public double LastBaseXp = -1d;
        public readonly List<ActiveModifier> Modifiers = new List<ActiveModifier>();
        public readonly HashSet<string> VisitedBodies = new HashSet<string>();
        public readonly HashSet<string> LandedBodies = new HashSet<string>();
        public readonly List<LogEntry> Log = new List<LogEntry>();
        /// <summary>In the order they were earned.</summary>
        public readonly List<RibbonAward> Ribbons = new List<RibbonAward>();

        // Not persisted: refreshed by the host every tick.
        public bool AtHome;
        /// <summary>False for Kerbals that may not pair up (tourists, grouchy crew).</summary>
        public bool Eligible = true;
        public int GroupIndex = -1;
        /// <summary>Which vessel of the group the Kerbal is aboard.</summary>
        public int Place;
        /// <summary>Experience level, for the ribbons that go with it.</summary>
        public int Level;
        /// <summary>Name of the strongest bond currently present, for display.</summary>
        public string BestCompany;
        /// <summary>Happiness last reported to listeners.</summary>
        public double NotifiedHappiness = double.NaN;

        public bool HasParent(string name)
        {
            return name != null && (ParentA == name || ParentB == name);
        }

        /// <summary>Hatched from an egg in this save, as opposed to hired.</summary>
        public bool BornInGame
        {
            get { return ParentA != null || ParentB != null; }
        }

        public bool HasRibbon(string id)
        {
            for (int i = 0; i < Ribbons.Count; ++i)
            {
                if (Ribbons[i].Id == id)
                    return true;
            }
            return false;
        }

        public ActiveModifier FindModifier(string id)
        {
            for (int i = 0; i < Modifiers.Count; ++i)
            {
                if (Modifiers[i].Id == id)
                    return Modifiers[i];
            }
            return null;
        }

        public void AddLog(double ut, string text, double amount)
        {
            Log.Add(new LogEntry { UT = ut, Text = text, Amount = amount });
            while (Log.Count > MaxLogEntries)
                Log.RemoveAt(0);
        }
    }

    public class Relationship
    {
        /// <summary>The two names in ordinal order, so each pair has exactly one record.</summary>
        public string A;
        public string B;
        /// <summary>0-100.</summary>
        public double Strength;
        public double KnownSince;
        public double LastTogetherUT;
        public bool IsCouple;
        public double CoupleSince = -1d;
        public double LastEggUT = -1d;
        /// <summary>UT at which an egg became due but could not be laid yet, or -1.</summary>
        public double EggPendingSince = -1d;
        /// <summary>Parent/child or siblings.</summary>
        public bool Family;

        public string Other(string name)
        {
            return A == name ? B : A;
        }

        public bool Involves(string name)
        {
            return A == name || B == name;
        }
    }

    public static class PairKey
    {
        public static string Make(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }
    }

    public static class StableHash
    {
        /// <summary>FNV-1a. string.GetHashCode differs between runtimes, which would change saved games' behaviour.</summary>
        public static uint Of(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < text.Length; ++i)
                {
                    hash ^= text[i];
                    hash *= 16777619;
                }
                return hash;
            }
        }

        /// <summary>A value in [0, 1] derived from the text.</summary>
        public static double Unit(string text)
        {
            return (Of(text) % 10007u) / 10006d;
        }
    }

    public interface IRandom
    {
        /// <summary>Uniform in [0, 1).</summary>
        double NextDouble();
    }

    public class SystemRandom : IRandom
    {
        private readonly Random _random;

        public SystemRandom()
        {
            _random = new Random();
        }

        public SystemRandom(int seed)
        {
            _random = new Random(seed);
        }

        public double NextDouble()
        {
            return _random.NextDouble();
        }
    }
}
