using System;
using System.Collections.Generic;
using System.Globalization;

namespace KerbalRelations.Core
{
    /// <summary>A ribbon Kerbals can earn. It is shown as a medal: striped cloth and a badge with a picture.</summary>
    public class RibbonDef
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>Stripes from left to right as "rrggbb:width", separated by commas; the width is optional.</summary>
        public string Stripes;
        /// <summary>The picture on the badge: one of <see cref="RibbonArt.GlyphNames"/>, or the path of a texture.</summary>
        public string Icon;

        public RibbonDef()
        {
        }

        public RibbonDef(string id, string name, string description, string stripes, string icon = null)
        {
            Id = id;
            Name = name;
            Description = description;
            Stripes = stripes;
            Icon = icon;
        }
    }

    public struct RibbonStripe
    {
        public byte R;
        public byte G;
        public byte B;
        public double Width;
    }

    /// <summary>Ids of the ribbons the simulation hands out.</summary>
    public static class RibbonIds
    {
        public const string FirstFriend = "FirstFriend";
        public const string BestFriends = "BestFriends";
        public const string SocialButterfly = "SocialButterfly";
        public const string Sweetheart = "Sweetheart";
        public const string BrokenHeart = "BrokenHeart";
        public const string Reunited = "Reunited";
        public const string Anniversary = "Anniversary";
        public const string Parent = "Parent";
        public const string BigFamily = "BigFamily";
        public const string ProudParent = "ProudParent";
        public const string Grandparent = "Grandparent";
        public const string SpaceBorn = "SpaceBorn";
        public const string CameOfAge = "CameOfAge";
        public const string Explorer = "Explorer";
        public const string GrandTour = "GrandTour";
        public const string Homecoming = "Homecoming";
        public const string LongHaul = "LongHaul";
        public const string Hermit = "Hermit";
        public const string CloudNine = "CloudNine";
        public const string RoughPatch = "RoughPatch";
        public const string InMemory = "InMemory";
        public const string Seasoned = "Seasoned";
        public const string Veteran = "Veteran";
        public const string SilverKerbal = "SilverKerbal";
        public const string GoodCompany = "GoodCompany";
        public const string Chaperone = "Chaperone";

        // One ribbon per body; the body name follows the prefix.
        public const string VisitPrefix = "Visit:";
        public const string LandPrefix = "Land:";
        public const string FirstBornPrefix = "FirstBorn:";

        // What it takes.
        public const double FriendBond = 50d;
        public const double BestFriendBond = 100d;
        public const int ButterflyFriends = 10;
        public const double KnownBond = 10d;
        public const double ReunitedYears = 1d;
        public const double AnniversaryYears = 5d;
        public const int BigFamilyChildren = 3;
        public const int ExplorerBodies = 3;
        public const int GrandTourBodies = 5;
        public const double HomecomingYears = 1d;
        public const double LongHaulYears = 5d;
        public const double HermitDays = 100d;
        public const double CloudNineHappiness = 180d;
        public const double RoughPatchHappiness = 20d;
        public const double MournedBond = 50d;
        public const int SeasonedLevel = 3;
        public const int VeteranLevel = 5;
        public const double SilverAge = 50d;
    }

    public static class Ribbons
    {
        /// <summary>The built-in ribbons, in the order they are shown.</summary>
        public static List<RibbonDef> BuiltIn()
        {
            return new List<RibbonDef>
            {
                new RibbonDef(RibbonIds.FirstFriend, "First Friend", "Reached a bond of 50% with another Kerbal.", "3f9b4a:3,f2d24b:1,3f9b4a:3", "smile"),
                new RibbonDef(RibbonIds.BestFriends, "Best Friends", "Reached a bond of 100% with another Kerbal.", "3f9b4a:2,f2d24b:1,3f9b4a:1,f2d24b:1,3f9b4a:2", "friends"),
                new RibbonDef(RibbonIds.SocialButterfly, "Social Butterfly", "Knows ten other Kerbals.", "e07a2f,f2d24b,4aa3df,b56bd6,4aa3df,f2d24b,e07a2f", "butterfly"),
                new RibbonDef(RibbonIds.Sweetheart, "Sweetheart", "Became part of a couple.", "d8477f:3,ffffff:1,d8477f:3", "heart"),
                new RibbonDef(RibbonIds.BrokenHeart, "Broken Heart", "Went through a break-up.", "55606e:3,d8477f:1,55606e:3", "brokenheart"),
                new RibbonDef(RibbonIds.Reunited, "Reunited", "Back with their partner after more than a year apart.", "d8477f:2,f2d24b:1,d8477f:2", "hearts"),
                new RibbonDef(RibbonIds.Anniversary, "Five Years Together", "Has been part of a couple for five years.", "8b1e4f:2,f2d24b:1,8b1e4f:1,f2d24b:1,8b1e4f:2", "ring"),
                new RibbonDef(RibbonIds.Parent, "Parent", "An egg of theirs hatched.", "4aa3df:3,ffffff:1,d8477f:3", "egg"),
                new RibbonDef(RibbonIds.BigFamily, "Big Family", "Parent of three.", "4aa3df:2,ffffff:1,f2d24b:1,ffffff:1,d8477f:2", "eggs"),
                new RibbonDef(RibbonIds.ProudParent, "Proud Parent", "Raised a child to adulthood.", "4aa3df:2,f2d24b:2,d8477f:2", "cap"),
                new RibbonDef(RibbonIds.Grandparent, "Grandparent", "A grandchild hatched.", "8a6fb0:3,f2d24b:1,8a6fb0:3", "tree"),
                new RibbonDef(RibbonIds.SpaceBorn, "Space Born", "Hatched away from the home world.", "1d2b4f:3,7fe0d6:1,1d2b4f:3", "eggstar"),
                new RibbonDef(RibbonIds.CameOfAge, "Came of Age", "Grew up in the space programme.", "2fb5a5:3,ffffff:1,2fb5a5:3", "cake"),
                new RibbonDef(RibbonIds.Explorer, "Explorer", "Travelled to three other worlds.", "2c4f8f:3,e07a2f:1,2c4f8f:3", "compass"),
                new RibbonDef(RibbonIds.GrandTour, "Grand Tour", "Landed on five other worlds.", "2c4f8f:2,e07a2f:1,f2d24b:1,e07a2f:1,2c4f8f:2", "flag"),
                new RibbonDef(RibbonIds.Homecoming, "Homecoming", "Came home after more than a year away.", "3f9b4a:2,4aa3df:2,3f9b4a:2", "house"),
                new RibbonDef(RibbonIds.LongHaul, "Long Haul", "Spent five years away from home in one go.", "1d2b4f:2,c0c6cf:1,1d2b4f:1,c0c6cf:1,1d2b4f:2", "hourglass"),
                new RibbonDef(RibbonIds.Hermit, "Hermit", "Spent a hundred days without any company.", "6b5a45:3,c0c6cf:1,6b5a45:3", "moon"),
                new RibbonDef(RibbonIds.CloudNine, "Cloud Nine", "Reached 180% happiness.", "b56bd6:2,7fe0d6:1,ffffff:1,7fe0d6:1,b56bd6:2", "cloud"),
                new RibbonDef(RibbonIds.RoughPatch, "Rough Patch", "Got through a time at 20% happiness or less.", "3a3f47:3,c0392b:1,3a3f47:3", "rain"),
                new RibbonDef(RibbonIds.InMemory, "In Memory", "Lost someone close.", "202328:3,c0c6cf:1,202328:3", "candle"),
                new RibbonDef(RibbonIds.Seasoned, "Seasoned", "Reached level 3.", "b0762f:3,f2d24b:1,b0762f:3", "chevrons"),
                new RibbonDef(RibbonIds.Veteran, "Veteran", "Reached level 5.", "b0762f:2,f2d24b:1,ffffff:1,f2d24b:1,b0762f:2", "star"),
                new RibbonDef(RibbonIds.SilverKerbal, "Silver Kerbal", "Turned fifty.", "c0c6cf:3,55606e:1,c0c6cf:3", "fifty"),
                new RibbonDef(RibbonIds.GoodCompany, "Good Company", "Kept a lonely Kerbal company on a mission.", "e07a2f:3,ffffff:1,e07a2f:3", "cup"),
                new RibbonDef(RibbonIds.Chaperone, "Chaperone", "Looked after a school class in space.", "f2d24b:3,2c4f8f:1,f2d24b:3", "book")
            };
        }

        /// <summary>The ribbon for a body: every body gets its own colour.</summary>
        public static RibbonDef ForBody(string id)
        {
            string prefix;
            if (id.StartsWith(RibbonIds.VisitPrefix, StringComparison.Ordinal))
                prefix = RibbonIds.VisitPrefix;
            else if (id.StartsWith(RibbonIds.LandPrefix, StringComparison.Ordinal))
                prefix = RibbonIds.LandPrefix;
            else if (id.StartsWith(RibbonIds.FirstBornPrefix, StringComparison.Ordinal))
                prefix = RibbonIds.FirstBornPrefix;
            else
                return null;

            var body = id.Substring(prefix.Length);
            var hue = StableHash.Unit(body) * 359d;
            var dark = Hex(hue, 0.6d, 0.62d);
            var light = Hex(hue, 0.22d, 0.97d);
            if (prefix == RibbonIds.VisitPrefix)
                return new RibbonDef(id, body + " Visitor", "Travelled to " + body + ".", dark + ":3," + light + ":1," + dark + ":3", "orbit");
            if (prefix == RibbonIds.LandPrefix)
                return new RibbonDef(id, body + " Lander", "Landed on " + body + ".", dark + ":2," + light + ":1," + dark + ":1," + light + ":1," + dark + ":2", "lander");
            return new RibbonDef(id, "First Born of " + body, "The first Kerbal to hatch at " + body + ".", dark + ":2,f2d24b:2," + dark + ":2", "hatch");
        }

        private static string Hex(double hue, double saturation, double value)
        {
            double r, g, b;
            SkinTint.FromHsv(hue, saturation, value, out r, out g, out b);
            return ((int)Math.Round(r * 255d)).ToString("x2", CultureInfo.InvariantCulture)
                + ((int)Math.Round(g * 255d)).ToString("x2", CultureInfo.InvariantCulture)
                + ((int)Math.Round(b * 255d)).ToString("x2", CultureInfo.InvariantCulture);
        }

        /// <summary>Reads a stripe list; null if it is not well formed.</summary>
        public static List<RibbonStripe> ParseStripes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            var result = new List<RibbonStripe>();
            foreach (var part in text.Split(','))
            {
                var pieces = part.Trim().Split(':');
                var hex = pieces[0].Trim();
                int rgb;
                if (pieces.Length > 2 || hex.Length != 6
                    || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                    return null;
                var width = 1d;
                if (pieces.Length == 2
                    && (!double.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out width) || width <= 0d))
                    return null;
                result.Add(new RibbonStripe { R = (byte)(rgb >> 16), G = (byte)(rgb >> 8), B = (byte)rgb, Width = width });
            }
            return result.Count > 0 ? result : null;
        }
    }
}
