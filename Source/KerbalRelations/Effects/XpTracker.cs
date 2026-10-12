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

                // The same calculation stock uses, without our extra experience on top.
                double baseXp = advanced.ImmediateLevelUp
                    ? KerbalRoster.CalculateExperience(pcm.careerLog, pcm.flightLog)
                    : KerbalRoster.CalculateExperience(pcm.careerLog);

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
    }
}
