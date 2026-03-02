using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TotS;
using TotS.Items;
using UnityEngine;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// After ItemManager registers all built-in ItemTypes, clones the Trough_Metal
    /// as a unique "CowFeedingTrough" ItemType and registers it. This ensures the
    /// cow trough is a distinct item from the regular Metal Feeding Trough decoration.
    ///
    /// Clones PlaceableAspect separately so that adding the gardening trait to
    /// CowFeedingTrough doesn't also add it to the original Trough_Metal.
    ///
    /// Runs before save loading, so saved CowFeedingTrough items are resolved correctly.
    /// </summary>
    [HarmonyPatch(typeof(ItemManager), "Awake")]
    public static class ItemManagerPatch
    {
        [HarmonyPostfix]
        static void Postfix(ItemManager __instance)
        {
            RegisterCowTroughItemType(__instance);
        }

        private static void RegisterCowTroughItemType(ItemManager itemManager)
        {
            try
            {
                // Find the original Metal Feeding Trough
                var troughMetal = itemManager.GetItemType(it => it.name == "Trough_Metal");
                if (troughMetal == null)
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "ItemManagerPatch: Trough_Metal ItemType not found, cannot create CowFeedingTrough.");
                    return;
                }

                // Clone the ItemType ScriptableObject (shallow copy — shares the same Aspects)
                var clone = Object.Instantiate(troughMetal);
                clone.name = "CowFeedingTrough";

                // Assign a unique SerialisedID so save/load can distinguish it from Trough_Metal
                var newID = SerialisedID.Create("CowFeedingTrough");
                Traverse.Create(clone).Field("m_TypeID").SetValue(newID);

                // The shallow copy shares Aspect ScriptableObject instances with the original.
                // We must clone aspects we intend to modify so we don't affect Trough_Metal.
                CloneAndFixAspects(clone);

                // Register with ItemManager's primary dictionary (keyed by SerialisedID)
                var dict = Traverse.Create(itemManager).Field("m_ItemTypes")
                    .GetValue<Dictionary<SerialisedID, ItemType>>();

                if (dict == null)
                {
                    CottonCowModPlugin.Log.LogError(
                        "ItemManagerPatch: Could not access m_ItemTypes dictionary.");
                    return;
                }

                dict[newID] = clone;

                // Store reference for other systems
                CowTroughManager.CowTroughItemType = clone;

                // Add the gardening trait so the trough appears in the Garden tab
                AddGardeningTrait(clone);

                CottonCowModPlugin.Log.LogInfo(
                    $"ItemManagerPatch: Registered CowFeedingTrough (SerialisedID={clone.SerialisedID}).");
            }
            catch (System.Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"ItemManagerPatch: Failed to register CowFeedingTrough: {ex}");
            }
        }

        /// <summary>
        /// Clones PlaceableAspect and PreviewAspect so modifications to CowFeedingTrough
        /// don't bleed through to the original Trough_Metal. Updates m_ItemType on each
        /// cloned aspect to point to the new ItemType, and replaces the references in
        /// the clone's m_Aspects list.
        /// </summary>
        private static void CloneAndFixAspects(ItemType clone)
        {
            var aspectsList = Traverse.Create(clone).Field("m_Aspects")
                .GetValue<List<Aspect>>();
            if (aspectsList == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "ItemManagerPatch: Could not access m_Aspects list on clone.");
                return;
            }

            for (int i = 0; i < aspectsList.Count; i++)
            {
                var aspect = aspectsList[i];
                if (aspect is PlaceableAspect || aspect is PreviewAspect)
                {
                    var clonedAspect = Object.Instantiate(aspect);
                    // Point the cloned aspect's m_ItemType to our new ItemType
                    Traverse.Create(clonedAspect).Field("m_ItemType").SetValue(clone);
                    aspectsList[i] = clonedAspect;

                    CottonCowModPlugin.Log.LogInfo(
                        $"ItemManagerPatch: Cloned {aspect.GetType().Name} for CowFeedingTrough.");
                }
            }
        }

        /// <summary>
        /// Finds the gardening Trait used by the Garden tab filter and adds it
        /// to the CowFeedingTrough's (cloned) PlaceableAspect.m_Traits array.
        /// Because PlaceableAspect was cloned in CloneAndFixAspects, this only
        /// affects CowFeedingTrough, not the original Trough_Metal.
        /// </summary>
        private static void AddGardeningTrait(ItemType cowTrough)
        {
            try
            {
                // Find the gardening trait by looking at DecorationInventoryPageController
                // which holds the Gardening tab config with its PlaceableAspectFilter
                Trait gardeningTrait = null;

                var pageControllers = Resources.FindObjectsOfTypeAll<DecorationInventoryPageController>();
                foreach (var controller in pageControllers)
                {
                    var gardeningConfig = Traverse.Create(controller)
                        .Field("m_Gardening").GetValue();
                    if (gardeningConfig == null) continue;

                    var filters = Traverse.Create(gardeningConfig)
                        .Field("m_CategoryFilters").GetValue<IInventoryDisplayFilter[]>();
                    if (filters == null) continue;

                    foreach (var filter in filters)
                    {
                        if (filter is PlaceableAspectFilter paf)
                        {
                            gardeningTrait = paf.FilterTrait;
                            break;
                        }
                    }

                    if (gardeningTrait != null) break;
                }

                // Fallback: search all traits by name
                if (gardeningTrait == null)
                {
                    var allTraits = Resources.FindObjectsOfTypeAll<Trait>();
                    foreach (var t in allTraits)
                    {
                        if (t.name.ToLower().Contains("garden"))
                        {
                            gardeningTrait = t;
                            break;
                        }
                    }
                }

                if (gardeningTrait == null)
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "ItemManagerPatch: Could not find gardening trait. " +
                        "Trough won't appear in Garden tab.");
                    return;
                }

                // Get the (now cloned) PlaceableAspect from the CowFeedingTrough
                var placeableAspect = cowTrough.GetAspect<PlaceableAspect>();
                if (placeableAspect == null)
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "ItemManagerPatch: CowFeedingTrough has no PlaceableAspect.");
                    return;
                }

                // Extend the m_Traits array to include the gardening trait
                var existingTraits = placeableAspect.Traits;
                if (existingTraits != null && existingTraits.Contains(gardeningTrait))
                {
                    CottonCowModPlugin.Log.LogInfo(
                        "ItemManagerPatch: CowFeedingTrough already has gardening trait.");
                    return;
                }

                int oldLen = existingTraits?.Length ?? 0;
                var newTraits = new Trait[oldLen + 1];
                if (existingTraits != null)
                    System.Array.Copy(existingTraits, newTraits, oldLen);
                newTraits[oldLen] = gardeningTrait;

                Traverse.Create(placeableAspect).Field("m_Traits").SetValue(newTraits);

                CottonCowModPlugin.Log.LogInfo(
                    $"ItemManagerPatch: Added gardening trait '{gardeningTrait.name}' to CowFeedingTrough " +
                    $"({oldLen} → {newTraits.Length} traits).");
            }
            catch (System.Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"ItemManagerPatch: AddGardeningTrait exception: {ex}");
            }
        }
    }

    /// <summary>
    /// Patches ItemTypeExtensions.DisplayName() and Description() to return custom
    /// text for the CowFeedingTrough. Covers tooltips, sorting, and other code paths
    /// that call these extension methods.
    /// </summary>
    [HarmonyPatch(typeof(ItemTypeExtensions))]
    public static class CowTroughDisplayPatch
    {
        internal const string TroughDisplayName = "Cow Feeding Trough";
        internal const string TroughDescription =
            "A sturdy trough for your cow. Fill it with vegetables, grains, or apples daily.";

        [HarmonyPatch("DisplayName")]
        [HarmonyPostfix]
        static void DisplayNamePostfix(ItemType itemType, ref string __result)
        {
            if (itemType != null && itemType.name == "CowFeedingTrough")
                __result = TroughDisplayName;
        }

        [HarmonyPatch("Description")]
        [HarmonyPostfix]
        static void DescriptionPostfix(ItemType itemType, ref string __result)
        {
            if (itemType != null && itemType.name == "CowFeedingTrough")
                __result = TroughDescription;
        }
    }

    /// <summary>
    /// Patches LocalizedText.UpdateText() to intercept CowFeedingTrough localization keys.
    /// The storage UI grid uses PreviewAspect.DisplayNameKey → LocalizedText → Articy,
    /// bypassing ItemTypeExtensions entirely. Since no Articy entry exists for our cloned
    /// item, we intercept the keys here and set the text directly.
    /// </summary>
    [HarmonyPatch(typeof(LocalizedText), "UpdateText")]
    public static class CowTroughLocalizedTextPatch
    {
        [HarmonyPostfix]
        static void Postfix(LocalizedText __instance)
        {
            var key = __instance.Key;
            if (key == "CowFeedingTrough.DisplayName")
                __instance.Text.text = CowTroughDisplayPatch.TroughDisplayName;
            else if (key == "CowFeedingTrough.Description")
                __instance.Text.text = CowTroughDisplayPatch.TroughDescription;
        }
    }
}
