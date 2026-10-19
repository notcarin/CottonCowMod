using HarmonyLib;
using TotS;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Caches the storage UI prefab for the cow trough as soon as any PhysicalStorage loads.
    /// Without this, a player with no chicken trough or other storage loaded when the cow
    /// trough initialised could never open the cow trough.
    /// </summary>
    [HarmonyPatch(typeof(PhysicalStorage), "Awake")]
    public static class PhysicalStoragePatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalStorage __instance)
        {
            CowTroughManager.CacheFrom(__instance);
        }
    }
}
