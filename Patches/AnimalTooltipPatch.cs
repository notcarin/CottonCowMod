using System;
using System.Reflection;
using HarmonyLib;
using TotS;
using TotS.Animals;
using TotS.UI;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Patches Animal's explicit IVerbTouch.TouchUpdate to change the tooltip
    /// from "Greet" to "Collect Milk" when approaching a cow with milk available.
    /// Uses SetVariable("interactiontype", ...) to inject direct text or restore
    /// the localized default via SetVariableKey.
    /// </summary>
    [HarmonyPatch]
    public static class AnimalTooltipPatch
    {
        static MethodBase TargetMethod()
        {
            try
            {
                // Animal explicitly implements Interactable.IVerbTouch.TouchUpdate
                var interfaceType = typeof(Interactable.IVerbTouch);
                var interfaceMethod = interfaceType.GetMethod("TouchUpdate");
                var map = typeof(Animal).GetInterfaceMap(interfaceType);

                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    if (map.InterfaceMethods[i] == interfaceMethod)
                    {
                        CottonCowModPlugin.Log.LogInfo(
                            $"AnimalTooltipPatch: Found target method: {map.TargetMethods[i].Name}");
                        return map.TargetMethods[i];
                    }
                }

                CottonCowModPlugin.Log.LogError(
                    "AnimalTooltipPatch: Could not find TouchUpdate in interface map!");
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"AnimalTooltipPatch: TargetMethod exception: {ex}");
            }
            return null;
        }

        [HarmonyPostfix]
        static void Postfix(Animal __instance)
        {
            var playerCow = __instance.GetComponent<PlayerOwnedCow>();
            if (playerCow == null) return;

            var tip = Traverse.Create(__instance)
                .Field("m_InteractionToolTip")
                .GetValue<ToolTipManager.Tip>();
            if (tip == null) return;

            if (playerCow.HasMilk)
            {
                tip.SetVariable("interactiontype", "Collect Milk");
            }
            else
            {
                tip.SetVariableKey("interactiontype", "INTERACTION_GREETNPC");
            }
        }
    }
}
