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
    /// 1. Chicken spawn points (from PlayerChickens) — known outdoor garden locations
    /// 2. TertiaryNPCSpawner spawn points — where animal NPCs appear
    /// 3. Boundary polygon centroid — fallback (may land inside the house)
    /// </summary>
    [HarmonyPatch(typeof(HomeDecorationHomesteadBoundary), "OnEnable")]
    public static class GardenAreaPatch
    {
        [HarmonyPostfix]
        static void Postfix(HomeDecorationHomesteadBoundary __instance)
        {
            if (__instance.AreaID != GardenAreaPart.Area5)
                return;

            // Priority 1: Custom position from config (set via F9 key in debug mode)
            Vector3 spawnPos;
            if (CottonCowModPlugin.TryGetCustomSpawnPosition(out spawnPos))
            {
                CottonCowModPlugin.Log.LogInfo(
                    $"GardenAreaPatch: Using custom spawn position: {spawnPos}");
                CowSpawner.OnGardenArea5Loaded(spawnPos);
                return;
            }

            // Priority 2: Find outdoor garden position from scene objects
            spawnPos = FindOutdoorGardenPosition();

            if (spawnPos == Vector3.zero)
            {
                // Priority 3: boundary polygon centroid (may land inside house)
                spawnPos = ComputeBoundaryCentroid(__instance);
            }

            CottonCowModPlugin.Log.LogInfo(
                $"GardenAreaPatch: Area5 spawn position: {spawnPos}");
            CowSpawner.OnGardenArea5Loaded(spawnPos);
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
