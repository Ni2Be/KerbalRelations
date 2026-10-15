using System;
using System.Collections.Generic;
using System.Reflection;
using Experience.Effects;
using HarmonyLib;
using KerbalRelations.Core;
using KerbalRelations.Visuals;
using KSP.UI.Screens.Flight;
using KSP.UI.Screens.Flight.Dialogs;
using UnityEngine;

namespace KerbalRelations.Effects
{
    /// <summary>Installs the patches once at game start. Each patch is applied on its own so one failure does not block the others.</summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyBootstrap : MonoBehaviour
    {
        public const string GrowthModuleType = "LifeSupport.USILS_KolonyGrowthModule";

        /// <summary>USI-LS is installed and its Kolony Growth timer is under our control.</summary>
        public static bool GrowthModulePatched { get; private set; }

        public void Awake()
        {
            var harmony = new Harmony("KerbalRelations");

            Patch(harmony, "lab research",
                AccessTools.Method(typeof(ModuleScienceConverter), "GetScientists"),
                postfix: nameof(Patches.ScientistsPostfix));

            Patch(harmony, "experience",
                AccessTools.Method(typeof(ProtoCrewMember), "CalculateExperiencePoints", new[] { typeof(GameParameters), typeof(Game.Modes) }),
                postfix: nameof(Patches.ExperiencePostfix));
            Patch(harmony, "levels set by cheats",
                AccessTools.Method(typeof(KerbalRoster), "SetExperienceLevel", new[] { typeof(ProtoCrewMember), typeof(int), typeof(GameParameters), typeof(Game.Modes) }),
                postfix: nameof(Patches.LevelSetPostfix));

            Patch(harmony, "EVA button for eggs and children",
                AccessTools.Method(typeof(KerbalPortrait), "CanEVA"),
                prefix: nameof(Patches.PortraitEvaPrefix), postfix: nameof(Patches.PortraitEvaPostfix));
            Patch(harmony, "hatch crew list for eggs and children",
                AccessTools.Method(typeof(CrewHatchDialog), "CreateList"),
                prefix: nameof(Patches.HatchListPrefix), postfix: nameof(Patches.HatchListPostfix));
            foreach (var method in new[] { "FixedUpdate", "Update", "LateUpdate", "OnCollisionEnter", "OnTriggerStay", "OnTriggerExit" })
            {
                Patch(harmony, "eggs outside (" + method + ")",
                    AccessTools.Method(typeof(KerbalEVA), method),
                    prefix: nameof(Patches.RollingEggPrefix));
            }

            var growthModule = AccessTools.TypeByName(GrowthModuleType);
            if (growthModule != null)
            {
                GrowthModulePatched = Patch(harmony, "USI-LS Kolony Growth",
                    AccessTools.Method(growthModule, "OnUpdate"),
                    prefix: nameof(Patches.KolonyGrowthPrefix));
            }
            else
            {
                Log.Info("USI-LS not found; eggs can be laid in any part with a free seat");
            }
        }

        private static bool Patch(Harmony harmony, string what, MethodBase target, string prefix = null, string postfix = null)
        {
            try
            {
                if (target == null)
                {
                    Log.Warn("Could not find the method to patch for " + what);
                    return false;
                }
                harmony.Patch(target,
                    prefix != null ? new HarmonyMethod(typeof(Patches), prefix) : null,
                    postfix != null ? new HarmonyMethod(typeof(Patches), postfix) : null);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Failed to patch " + what, ex);
                return false;
            }
        }
    }

    internal static class Patches
    {
        /// <summary>Science labs: happy scientists research faster.</summary>
        public static void ScientistsPostfix(ModuleScienceConverter __instance, ref float __result)
        {
            if (__result <= 0f || !KerbalRelationsAPI.IsActive || __instance.part == null)
                return;
            var crew = __instance.part.protoModuleCrew;
            var sum = 0d;
            var count = 0;
            for (int i = 0; i < crew.Count; ++i)
            {
                if (!crew[i].HasEffect<ScienceSkill>())
                    continue;
                sum += KerbalRelationsAPI.GetMultiplier(crew[i].name, EffectRegistry.Research);
                ++count;
            }
            if (count > 0)
                __result *= (float)(sum / count);
        }

        /// <summary>
        /// Stock stores an "extraXP" value per Kerbal but never counts it. Counting it here gives a
        /// per-Kerbal XP adjustment that is saved with the roster; <see cref="XpTracker"/> fills it in.
        /// </summary>
        public static void ExperiencePostfix(ProtoCrewMember __instance, ref float __result)
        {
            // 99999 is what stock returns when Kerbal experience is switched off.
            if (__result >= 99999f)
                return;
            var extra = __instance.ExtraExperience;
            if (extra != 0f)
                __result = Mathf.Max(0f, __result + extra);
        }

        /// <summary>The cheat menu's "max level" and the Mission Builder set a level that should stick.</summary>
        public static void LevelSetPostfix(ProtoCrewMember pcm, GameParameters parameters)
        {
            XpTracker.LevelSet(pcm, parameters);
        }

        /// <summary>
        /// Stock offers tourists no EVA button. Eggs and children of the space programme are tourists
        /// to stock, so they pass as crew while stock makes up its mind.
        /// </summary>
        public static void PortraitEvaPrefix(KerbalPortrait __instance, out bool __state)
        {
            var kerbal = __instance.crewMember;
            __state = kerbal != null && EvaAccess.Lift(kerbal.protoCrewMember);
        }

        public static void PortraitEvaPostfix(KerbalPortrait __instance, bool __state)
        {
            if (__state && __instance.crewMember != null)
                EvaAccess.Restore(__instance.crewMember.protoCrewMember);
        }

        /// <summary>The same for the list that opens when a hatch is clicked.</summary>
        public static void HatchListPrefix(List<ProtoCrewMember> crew, out List<ProtoCrewMember> __state)
        {
            __state = EvaAccess.Lift(crew);
        }

        public static void HatchListPostfix(List<ProtoCrewMember> __state)
        {
            for (int i = 0; __state != null && i < __state.Count; ++i)
                EvaAccess.Restore(__state[i]);
        }

        /// <summary>
        /// An egg outside is a stock Kerbal underneath. Its walking, climbing and stumbling are
        /// switched off here while <see cref="EggBall"/> rolls it around.
        /// </summary>
        public static bool RollingEggPrefix(KerbalEVA __instance)
        {
            return !EggBall.Rolling.Contains(__instance);
        }

        private const string GrowthStatus = "Needs a couple (Kerbal Relations)";

        /// <summary>Replaces the USI-LS timer that spawns a Kerbal: with families enabled, couples lay eggs instead.</summary>
        public static bool KolonyGrowthPrefix(PartModule __instance)
        {
            var scenario = RelationsScenario.Instance;
            if (scenario == null || !scenario.ReproductionActive)
                return true;
            try
            {
                var countdown = __instance.Fields["KerbabyCountdown"];
                var enabled = __instance.Fields["KolonyGrowthEnabled"];
                if (countdown != null)
                {
                    countdown.guiActive = enabled != null && (bool)enabled.GetValue(__instance);
                    countdown.SetValue(GrowthStatus, __instance);
                }
            }
            catch (Exception)
            {
                // Display only; a changed field layout must not bring back the stock spawn.
            }
            return false;
        }
    }
}
