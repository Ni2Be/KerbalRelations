namespace KerbalRelations.Core
{
    /// <summary>
    /// Every tunable number of the simulation. Public fields are filled by name from the KR_SETTINGS
    /// config node, so adding a field here makes it configurable.
    /// </summary>
    public class SimConfig
    {
        // Calendar, set by the host from the game's date settings (defaults are Kerbin's).
        public double SecondsPerDay = 21600d;
        public double SecondsPerYear = 9203400d;

        // Happiness (percent).
        public double BaseHappiness = 70d;
        public double LonelyPenalty = 20d;
        public double AloneGraceDays = 10d;
        public double CompanyHappiness = 100d;
        public double BondBonusMax = 50d;
        public double SocialEaseDays = 3d;
        public double MinHappiness = 0d;
        public double MaxHappiness = 200d;
        public double MissPartnerGraceDays = 30d;

        // Relationships (percent).
        public double GrowthPerDay = 0.25d;
        // How far a bond can grow between Kerbals in different vessels: side by side, and a full range apart.
        public double OtherVesselBondCap = 60d;
        public double FarBondCap = 20d;
        /// <summary>Metres within which vessels count as one group.</summary>
        public double TogetherRange = 2000d;
        public double CompatibilityMin = 0.6d;
        public double CompatibilityMax = 1.4d;
        public double DecayGraceDays = 30d;
        public double DecayPerDay = 0.05d;
        public double CoupleThreshold = 70d;
        public double BreakupThreshold = 50d;

        // Reproduction.
        public double EggThreshold = 85d;
        public double EggCooldownYears = 1d;
        public double EggMeanWaitDays = 30d;
        public double EggDurationYears = 0.75d;
        public double ChildDurationYears = 14d;
        public double ParentChildStartStrength = 50d;

        // Age (years) given to Kerbals who were hired rather than hatched; children grow up at
        // ChildDurationYears.
        public double HireAgeMinYears = 18d;
        public double HireAgeMaxYears = 45d;

        // Ribbons.
        public bool EnableRibbons = true;

        // Missions: how long a Kerbal must be away before a mission asks to bring them home.
        public double HomesickYears = 2d;

        // Random events.
        public double EventMeanIntervalDays = 40d;

        // Stepping under time warp.
        public double MaxStepDays = 1d;
        public int MaxSubSteps = 60;

        // Switches and multipliers from the difficulty settings.
        public bool EnableEvents = true;
        public bool EnableReproduction = true;
        public double RelationshipSpeed = 1d;
        public double GrowUpSpeed = 1d;
        public double EventFrequency = 1d;

        /// <summary>Shows the debug panel in the relationship window.</summary>
        public bool Debug = false;

        /// <summary>Name of the home world, set by the host.</summary>
        public string HomeBody = "Kerbin";

        public double Days(double days)
        {
            return days * SecondsPerDay;
        }

        public double Years(double years)
        {
            return years * SecondsPerYear;
        }

        public double EggDuration
        {
            get { return Years(EggDurationYears) / GrowUpSpeed; }
        }

        public double ChildDuration
        {
            get { return Years(ChildDurationYears) / GrowUpSpeed; }
        }
    }
}
