using System;
using System.Reflection;
using HarmonyLib;
using TotS;
using TotS.Animals;
using TotS.Items;
using TotS.Inventory;
using UnityEngine;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Patches Animal's explicit IVerbQuickInteract.QuickInteractEnd to intercept
    /// interactions with player-owned cows that have milk available.
    /// When the player interacts with a milkable cow, creates an Ingredient_Milk item
    /// with the appropriate quality and adds it to the player's backpack (or mailbox if full).
    /// The normal greeting animation still plays.
    /// </summary>
    [HarmonyPatch]
    public static class AnimalInteractionPatch
    {
        static MethodBase TargetMethod()
        {
            try
            {
                // Animal explicitly implements Interactable.IVerbQuickInteract.QuickInteractEnd
                var interfaceType = typeof(Interactable.IVerbQuickInteract);
                var interfaceMethod = interfaceType.GetMethod("QuickInteractEnd");
                var map = typeof(Animal).GetInterfaceMap(interfaceType);

                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    if (map.InterfaceMethods[i] == interfaceMethod)
                    {
                        CottonCowModPlugin.Log.LogInfo(
                            $"AnimalInteractionPatch: Found target method: {map.TargetMethods[i].Name}");
                        return map.TargetMethods[i];
                    }
                }

                CottonCowModPlugin.Log.LogError(
                    "AnimalInteractionPatch: Could not find QuickInteractEnd in interface map!");
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"AnimalInteractionPatch: TargetMethod exception: {ex}");
            }
            return null;
        }

        [HarmonyPrefix]
        static void Prefix(Animal __instance, Holdable.QuickUsingEndReason reason)
        {
            try
            {
                if (reason != Holdable.QuickUsingEndReason.Used) return;

                var playerCow = __instance.GetComponent<PlayerOwnedCow>();
                if (playerCow == null || !playerCow.HasMilk) return;

                GiveMilkToPlayer(playerCow, __instance.gameObject);
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"AnimalInteractionPatch: Prefix exception: {ex}");
            }
        }

        private static void GiveMilkToPlayer(PlayerOwnedCow cow, GameObject cowGO)
        {
            // Find Ingredient_Milk ItemType
            var milkType = Singleton<ItemManager>.Instance.GetItemType(
                it => it.name == "Ingredient_Milk");

            if (milkType == null)
            {
                CottonCowModPlugin.Log.LogError(
                    "AnimalInteractionPatch: Could not find Ingredient_Milk ItemType! " +
                    "Check the exact item name in the game's item database.");
                return;
            }

            // Create milk with quality
            int quality = Mathf.Clamp(cow.MilkQuality, 1, 3);
            var variation = new Variation(milkType, quality);
            var milkItem = variation.CreateItem();

            var backpack = Singleton<InventoryManager>.Instance.Backpack;

            if (backpack.Add(milkItem, true)) // showGainedItemPrompt = true
            {
                CottonCowModPlugin.Log.LogInfo(
                    $"AnimalInteractionPatch: Delivered Q{quality} milk to backpack.");
            }
            else
            {
                // Backpack full — send to mailbox
                var mailbox = Singleton<InventoryManager>.Instance.Mailbox;
                mailbox.Add(milkItem);
                CottonCowModPlugin.Log.LogInfo(
                    $"AnimalInteractionPatch: Backpack full — Q{quality} milk sent to mailbox.");
            }

            // Clear milk flag
            cow.HasMilk = false;
            cow.MilkQuality = 0;

            // Persist cleared milk state to config
            bool isCow1 = cowGO == CowSpawner.GetCow1GO();
            CowProductionManager.SaveMilkState(isCow1, false, 0);

            // Update visual indicator
            var indicator = cowGO.GetComponent<CowMilkIndicator>();
            if (indicator != null)
                indicator.SetMilkAvailable(false);
        }
    }
}
