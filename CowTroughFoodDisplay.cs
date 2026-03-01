using System;
using System.Collections.Generic;
using TotS.Inventory;
using TotS.Items;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Shows 3D food models inside the physical cow trough based on inventory contents.
    ///
    /// Each of the 4 inventory stacks maps to a display zone along the trough's long axis.
    /// Within each zone, 1–4 models are shown based on stack quantity, with organic
    /// (slightly randomized) positioning so they look tossed in rather than grid-placed.
    /// Models are auto-scaled so different food types appear at consistent sizes.
    ///
    /// Quantity tiers: 1 item → 1 model, 2–3 → 2, 4–6 → 3, 7–9 → 4.
    /// </summary>
    public class CowTroughFoodDisplay : MonoBehaviour
    {
        private const int SlotCount = 4;
        private const int MaxModelsPerSlot = 4;

        // Food model target size as a fraction of the distance between slot centers.
        // Increase to make food larger, decrease for smaller.
        private const float FoodSizeFraction = 0.5f;

        // Randomization
        private const float MaxYawDegrees = 360f;
        private const float MaxTiltDegrees = 15f;

        private Inventory _inventory;
        private Vector3[] _slotCenters;
        private float _slotSpacing;
        private float _shortAxisExtent;
        private bool _longAxisIsX;

        // Per-slot tracking: each slot can hold up to MaxModelsPerSlot GameObjects
        private GameObject[][] _displayedModels;
        private ItemType[] _displayedTypes;
        private int[] _displayedCounts;
        private Dictionary<string, float> _scaleCache;

        private void Start()
        {
            _displayedModels = new GameObject[SlotCount][];
            _displayedTypes = new ItemType[SlotCount];
            _displayedCounts = new int[SlotCount];
            _scaleCache = new Dictionary<string, float>();
            for (int i = 0; i < SlotCount; i++)
                _displayedModels[i] = new GameObject[MaxModelsPerSlot];

            _inventory = CowTroughManager.Instance.GetInventory();
            if (_inventory == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughFoodDisplay: CowTrough inventory not available.");
                return;
            }

            _inventory.Changed += OnInventoryChanged;
            CalculateSlotPositions();
            RefreshDisplay();

            CottonCowModPlugin.Log.LogInfo("CowTroughFoodDisplay: Initialized.");
        }

        /// <summary>
        /// Calculates 4 slot center positions along the trough's long axis.
        /// Uses mesh bounds transformed into trough-local space so the result is
        /// independent of the trough's world rotation.
        /// </summary>
        private void CalculateSlotPositions()
        {
            _slotCenters = new Vector3[SlotCount];

            var meshFilters = GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughFoodDisplay: No MeshFilters found, using defaults.");
                _longAxisIsX = true;
                _slotSpacing = 0.4f;
                _shortAxisExtent = 0.1f;
                for (int i = 0; i < SlotCount; i++)
                    _slotCenters[i] = Vector3.up * 0.5f + Vector3.right * (i - 1.5f) * _slotSpacing;
                return;
            }

            // Build combined bounds in the trough's local space by transforming
            // each mesh's 8 bounding-box corners through the hierarchy.
            Bounds localBounds = new Bounds();
            bool first = true;
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                Vector3 min = mf.sharedMesh.bounds.min;
                Vector3 max = mf.sharedMesh.bounds.max;

                for (int cx = 0; cx < 2; cx++)
                for (int cy = 0; cy < 2; cy++)
                for (int cz = 0; cz < 2; cz++)
                {
                    Vector3 corner = new Vector3(
                        cx == 0 ? min.x : max.x,
                        cy == 0 ? min.y : max.y,
                        cz == 0 ? min.z : max.z);
                    Vector3 worldPt = mf.transform.TransformPoint(corner);
                    Vector3 localPt = transform.InverseTransformPoint(worldPt);

                    if (first)
                    {
                        localBounds = new Bounds(localPt, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPt);
                    }
                }
            }

            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;

            // Y: rest near the bottom of the trough
            float slotY = center.y - extents.y * 0.6f;

            // Long axis = whichever local extent is larger
            _longAxisIsX = extents.x >= extents.z;
            float longExtent = _longAxisIsX ? extents.x : extents.z;
            _shortAxisExtent = _longAxisIsX ? extents.z : extents.x;

            float spread = longExtent * 0.6f;
            _slotSpacing = (SlotCount > 1) ? (2f * spread / (SlotCount - 1)) : spread;

            for (int i = 0; i < SlotCount; i++)
            {
                float t = (i - (SlotCount - 1) * 0.5f) / ((SlotCount - 1) * 0.5f); // -1 to +1
                float offset = t * spread;

                if (_longAxisIsX)
                    _slotCenters[i] = new Vector3(center.x + offset, slotY, center.z);
                else
                    _slotCenters[i] = new Vector3(center.x, slotY, center.z + offset);
            }

            CottonCowModPlugin.Log.LogInfo(
                $"CowTroughFoodDisplay: LocalBounds center={center}, extents={extents}, " +
                $"longAxis={(_longAxisIsX ? "X" : "Z")}, slotSpacing={_slotSpacing:F3}, " +
                $"slots: [{string.Join(", ", Array.ConvertAll(_slotCenters, p => p.ToString("F3")))}]");
        }

        private void OnInventoryChanged(Inventory sender)
        {
            RefreshDisplay();
        }

        /// <summary>
        /// Groups inventory items into stacks by ItemType, then updates each visual slot
        /// to match the corresponding stack's type and quantity tier.
        /// </summary>
        private void RefreshDisplay()
        {
            if (_inventory == null || _slotCenters == null)
                return;

            // Group flat item list into stacks (preserving insertion order)
            var stacks = new List<KeyValuePair<ItemType, int>>();
            var typeToIndex = new Dictionary<ItemType, int>();

            foreach (var item in _inventory.Items)
            {
                var type = item.ItemType;
                if (typeToIndex.TryGetValue(type, out int idx))
                {
                    var old = stacks[idx];
                    stacks[idx] = new KeyValuePair<ItemType, int>(type, old.Value + 1);
                }
                else
                {
                    typeToIndex[type] = stacks.Count;
                    stacks.Add(new KeyValuePair<ItemType, int>(type, 1));
                }
            }

            for (int i = 0; i < SlotCount; i++)
            {
                if (i < stacks.Count)
                {
                    var itemType = stacks[i].Key;
                    int count = stacks[i].Value;
                    int modelCount = GetModelCount(count);

                    // Skip rebuild if already showing this type at this quantity tier
                    if (_displayedTypes[i] == itemType && _displayedCounts[i] == modelCount)
                        continue;

                    ClearSlot(i);
                    SpawnSlotModels(i, itemType, modelCount);
                }
                else
                {
                    ClearSlot(i);
                }
            }
        }

        /// <summary>
        /// Maps stack quantity to number of visible models.
        /// </summary>
        private static int GetModelCount(int stackSize)
        {
            if (stackSize <= 1) return 1;
            if (stackSize <= 3) return 2;
            if (stackSize <= 6) return 3;
            return 4;
        }

        private void SpawnSlotModels(int slotIndex, ItemType itemType, int modelCount)
        {
            var previewAspect = itemType.GetAspect<PreviewAspect>();
            if (previewAspect == null || previewAspect.DisplayPrefab == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    $"CowTroughFoodDisplay: No DisplayPrefab for {itemType.name}.");
                return;
            }

            var prefab = previewAspect.DisplayPrefab;

            // Deterministic seed: same food type in same slot always looks the same
            int baseSeed = slotIndex * 10000 + itemType.name.GetHashCode();

            for (int j = 0; j < modelCount; j++)
            {
                var rng = new System.Random(baseSeed + j);

                var model = UnityEngine.Object.Instantiate(prefab, transform);

                // Auto-scale: normalize all food to a consistent size
                float scale = GetFoodScale(itemType.name, model);
                model.transform.localScale = Vector3.one * scale;

                // Position: slot center + organic sub-offset
                Vector3 subOffset = GetSubOffset(modelCount, j, rng);
                model.transform.localPosition = _slotCenters[slotIndex] + subOffset;

                // Rotation: full random spin on Y, slight random tilt on X/Z
                float yaw = (float)(rng.NextDouble() * MaxYawDegrees);
                float pitch = (float)(rng.NextDouble() * 2 - 1) * MaxTiltDegrees;
                float roll = (float)(rng.NextDouble() * 2 - 1) * MaxTiltDegrees;
                model.transform.localRotation = Quaternion.Euler(pitch, yaw, roll);

                _displayedModels[slotIndex][j] = model;
            }

            _displayedTypes[slotIndex] = itemType;
            _displayedCounts[slotIndex] = modelCount;
        }

        /// <summary>
        /// Computes auto-scale for a food model so its largest mesh dimension (any axis)
        /// fits within a fraction of the slot spacing. Cached per food type.
        /// </summary>
        private float GetFoodScale(string foodName, GameObject instance)
        {
            if (_scaleCache.TryGetValue(foodName, out float cached))
                return cached;

            float maxDim = 0f;
            var meshFilters = instance.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                var size = mf.sharedMesh.bounds.size;
                maxDim = Mathf.Max(maxDim, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
            }

            float targetSize = _slotSpacing * FoodSizeFraction;
            float scale = (maxDim > 0.001f) ? targetSize / maxDim : 1f;

            _scaleCache[foodName] = scale;
            CottonCowModPlugin.Log.LogInfo(
                $"CowTroughFoodDisplay: {foodName} meshMaxXZ={maxDim:F3}, targetSize={targetSize:F3}, scale={scale:F3}");
            return scale;
        }

        /// <summary>
        /// Returns a local-space offset from the slot center for the j-th model in a cluster.
        /// Positions are loosely arranged with seeded jitter for an organic look.
        /// </summary>
        private Vector3 GetSubOffset(int totalCount, int subIndex, System.Random rng)
        {
            float longFrac = 0f;
            float shortFrac = 0f;

            // Base fractional positions — deliberately asymmetric for a natural look
            if (totalCount == 2)
            {
                longFrac  = subIndex == 0 ? -0.5f  : 0.45f;
                shortFrac = subIndex == 0 ? -0.25f : 0.3f;
            }
            else if (totalCount == 3)
            {
                float[] lf = { -0.55f, 0.35f, -0.1f };
                float[] sf = { -0.2f, -0.15f, 0.3f };
                longFrac = lf[subIndex];
                shortFrac = sf[subIndex];
            }
            else if (totalCount >= 4)
            {
                float[] lf = { -0.5f, 0.45f, -0.2f, 0.3f };
                float[] sf = { -0.25f, 0.2f, 0.3f, -0.3f };
                longFrac = lf[subIndex];
                shortFrac = sf[subIndex];
            }

            // Convert fractions to local units
            float maxLong = _slotSpacing * 0.35f;
            float maxShort = _shortAxisExtent * 0.4f;

            float longOff = longFrac * maxLong;
            float shortOff = shortFrac * maxShort;

            // Add random jitter (±15% of the max offsets)
            longOff += (float)(rng.NextDouble() * 2 - 1) * maxLong * 0.15f;
            shortOff += (float)(rng.NextDouble() * 2 - 1) * maxShort * 0.15f;
            float yOff = (float)rng.NextDouble() * 0.005f;

            if (_longAxisIsX)
                return new Vector3(longOff, yOff, shortOff);
            else
                return new Vector3(shortOff, yOff, longOff);
        }

        private void ClearSlot(int index)
        {
            for (int j = 0; j < MaxModelsPerSlot; j++)
            {
                if (_displayedModels[index][j] != null)
                {
                    UnityEngine.Object.Destroy(_displayedModels[index][j]);
                    _displayedModels[index][j] = null;
                }
            }
            _displayedTypes[index] = null;
            _displayedCounts[index] = 0;
        }

        private void OnDestroy()
        {
            if (_inventory != null)
                _inventory.Changed -= OnInventoryChanged;

            if (_displayedModels != null)
            {
                for (int i = 0; i < SlotCount; i++)
                    ClearSlot(i);
            }
        }
    }
}
