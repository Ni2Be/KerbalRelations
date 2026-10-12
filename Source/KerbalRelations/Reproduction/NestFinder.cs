using System;
using System.Collections.Generic;
using KerbalRelations.Effects;

namespace KerbalRelations
{
    /// <summary>
    /// Finds seats where an egg can be laid. With USI-LS installed that is a part whose Kolony Growth
    /// toggle is on (habitation parts and the MKS Kolonization modules); without it, any free seat.
    /// </summary>
    public static class NestFinder
    {
        private const string GrowthModuleName = "USILS_KolonyGrowthModule";
        private const string GrowthEnabledField = "KolonyGrowthEnabled";

        public static bool AnyNest(List<Vessel> vessels)
        {
            return FindLoadedNest(vessels) != null || FindSavedNest(vessels) != null;
        }

        /// <summary>A nest on a loaded vessel.</summary>
        public static Part FindLoadedNest(List<Vessel> vessels)
        {
            for (int i = 0; i < vessels.Count; ++i)
            {
                var vessel = vessels[i];
                if (vessel == null || !vessel.loaded || vessel.isEVA)
                    continue;
                for (int p = 0; p < vessel.parts.Count; ++p)
                {
                    if (IsNest(vessel.parts[p]))
                        return vessel.parts[p];
                }
            }
            return null;
        }

        /// <summary>A nest on an unloaded vessel, as a part of its saved state.</summary>
        public static ProtoPartSnapshot FindSavedNest(List<Vessel> vessels)
        {
            for (int i = 0; i < vessels.Count; ++i)
            {
                var vessel = vessels[i];
                if (vessel == null || vessel.loaded || vessel.isEVA || vessel.protoVessel == null)
                    continue;
                var parts = vessel.protoVessel.protoPartSnapshots;
                for (int p = 0; p < parts.Count; ++p)
                {
                    if (IsNest(parts[p]))
                        return parts[p];
                }
            }
            return null;
        }

        private static bool IsNest(Part part)
        {
            if (part == null || part.CrewCapacity <= part.protoModuleCrew.Count)
                return false;
            if (!HarmonyBootstrap.GrowthModulePatched)
                return true;

            for (int i = 0; i < part.Modules.Count; ++i)
            {
                var module = part.Modules[i];
                if (module.moduleName != GrowthModuleName)
                    continue;
                var field = module.Fields[GrowthEnabledField];
                return field != null && (bool)field.GetValue(module);
            }
            return false;
        }

        private static bool IsNest(ProtoPartSnapshot part)
        {
            if (part == null || part.partInfo == null || part.partInfo.partPrefab == null)
                return false;
            var needsToggle = HarmonyBootstrap.GrowthModulePatched;
            if (needsToggle && !part.partInfo.partPrefab.Modules.Contains(GrowthModuleName))
                return false;
            if (SavedSeats(part) <= part.protoModuleCrew.Count)
                return false;
            if (!needsToggle)
                return true;

            for (int i = 0; i < part.modules.Count; ++i)
            {
                var module = part.modules[i];
                if (module.moduleName != GrowthModuleName || module.moduleValues == null)
                    continue;
                bool enabled;
                return bool.TryParse(module.moduleValues.GetValue(GrowthEnabledField), out enabled) && enabled;
            }
            return false;
        }

        /// <summary>
        /// Seats of a part on an unloaded vessel. The saved part does not record them. Inflatable
        /// habitats have none in their part definition and get them from a module when deployed, so
        /// for those the saved module state decides.
        /// </summary>
        private static int SavedSeats(ProtoPartSnapshot part)
        {
            var prefab = part.partInfo.partPrefab;
            var seats = prefab.CrewCapacity;
            for (int i = 0; i < part.modules.Count; ++i)
            {
                var saved = part.modules[i];
                if (saved.moduleValues == null)
                    continue;

                if (saved.moduleName == "USIAnimation")
                {
                    // USI inflatables (MKS Ranger and Tundra habitats).
                    var module = prefab.Modules["USIAnimation"];
                    var capacity = IntField(module, "CrewCapacity");
                    if (capacity > 0 && BoolField(module, "inflatable"))
                        seats = SavedBool(saved, "isDeployed") ? capacity : 0;
                }
                else if (saved.moduleName == "ModuleDeployableHabitat")
                {
                    // Stockalike Station Parts Expansion.
                    var module = prefab.Modules["ModuleDeployableHabitat"];
                    seats = IntField(module, SavedBool(saved, "Deployed") ? "DeployedCrewCapacity" : "RetractedCrewCapacity");
                }
            }
            return seats;
        }

        private static bool SavedBool(ProtoPartModuleSnapshot module, string name)
        {
            bool value;
            return bool.TryParse(module.moduleValues.GetValue(name), out value) && value;
        }

        private static int IntField(PartModule module, string name)
        {
            var field = module != null ? module.Fields[name] : null;
            return field != null ? Convert.ToInt32(field.GetValue(module)) : 0;
        }

        private static bool BoolField(PartModule module, string name)
        {
            var field = module != null ? module.Fields[name] : null;
            return field != null && (bool)field.GetValue(module);
        }
    }
}
