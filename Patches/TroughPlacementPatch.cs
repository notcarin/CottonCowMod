using HarmonyLib;
using TotS;
using TotS.Items;
using UnityEngine;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// When a CowFeedingTrough item is placed as a decoration, adds the
    /// CowTroughInteraction component so it functions as a cow trough.
    ///
    /// Only activates for our custom CowFeedingTrough ItemType (cloned from
    /// Trough_Metal by ItemManagerPatch). Regular Metal Feeding Troughs remain
    /// normal decorations.
    ///
    /// Also sets ItemComponent.ItemType to the cloned type so the save system
    /// persists "CowFeedingTrough" instead of "Trough_Metal".
    /// </summary>
    [HarmonyPatch(typeof(PlaceableAspect), "InstantiatePrefab")]
    public static class TroughPlacementPatch
    {
        [HarmonyPostfix]
        static void Postfix(PlaceableAspect __instance, Item item, ref GameObject __result)
        {
            if (__result == null)
                return;

            // Only activate for our custom CowFeedingTrough, not regular Trough_Metal
            if (item?.ItemType == null || item.ItemType.name != "CowFeedingTrough")
                return;

            // Don't add duplicate components
            if (__result.GetComponent<CowTroughInteraction>() != null)
                return;

            __result.AddComponent<CowTroughInteraction>();
            __result.AddComponent<CowTroughFoodDisplay>();

            // Ensure ItemComponent references our cloned type (not the prefab's baked-in
            // Trough_Metal). Critical for save/load: DynamicStreamedItem reads SerialisedID
            // from ItemComponent.ItemType. For the auto-placement path (TryAutoPlaceTrough),
            // Item.AttachToMonoBehaviours doesn't run, so we set it manually here.
            // For the normal placement path (SwitchContext), AttachTo sets it to the same
            // value afterward — no conflict.
            var itemComponent = __result.GetComponent<ItemComponent>();
            if (itemComponent != null)
                itemComponent.ItemType = item.ItemType;

            CottonCowModPlugin.Log.LogInfo(
                "TroughPlacementPatch: Added CowTroughInteraction to placed CowFeedingTrough.");
        }
    }
}
