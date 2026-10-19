using System.Collections.Generic;
using HarmonyLib;
using TotS;
using TotS.Character;
using TotS.Garden;
using TotS.Unlock;
using UnityEngine;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Catches when the Garden Area 5 boundary loads in the streaming world.
    ///
    /// Position resolution priority:
    /// 1. Custom position from config (dev tool)
    /// 2. The glade (see GladeArea) — spawn and roaming area shaped to fit it
    /// 3. Chicken spawn points (from PlayerChickens) — known outdoor garden locations
    /// 4. TertiaryNPCSpawner spawn points — where animal NPCs appear
    /// 5. Boundary polygon centroid — fallback (may land inside the house)
    /// </summary>
    [HarmonyPatch(typeof(HomeDecorationHomesteadBoundary), "OnEnable")]
    public static class GardenAreaPatch
    {
        [HarmonyPostfix]
        static void Postfix(HomeDecorationHomesteadBoundary __instance)
        {
            if (__instance.AreaID != GardenAreaPart.Area5)
                return;

            CowSpawner.OnGardenArea5Loaded(__instance);
        }

        /// <summary>
        /// Works out where cows spawn and roam. Called by CowSpawner when the first cow spawns,
        /// rather than when the boundary loads, so the surrounding terrain and NavMesh have loaded.
        /// Returns false if the glade isn't ready and fallbacks aren't allowed yet (CowSpawner retries).
        /// roamBoxes is null when only a spawn point is known (CowSpawner then uses a box around it).
        /// </summary>
        internal static bool TryResolveSpawnArea(HomeDecorationHomesteadBoundary area5, bool allowFallback,
            out Vector3 spawnPos, out List<Bounds> roamBoxes)
        {
            roamBoxes = null;

            // Priority 1: Custom position from config (set via F9 key in debug mode)
            if (CottonCowModPlugin.TryGetCustomSpawnPosition(out spawnPos))
            {
                CottonCowModPlugin.Log.LogInfo(
                    $"GardenAreaPatch: Using custom spawn position: {spawnPos}");
                return true;
            }

            // Priority 2: The glade (part of Area 5 outside Area 4), with a roaming area shaped to fit
            if (area5 != null && GladeArea.TryBuild(area5, out spawnPos, out roamBoxes, logFailure: allowFallback))
                return true;

            if (!allowFallback)
                return false;

            // Priority 3: Find outdoor garden position from scene objects
            spawnPos = FindOutdoorGardenPosition();

            if (spawnPos == Vector3.zero && area5 != null)
            {
                // Priority 4: boundary polygon centroid (may land inside house)
                spawnPos = ComputeBoundaryCentroid(area5);
            }

            CottonCowModPlugin.Log.LogWarning(
                $"GardenAreaPatch: Glade not available, using fallback spawn position: {spawnPos}");
            return true;
        }

        /// <summary>
        /// Searches the scene for known outdoor garden objects to use as a spawn reference.
        /// Tries chicken spawn points first, then NPC spawner positions.
        /// </summary>
        private static Vector3 FindOutdoorGardenPosition()
        {
            // Approach 1: Find PlayerChickens and use its chicken spawn locations
            var chickens = Object.FindObjectOfType<PlayerChickens>();
            if (chickens != null)
            {
                var unlocksList = Traverse.Create(chickens)
                    .Field("m_ChickenUnlocks")
                    .GetValue<IList<ChickenUnlock>>();

                if (unlocksList != null && unlocksList.Count > 0)
                {
                    var spawnLoc = unlocksList[0].SpawnLocation;
                    if (spawnLoc != null)
                        return spawnLoc.position;
                }

                var spawner = Traverse.Create(chickens)
                    .Field("m_SpawnerToManageChickenInstances")
                    .GetValue<TertiaryNPCSpawner>();

                if (spawner != null && spawner.transform.position != Vector3.zero)
                    return spawner.transform.position;
            }

            // Approach 2: Find any TertiaryNPCSpawner in the scene
            var spawners = Object.FindObjectsOfType<TertiaryNPCSpawner>();
            foreach (var s in spawners)
            {
                if (s.transform.position != Vector3.zero)
                    return s.transform.position;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Computes the center of the boundary polygon in world space.
        /// Falls back to the transform position if no points are available.
        /// </summary>
        private static Vector3 ComputeBoundaryCentroid(HomeDecorationHomesteadBoundary boundary)
        {
            List<Vector2> points = boundary.Points;
            if (points == null || points.Count == 0)
                return boundary.transform.position;

            Vector2 centroid2D = Vector2.zero;
            foreach (var pt in points)
                centroid2D += pt;
            centroid2D /= points.Count;

            // BoundaryPolygon2D uses (x, z) mapping: Vector2(x,y) → Vector3(x, 0, y)
            Vector3 localPoint = new Vector3(centroid2D.x, 0f, centroid2D.y);
            return boundary.Transform.TransformPoint(localPoint);
        }
    }
}
