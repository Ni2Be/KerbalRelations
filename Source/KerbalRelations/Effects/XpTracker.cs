using System;
using KerbalRelations.Core;

namespace KerbalRelations.Effects
{
    /// <summary>
    /// Scales experience gains by happiness. Whenever a Kerbal's log-derived XP has risen, the difference
    /// times (multiplier - 1) is added to their extra experience, which the experience patch counts.
    /// </summary>
    public static class XpTracker
    {
        public static void Update(RelationsScenario scenario)
        {
            var game = HighLogic.CurrentGame;
            var advanced = game.Parameters.CustomParams<GameParameters.AdvancedParams>();
            if (!advanced.KerbalExperienceEnabled(game.Mode))
                return;

            var roster = game.CrewRoster;
            foreach (var state in scenario.Data.Kerbals.Values)
            {
                var pcm = roster[state.Name];
                if (pcm == null)
                    continue;
                if (state.Stage != LifeStage.Adult || pcm.type != ProtoCrewMember.KerbalType.Crew)
                {
                    state.LastBaseXp = -1d;
                    continue;
                }

                double baseXp = BaseXp(pcm, advanced);
                if (state.LastBaseXp >= 0d && baseXp > state.LastBaseXp + 1e-4)
                {
                    var multiplier = ConfigLoader.Effects.GetMultiplier(EffectRegistry.Experience, state.Happiness);
                    var bonus = (baseXp - state.LastBaseXp) * (multiplier - 1d);
                    if (Math.Abs(bonus) > 1e-4)
                    {
                        // Never take away more than the Kerbal has earned. The setter recalculates the level.
                        pcm.ExtraExperience = (float)Math.Max(-baseXp, pcm.ExtraExperience + bonus);
                    }
                }
                state.LastBaseXp = baseXp;
            }
        }

        /// <summary>
        /// A level set directly (the cheat menu, the Mission Builder) is meant exactly: happiness neither adds
        /// to nor takes from it, and the jump to it is not scaled later.
        /// </summary>
        public static void LevelSet(ProtoCrewMember pcm, GameParameters parameters)
        {
            if (pcm == null || parameters == null)
                return;
            if (pcm.ExtraExperience != 0f)
                pcm.ExtraExperience = 0f;
            var scenario = RelationsScenario.Instance;
            KerbalState state;
            if (scenario != null && scenario.Data.Kerbals.TryGetValue(pcm.name, out state) && state.LastBaseXp >= 0d)
                state.LastBaseXp = BaseXp(pcm, parameters.CustomParams<GameParameters.AdvancedParams>());
        }

        // The same calculation stock uses, without our extra experience on top.
        private static double BaseXp(ProtoCrewMember pcm, GameParameters.AdvancedParams advanced)
        {
            return advanced.ImmediateLevelUp
                ? KerbalRoster.CalculateExperience(pcm.careerLog, pcm.flightLog)
                : KerbalRoster.CalculateExperience(pcm.careerLog);
        }
    }
}
