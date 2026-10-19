using System.Collections.Generic;
using TotS;
using TotS.Garden;
using UnityEngine;
using UnityEngine.AI;

namespace CottonCowMod
{
    /// <summary>
    /// Works out where the glade (Garden Area 5) is, so cows spawn and roam there.
    ///
    /// The game's homestead boundaries are cumulative: the Area 5 polygon covers the whole
    /// homestead including the house, and only the highest unlocked boundary is active.
    /// So the glade is the part of the Area 5 polygon that is outside the Area 4 polygon.
    ///
    /// We sample a grid over the Area 5 polygon and keep cells that are inside Area 5,
    /// clear of Area 4 (with a margin), and on the NavMesh. Each row of kept cells is merged
    /// into one box. MapLocationArea.SamplePosition clamps wander targets to the closest of
    /// its colliders, so a set of boxes gives cows a roaming area shaped like the glade.
    /// </summary>
    public static class GladeArea
    {
        private const float CellSize = 2f;
        private const float EdgeMargin = 1.5f;
        private const float NavMeshSearchRadius = 1.5f;
        private const float MaxNavMeshOffset = 1f;
        private const float RayStartHeight = 100f;
        private const float SecondCowMinDistance = 2.5f;

        private static HomeDecorationHomesteadBoundary _area5;
        private static HomeDecorationHomesteadBoundary _area4;

        /// <summary>
        /// Whether a point is in the glade (inside Area 5, outside Area 4), using the
        /// boundaries from the last TryBuild. Used by the bug report and tests.
        /// </summary>
        public static bool Contains(Vector3 point)
        {
            return _area5 != null && _area5.ContainsWorldPoint(point)
                && (_area4 == null || !_area4.ContainsWorldPoint(point));
        }

        public static bool Built => _area5 != null;

        /// <summary>
        /// How far (horizontally) a point is outside the glade: 0 inside, otherwise the distance
        /// to the nearest glade edge it has to cross (Area 5's outer edge or Area 4's edge).
        /// </summary>
        public static float DistanceOutside(Vector3 point)
        {
            if (_area5 == null || Contains(point))
                return 0f;
            if (!_area5.ContainsWorldPoint(point))
                return EdgeDistance(_area5, point);
            return EdgeDistance(_area4, point);
        }

        private static float EdgeDistance(HomeDecorationHomesteadBoundary boundary, Vector3 point)
        {
            var points = boundary.Points;
            var p = new Vector2(point.x, point.z);
            float best = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = ToWorld2D(boundary, points[i]);
                Vector2 b = ToWorld2D(boundary, points[(i + 1) % points.Count]);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        private static Vector2 ToWorld2D(HomeDecorationHomesteadBoundary boundary, Vector2 local)
        {
            Vector3 world = boundary.Transform.TransformPoint(new Vector3(local.x, 0f, local.y));
            return new Vector2(world.x, world.z);
        }

        public static bool TryBuild(HomeDecorationHomesteadBoundary area5,
            out Vector3 spawnPosition, out List<Bounds> roamBoxes, bool logFailure = true)
        {
            spawnPosition = Vector3.zero;
            roamBoxes = new List<Bounds>();

            var points = area5.Points;
            if (points == null || points.Count < 3 || area5.Transform == null)
                return false;

            var area4 = FindBoundary(area5, GardenAreaPart.Area4);
            _area5 = area5;
            _area4 = area4;
            if (area4 == null && logFailure)
                CottonCowModPlugin.Log.LogWarning("GladeArea: Area 4 boundary not found, using all of Area 5.");

            // World-space bounds of the Area 5 polygon
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in points)
            {
                Vector3 world = area5.Transform.TransformPoint(new Vector3(p.x, 0f, p.y));
                minX = Mathf.Min(minX, world.x);
                maxX = Mathf.Max(maxX, world.x);
                minZ = Mathf.Min(minZ, world.z);
                maxZ = Mathf.Max(maxZ, world.z);
            }
            float baseY = area5.Transform.position.y;

            // Kept cells per row, keyed by row index, as (column, ground point)
            var rows = new SortedDictionary<int, List<KeyValuePair<int, Vector3>>>();
            var keptCells = new List<Vector3>();
            int gladeCells = 0;

            int columns = Mathf.CeilToInt((maxX - minX) / CellSize);
            int rowCount = Mathf.CeilToInt((maxZ - minZ) / CellSize);
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    var center = new Vector3(
                        minX + (col + 0.5f) * CellSize, baseY, minZ + (row + 0.5f) * CellSize);

                    if (!IsGladePoint(area5, area4, center))
                        continue;
                    gladeCells++;

                    if (!TryFindGround(center, out Vector3 ground))
                        continue;

                    if (!rows.TryGetValue(row, out var cells))
                        rows[row] = cells = new List<KeyValuePair<int, Vector3>>();
                    // Grid-aligned x/z so merged boxes tile cleanly; ground height from the NavMesh
                    cells.Add(new KeyValuePair<int, Vector3>(col, new Vector3(center.x, ground.y, center.z)));
                    keptCells.Add(ground);
                }
            }

            if (keptCells.Count == 0)
            {
                if (logFailure)
                    CottonCowModPlugin.Log.LogWarning(
                    $"GladeArea: No walkable glade cells found ({gladeCells} of {rowCount * columns} grid cells " +
                    $"in the glade, polygon y={baseY}).");
                return false;
            }

            spawnPosition = CellNearestCentroid(keptCells);
            Center = spawnPosition;
            SecondSpawnPosition = NearbyCell(keptCells, spawnPosition);
            roamBoxes = MergeRows(rows);

            CottonCowModPlugin.Log.LogInfo(
                $"GladeArea: {keptCells.Count} walkable of {gladeCells} glade cells, {roamBoxes.Count} boxes, " +
                $"spawn at {spawnPosition}.");
            return true;
        }

        /// <summary>
        /// Finds walkable ground under a point. The boundary polygon's height (its transform's y)
        /// doesn't match the terrain (the glade is ~10m above it), so cast down from well above
        /// and take the first hit that sits on the NavMesh, skipping tree canopies and roofs.
        /// </summary>
        private static bool TryFindGround(Vector3 point, out Vector3 ground)
        {
            var from = new Vector3(point.x, point.y + RayStartHeight, point.z);
            var hits = Physics.RaycastAll(from, Vector3.down, RayStartHeight * 2f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                // Area mask -1 = all NavMesh areas
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, NavMeshSearchRadius, -1) &&
                    HorizontalDistance(navHit.position, point) <= MaxNavMeshOffset)
                {
                    ground = navHit.position;
                    return true;
                }
            }

            ground = Vector3.zero;
            return false;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Inside Area 5 and outside Area 4, with a margin from both edges so cows don't
        /// clip into fences or wander into the main garden.
        /// </summary>
        private static bool IsGladePoint(HomeDecorationHomesteadBoundary area5,
            HomeDecorationHomesteadBoundary area4, Vector3 point)
        {
            var offsets = new[]
            {
                Vector3.zero,
                new Vector3(EdgeMargin, 0f, 0f), new Vector3(-EdgeMargin, 0f, 0f),
                new Vector3(0f, 0f, EdgeMargin), new Vector3(0f, 0f, -EdgeMargin)
            };
            foreach (var offset in offsets)
            {
                Vector3 p = point + offset;
                if (!area5.ContainsWorldPoint(p))
                    return false;
                if (area4 != null && area4.ContainsWorldPoint(p))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The boundaries are siblings under HomeDecorationHomesteadBoundaryController,
        /// and the lower areas are inactive once Area 5 is unlocked.
        /// </summary>
        private static HomeDecorationHomesteadBoundary FindBoundary(
            HomeDecorationHomesteadBoundary area5, GardenAreaPart areaID)
        {
            var root = area5.GetComponentInParent<HomeDecorationHomesteadBoundaryController>();
            var searchRoot = root != null ? root.transform : area5.transform.parent;
            if (searchRoot == null)
                return null;

            foreach (var boundary in searchRoot.GetComponentsInChildren<HomeDecorationHomesteadBoundary>(true))
            {
                if (boundary.AreaID == areaID)
                    return boundary;
            }
            return null;
        }

        /// <summary>
        /// Picks a real cell near the middle, so the spawn point is inside the glade even
        /// when its shape is concave.
        /// </summary>
        private static Vector3 CellNearestCentroid(List<Vector3> cells)
        {
            Vector3 centroid = Vector3.zero;
            foreach (var cell in cells)
                centroid += cell;
            centroid /= cells.Count;

            Vector3 best = cells[0];
            float bestDistance = float.MaxValue;
            foreach (var cell in cells)
            {
                float distance = (cell - centroid).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }
            return best;
        }

        /// <summary>
        /// A glade cell a few meters from the first cow, for the second cow (a fixed offset
        /// could land outside the glade where it's narrow).
        /// </summary>
        private static Vector3 NearbyCell(List<Vector3> cells, Vector3 from)
        {
            Vector3 best = from;
            float bestDistance = float.MaxValue;
            foreach (var cell in cells)
            {
                float distance = HorizontalDistance(cell, from);
                if (distance >= SecondCowMinDistance && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }
            return best;
        }

        /// <summary>
        /// Where the second cow spawns, from the last successful TryBuild.
        /// </summary>
        public static Vector3 SecondSpawnPosition { get; private set; }

        /// <summary>
        /// A walkable point in the middle of the glade (where the first cow spawns).
        /// </summary>
        public static Vector3 Center { get; private set; }

        /// <summary>
        /// Merges each row's runs of adjacent cells into one box (tall enough to cover
        /// uneven ground).
        /// </summary>
        private static List<Bounds> MergeRows(SortedDictionary<int, List<KeyValuePair<int, Vector3>>> rows)
        {
            var boxes = new List<Bounds>();
            foreach (var cells in rows.Values)
            {
                int i = 0;
                while (i < cells.Count)
                {
                    int start = i;
                    float sumY = cells[i].Value.y;
                    while (i + 1 < cells.Count && cells[i + 1].Key == cells[i].Key + 1)
                    {
                        i++;
                        sumY += cells[i].Value.y;
                    }

                    Vector3 first = cells[start].Value;
                    Vector3 last = cells[i].Value;
                    int count = i - start + 1;
                    var center = new Vector3((first.x + last.x) / 2f, sumY / count, (first.z + last.z) / 2f);
                    boxes.Add(new Bounds(center, new Vector3(count * CellSize, 20f, CellSize)));
                    i++;
                }
            }
            return boxes;
        }
    }
}
