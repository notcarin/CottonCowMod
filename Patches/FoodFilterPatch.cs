using System.Collections.Generic;
using HarmonyLib;
using TotS.Inventory;
using TotS.Items;
using TotS.UI;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Allows valid cow food items to be transferred into the CowTrough inventory.
    /// Uses a prefix to skip the base CanTransfer entirely for CowTrough transfers,
    /// preventing the wrong animation from firing. For invalid items targeting CowTrough,
    /// triggers the rejection animation manually.
    /// </summary>
    [HarmonyPatch(typeof(StorageUIScreen), "CanTransfer")]
    public static class FoodFilterPatch
    {
        [HarmonyPrefix]
        static bool Prefix(
            StorageUIScreen __instance,
            ref bool __result,
            IEnumerable<Item> selectedItems,
            Inventory targetInventory,
            bool triggerAnimations)
        {
            // Only intercept for CowTrough
            if (targetInventory?.EditorName != "CowTrough")
                return true; // let original run

            // Check that all items are valid cow food and inventory has space
            foreach (var item in selectedItems)
            {
                if (item?.ItemType == null)
                {
                    TriggerRejectAnimation(__instance, triggerAnimations);
                    __result = false;
                    return false;
                }

                string itemName = item.ItemType.name;

                if (!CowDiet.IsValidCowFood(itemName))
                {
                    TriggerRejectAnimation(__instance, triggerAnimations);
                    __result = false;
                    return false;
                }

                if (!targetInventory.CanAdd(item))
                {
                    TriggerFullAnimation(__instance, triggerAnimations);
                    __result = false;
                    return false;
                }
            }

            __result = true;
            return false; // skip original — we handled it
        }

        private static void TriggerRejectAnimation(StorageUIScreen instance, bool triggerAnimations)
        {
            if (!triggerAnimations) return;

            try
            {
                var transferIcon = Traverse.Create(instance)
                    .Field("m_TransferIcon").GetValue();
                if (transferIcon != null)
                {
                    Traverse.Create(transferIcon)
                        .Method("TriggerAnimation", new[] { typeof(string) })
                        .GetValue("CantTransferInvalid");
                }
            }
            catch (System.Exception ex)
            {
                CottonCowModPlugin.Log.LogWarning(
                    $"FoodFilterPatch: Failed to trigger reject animation: {ex.Message}");
            }
        }

        private static void TriggerFullAnimation(StorageUIScreen instance, bool triggerAnimations)
        {
            if (!triggerAnimations) return;

            try
            {
                var transferIcon = Traverse.Create(instance)
                    .Field("m_TransferIcon").GetValue();
                if (transferIcon != null)
                {
                    Traverse.Create(transferIcon)
                        .Method("TriggerAnimation", new[] { typeof(string) })
                        .GetValue("CantTransferFull");
                }
            }
            catch (System.Exception ex)
            {
                CottonCowModPlugin.Log.LogWarning(
                    $"FoodFilterPatch: Failed to trigger full animation: {ex.Message}");
            }
        }
    }
}
