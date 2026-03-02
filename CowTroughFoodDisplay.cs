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
    /// Models are mixed across the full length of the trough regardless of type,
    /// with organic (slightly randomized) positioning so they look tossed in.
    /// Models are auto-scaled so different food types appear at consistent sizes.
    ///
    /// Quantity tiers per type: 1 item → 1 model, 2–3 → 2, 4–6 → 3, 7–9 → 4.
    /// </summary>
    public class CowTroughFoodDisplay : MonoBehaviour
    {
        private const int MaxTotalModels = 16;

        // Food model target size relative to trough length.
        private const float FoodSizeFraction = 0.5f;

        // Randomization
        private const float MaxYawDegrees = 360f;
        private const float MaxTiltDegrees = 15f;

        private Inventory _inventory;
        private Vector3 _troughCenter;
        private float _longSpread;
        private float _shortAxisExtent;
        private float _slotSpacing; // kept for scale calculation
        private bool _longAxisIsX;
        private bool _geometryReady;

        private List<GameObject> _spawnedModels;
        private Dictionary<string, float> _scaleCache;

        private void Start()
        {
            _spawnedModels = new List<GameObject>();
            _scaleCache = new Dictionary<string, float>();

            _inventory = CowTroughManager.Instance.GetInventory();
            if (_inventory == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughFoodDisplay: CowTrough inventory not available.");
                return;
            }

            _inventory.Changed += OnInventoryChanged;
            CalculateTroughGeometry();
            RefreshDisplay();

            CottonCowModPlugin.Log.LogInfo("CowTroughFoodDisplay: Initialized.");
        }

        /// <summary>
        /// Measures the trough mesh bounds in local space to determine the long axis,
        /// spread, and baseline Y for placing food models.
        /// </summary>
        private void CalculateTroughGeometry()
        {
            var meshFilters = GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughFoodDisplay: No MeshFilters found, using defaults.");
                _longAxisIsX = true;
                _longSpread = 0.6f;
                _shortAxisExtent = 0.1f;
                _slotSpacing = 0.4f;
                _troughCenter = Vector3.up * 0.5f;
                _geometryReady = true;
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

            // Long axis = whichever local extent is larger
            _longAxisIsX = extents.x >= extents.z;
            float longExtent = _longAxisIsX ? extents.x : extents.z;
            _shortAxisExtent = _longAxisIsX ? extents.z : extents.x;

            _longSpread = longExtent * 0.6f;
            _slotSpacing = longExtent * 0.4f; // matches original sizing
            _troughCenter = new Vector3(
                center.x,
                center.y - extents.y * 0.6f,
                center.z);
            _geometryReady = true;
        }

        private void OnInventoryChanged(Inventory sender)
        {
            RefreshDisplay();
        }

        /// <summary>
        /// Builds a mixed list of food models from all item types, shuffles them,
        /// and distributes them across the full length of the trough.
        /// </summary>
        private void RefreshDisplay()
        {
            ClearAll();

            if (_inventory == null || !_geometryReady || _inventory.Items.Count == 0)
                return;

            // Group items by type
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

            // Build flat list of model entries, round-robin across types for even mixing
            var modelEntries = new List<ItemType>();
            bool added = true;
            while (added && modelEntries.Count < MaxTotalModels)
            {
                added = false;
                foreach (var stack in stacks)
                {
                    int needed = GetModelCount(stack.Value);
                    int have = 0;
                    foreach (var e in modelEntries)
                        if (e == stack.Key) have++;
                    if (have < needed)
                    {
                        modelEntries.Add(stack.Key);
                        added = true;
                        if (modelEntries.Count >= MaxTotalModels) break;
                    }
                }
            }

            if (modelEntries.Count == 0)
                return;

            // Deterministic shuffle so types are mixed across the trough
            int seed = 42;
            foreach (var stack in stacks)
                seed = seed * 31 + stack.Key.name.GetHashCode() + stack.Value;
            var rng = new System.Random(seed);
            for (int i = modelEntries.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = modelEntries[i];
                modelEntries[i] = modelEntries[j];
                modelEntries[j] = tmp;
            }

            // Distribute models across the full trough length
            int count = modelEntries.Count;
            for (int i = 0; i < count; i++)
            {
                SpawnModel(i, count, modelEntries[i], rng);
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

        private void SpawnModel(int index, int totalCount, ItemType itemType, System.Random rng)
        {
            var previewAspect = itemType.GetAspect<PreviewAspect>();
            if (previewAspect == null || previewAspect.DisplayPrefab == null)
                return;

            var model = UnityEngine.Object.Instantiate(previewAspect.DisplayPrefab, transform);

            // Auto-scale
            float scale = GetFoodScale(itemType.name, model);
            model.transform.localScale = Vector3.one * scale;

            // Position along the trough's long axis, with organic jitter
            float t = totalCount == 1 ? 0f
                : ((float)index / (totalCount - 1)) * 2f - 1f; // -1 to +1
            float longOff = t * _longSpread;

            // Add jitter so models aren't in a perfect line
            float jitterLong = (float)(rng.NextDouble() * 2 - 1) * _longSpread * 0.08f;
            float jitterShort = (float)(rng.NextDouble() * 2 - 1) * _shortAxisExtent * 0.4f;
            float yOff = (float)rng.NextDouble() * 0.005f;

            Vector3 pos = _troughCenter;
            if (_longAxisIsX)
                pos += new Vector3(longOff + jitterLong, yOff, jitterShort);
            else
                pos += new Vector3(jitterShort, yOff, longOff + jitterLong);

            model.transform.localPosition = pos;

            // Rotation: full random spin on Y, slight random tilt on X/Z
            float yaw = (float)(rng.NextDouble() * MaxYawDegrees);
            float pitch = (float)(rng.NextDouble() * 2 - 1) * MaxTiltDegrees;
            float roll = (float)(rng.NextDouble() * 2 - 1) * MaxTiltDegrees;
            model.transform.localRotation = Quaternion.Euler(pitch, yaw, roll);

            _spawnedModels.Add(model);
        }

        /// <summary>
        /// Computes auto-scale for a food model so its largest mesh dimension
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
            return scale;
        }

        private void ClearAll()
        {
            if (_spawnedModels == null) return;
            foreach (var model in _spawnedModels)
            {
                if (model != null)
                    UnityEngine.Object.Destroy(model);
            }
            _spawnedModels.Clear();
        }

        private void OnDestroy()
        {
            if (_inventory != null)
                _inventory.Changed -= OnInventoryChanged;
            ClearAll();
        }
    }
}
