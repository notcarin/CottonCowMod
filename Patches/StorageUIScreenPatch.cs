using HarmonyLib;
using TotS.UI;
using UnityEngine;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Intercepts StorageUIScreen initialization to configure it for CowTrough use.
    /// Swaps the inventory name, clears the label, sets trough sounds, and hides
    /// category navigation — making it behave like the chicken trough UI.
    /// </summary>
    [HarmonyPatch(typeof(StorageUIScreen), "SetIndexes")]
    public static class StorageUIScreenPatch
    {
        [HarmonyPrefix]
        static void Prefix(StorageUIScreen __instance)
        {
            if (!CowTroughManager.IsOpeningCowTrough)
                return;

            var t = Traverse.Create(__instance);

            // Swap the inventory name to CowTrough before SetIndexes reads it
            t.Field("m_StorageInventoryName").SetValue("CowTrough");

            // Clear the storage label so it doesn't show "Pantry"
            t.Field("m_StorageNameLocKey").SetValue("");

            // Use trough sounds instead of pantry sounds
            t.Field("m_IsPantry").SetValue(false);

            CowTroughManager.IsOpeningCowTrough = false;

            CottonCowModPlugin.Log.LogInfo(
                "StorageUIScreenPatch: Configured UI for CowTrough.");
        }
    }

    /// <summary>
    /// Makes IsChickenTrough return true for CowTrough so that category navigation
    /// is disabled and nav tips are hidden — matching chicken trough behavior.
    ///
    /// IsChickenTrough is checked in SelectStorage (nav tips), OnNextCategory,
    /// OnPrevCategory, and UpdateTips. Patching the getter covers all call sites.
    /// </summary>
    [HarmonyPatch(typeof(StorageUIScreen), "get_IsChickenTrough")]
    public static class StorageUIIsTroughPatch
    {
        [HarmonyPostfix]
        static void Postfix(StorageUIScreen __instance, ref bool __result)
        {
            if (__result)
                return;

            var name = Traverse.Create(__instance)
                .Field("m_StorageInventoryName").GetValue<string>();
            if (name == "CowTrough")
                __result = true;
        }
    }

    /// <summary>
    /// After Init completes for CowTrough: force-hides nav tips (safety net in case
    /// IsChickenTrough was JIT-inlined) and adds the rejection text override component.
    /// </summary>
    [HarmonyPatch(typeof(StorageUIScreen), "Init")]
    public static class StorageUIInitPatch
    {
        [HarmonyPostfix]
        static void Postfix(StorageUIScreen __instance)
        {
            var t = Traverse.Create(__instance);
            var name = t.Field("m_StorageInventoryName").GetValue<string>();
            if (name != "CowTrough")
                return;

            // Hide category navigation tips
            var leftTip = t.Field("m_StorageNavLeftTip").GetValue<GameObject>();
            var rightTip = t.Field("m_StorageNavRightTip").GetValue<GameObject>();

            if (leftTip != null)
                leftTip.SetActive(false);
            if (rightTip != null)
                rightTip.SetActive(false);

            // Override rejection text with cow diet description
            var transferIcon = t.Field("m_TransferIcon").GetValue<object>();
            if (transferIcon is Component transferIconComponent)
            {
                var overrider = __instance.gameObject
                    .AddComponent<CowTroughRejectionTextOverride>();
                overrider.Initialize(transferIconComponent.transform);
            }
        }
    }
}
