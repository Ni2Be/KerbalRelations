using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KerbalRelations.Core;

namespace KerbalRelations.Effects
{
    /// <summary>
    /// Lets the eggs and children of the space programme leave a vessel. To stock they are tourists,
    /// which keeps them from flying ships and from life support penalties, but tourists get no EVA
    /// button. So where stock decides about that button they pass as crew for a moment, and while they
    /// are outside they are crew for real.
    /// </summary>
    public static class EvaAccess
    {
        private static readonly FieldInfo TypeField = AccessTools.Field(typeof(ProtoCrewMember), "_type");

        /// <summary>Eggs and children may go outside; guests on a school trip may not.</summary>
        public static bool MayGoOutside(ProtoCrewMember crew)
        {
            var scenario = RelationsScenario.Instance;
            if (crew == null || scenario == null || !scenario.Active || !ConfigLoader.YoungOnEva)
                return false;
            var state = scenario.Data.Get(crew.name);
            return state != null && state.Stage != LifeStage.Adult && !state.Visitor;
        }

        /// <summary>Makes a young tourist pass as crew without telling the rest of the game.</summary>
        /// <returns>True if something was changed and <see cref="Restore"/> has to follow.</returns>
        public static bool Lift(ProtoCrewMember crew)
        {
            if (TypeField == null || crew == null || crew.type != ProtoCrewMember.KerbalType.Tourist || !MayGoOutside(crew))
                return false;
            TypeField.SetValue(crew, ProtoCrewMember.KerbalType.Crew);
            return true;
        }

        public static void Restore(ProtoCrewMember crew)
        {
            if (TypeField != null && crew != null)
                TypeField.SetValue(crew, ProtoCrewMember.KerbalType.Tourist);
        }

        public static List<ProtoCrewMember> Lift(List<ProtoCrewMember> crew)
        {
            List<ProtoCrewMember> lifted = null;
            for (int i = 0; crew != null && i < crew.Count; ++i)
            {
                if (!Lift(crew[i]))
                    continue;
                if (lifted == null)
                    lifted = new List<ProtoCrewMember>();
                lifted.Add(crew[i]);
            }
            return lifted;
        }

        /// <summary>
        /// Keeps the type of an egg or child right: crew while outside, a tourist everywhere else.
        /// </summary>
        public static void Sync(ProtoCrewMember crew, KerbalState state, Vessel vessel)
        {
            if (state.Stage == LifeStage.Adult || state.Visitor)
                return;
            var outside = vessel != null && (vessel.isEVA || vessel.vesselType == VesselType.EVA) && ConfigLoader.YoungOnEva;
            var wanted = outside ? ProtoCrewMember.KerbalType.Crew : ProtoCrewMember.KerbalType.Tourist;
            if (crew.type == wanted)
                return;
            if (crew.type != ProtoCrewMember.KerbalType.Crew && crew.type != ProtoCrewMember.KerbalType.Tourist)
                return;
            crew.type = wanted;
        }
    }
}
