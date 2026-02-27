using System;
using HarmonyLib;
using TotS.Inventory;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Creates the CowTrough inventory during InventoryManager initialization,
    /// and initializes PlayerCows (subscribes to UnlockManager for cow unlock tracking).
    /// </summary>
    [HarmonyPatch(typeof(InventoryManager), "Awake")]
    public static class InventoryManagerPatch
    {
        [HarmonyPostfix]
        static void Postfix(InventoryManager __instance)
        {
            try
            {
                var cowTrough = __instance.CreateInventory("CowTrough");
                // Game config doesn't know about CowTrough, so capacity defaults to 0.
                cowTrough.UpdateCapacity(4);
                CowTroughManager.Instance.SetInventory(cowTrough);
                CottonCowModPlugin.Log.LogInfo("CowTrough inventory created with capacity 4.");
            }
            catch (ArgumentException)
            {
                var cowTrough = __instance.GetInventory("CowTrough");
                if (cowTrough.EmptyStacks == 0 && cowTrough.StackCount == 0)
                    cowTrough.UpdateCapacity(4);
                CowTroughManager.Instance.SetInventory(cowTrough);
                CottonCowModPlugin.Log.LogInfo("CowTrough inventory already existed.");
            }

            // Initialize PlayerCows — subscribe to UnlockManager and evaluate state.
            // UnlockManager should exist by this point (both are scene singletons).
            // Note: save data may not be loaded yet, so initial evaluation may show
            // all-false. The UnlockRequest subscription will trigger re-evaluation
            // when save data loads and unlocks are replayed.
            PlayerCows.Initialize();
        }
    }
}
