namespace KerbalRelations
{
    /// <summary>Per-save switches, shown under Difficulty Settings.</summary>
    public class RelationsParameters : GameParameters.CustomParameterNode
    {
        [GameParameters.CustomParameterUI("Enable Kerbal Relations", toolTip = "Turns happiness, relationships and families on or off for this save.", autoPersistance = true)]
        public bool ModEnabled = true;

        [GameParameters.CustomParameterUI("Families (eggs and children)", toolTip = "Couples lay eggs in parts with Kolony Growth switched on. Replaces the USI-LS Kolony Growth timer.", autoPersistance = true)]
        public bool Reproduction = true;

        [GameParameters.CustomParameterUI("Random mood events", toolTip = "Birthdays, arguments about planets and other small things that change a Kerbal's mood.", autoPersistance = true)]
        public bool RandomEvents = true;

        [GameParameters.CustomParameterUI("Missions", toolTip = "Career only: Mission Control offers missions about your Kerbals, such as keeping a lonely Kerbal company or taking a school class on a trip.", autoPersistance = true)]
        public bool Missions = true;

        [GameParameters.CustomParameterUI("Screen messages", toolTip = "Show a message when something happens to your Kerbals.", autoPersistance = true)]
        public bool Messages = true;

        [GameParameters.CustomIntParameterUI("Relationship speed (%)", toolTip = "How fast relationships grow and fade.", autoPersistance = true, minValue = 25, maxValue = 2000, stepSize = 25)]
        public int RelationshipSpeed = 100;

        [GameParameters.CustomIntParameterUI("Growing-up speed (x)", toolTip = "Divides the 9 months an egg needs to hatch and the 14 years a child needs to grow up.", autoPersistance = true, minValue = 1, maxValue = 200, stepSize = 1)]
        public int GrowUpSpeed = 1;

        [GameParameters.CustomIntParameterUI("Event frequency (%)", toolTip = "How often random mood events happen.", autoPersistance = true, minValue = 25, maxValue = 500, stepSize = 25)]
        public int EventFrequency = 100;

        public override string Title
        {
            get { return "Kerbal Relations"; }
        }

        public override GameParameters.GameMode GameMode
        {
            get { return GameParameters.GameMode.ANY; }
        }

        public override string Section
        {
            get { return "Kerbal Relations"; }
        }

        public override string DisplaySection
        {
            get { return "Kerbal Relations"; }
        }

        public override int SectionOrder
        {
            get { return 1; }
        }

        public override bool HasPresets
        {
            get { return false; }
        }

        public static RelationsParameters Current
        {
            get
            {
                var game = HighLogic.CurrentGame;
                return game != null ? game.Parameters.CustomParams<RelationsParameters>() : null;
            }
        }
    }
}
