using HarmonyLib;
using TotS.Inventory;
using TotS.Items;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Holds a reference to the CowTrough inventory, the cloned CowFeedingTrough ItemType,
    /// and the cached UI prefab. Also provides a flag to signal StorageUIScreen to use
    /// "CowTrough" inventory.
    /// </summary>
    public class CowTroughManager
    {
        public static CowTroughManager Instance { get; } = new CowTroughManager();

        private Inventory _cowTrough;

        /// <summary>
        /// When true, the next StorageUIScreen.Init() call should use "CowTrough"
        /// instead of whatever inventory name is baked into the prefab.
        /// Set by CowTroughInteraction, cleared by StorageUIScreenPatch.
        /// </summary>
        public static bool IsOpeningCowTrough;

        /// <summary>
        /// Cached reference to the StorageUIScreen prefab (obtained from an existing
        /// PhysicalStorage in the scene, e.g. the chicken coop).
        /// </summary>
        public static GameObject CachedUIPrefab;

        /// <summary>
        /// The runtime-cloned ItemType for the cow feeding trough.
        /// Created by ItemManagerPatch from Trough_Metal with a unique SerialisedID.
        /// This is what distinguishes the cow trough from regular Metal Feeding Troughs.
        /// </summary>
        public static ItemType CowTroughItemType;

        /// <summary>
        /// Whether we've already given the player a CowFeedingTrough item.
        /// Uses a different string from the old Trough_Metal system so players
        /// migrating from the old version automatically receive the new item.
        /// Persisted via UnlockManager string so it survives save/load.
        /// </summary>
        public const string TroughGivenUnlockString = "COTTONCOWMOD_CowTroughGiven";

        public void SetInventory(Inventory inventory) => _cowTrough = inventory;
        public Inventory GetInventory() => _cowTrough;
        public bool HasInventory() => _cowTrough != null;

        /// <summary>
        /// Finds and caches the StorageUIScreen prefab from an existing PhysicalStorage.
        /// Prefers the chicken coop's prefab (simpler trough UI) over the pantry's.
        /// Falls back to any PhysicalStorage if the chicken coop isn't found.
        /// </summary>
        public static void CacheUIPrefabIfNeeded()
        {
            if (CachedUIPrefab != null)
                return;

            TotS.PhysicalStorage fallback = null;

            var allStorages = Object.FindObjectsOfType<TotS.PhysicalStorage>();
            foreach (var storage in allStorages)
            {
                if (storage.UIPrefab == null)
                    continue;

                // Prefer the chicken trough's PhysicalStorage — its UI prefab
                // has trough-appropriate settings (no pantry tabs, trough sounds)
                var storageInv = storage.StorageInventory;
                if (storageInv != null)
                {
                    var invName = Traverse.Create(storageInv)
                        .Field("m_InventoryName").GetValue<string>();
                    if (invName == "ChickenTrough")
                    {
                        CachedUIPrefab = storage.UIPrefab;
                        CottonCowModPlugin.Log.LogInfo(
                            $"CowTroughManager: Cached UIPrefab from chicken trough ({storage.gameObject.name}).");
                        return;
                    }
                }

                if (fallback == null)
                    fallback = storage;
            }

            // Fall back to any PhysicalStorage if chicken trough wasn't found
            if (fallback != null)
            {
                CachedUIPrefab = fallback.UIPrefab;
                CottonCowModPlugin.Log.LogInfo(
                    $"CowTroughManager: Chicken trough not found, cached UIPrefab from {fallback.gameObject.name}.");
            }
            else
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughManager: No PhysicalStorage found in scene to cache UIPrefab.");
            }
        }
    }
}
