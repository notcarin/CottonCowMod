using System.Reflection;
using HarmonyLib;
using TotS;
using TotS.BuySell;
using TotS.Data;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Fixes Cotton NPC shops becoming empty at levels 11-13.
    /// Shop stock entries have maxRelationshipLevel=10 (the vanilla cap).
    /// When our mod extends the cap to 13, the range check fails because
    /// 13 > 10. This patch treats entries whose max was at the old cap as
    /// still valid at extended levels, while items meant to rotate out at
    /// lower levels still do so correctly.
    /// </summary>
    [HarmonyPatch]
    public static class ShopRelationshipPatch
    {
        private const int VanillaLevelCap = 10;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var shopStockType = AccessTools.Inner(typeof(ShopStockHandler), "ShopStock");
            return AccessTools.Method(shopStockType, "CheckRelationshipRange",
                new[] { typeof(string), typeof(ShopStockEntryData) });
        }

        [HarmonyPostfix]
        static void Postfix(ref bool __result, string ownerID, ShopStockEntryData stockEntryData)
        {
            if (__result) return;
            if (!LevelCapPatches.IsCottonNPC(ownerID)) return;

            var relData = Singleton<RelationshipManager>.Instance.GetRelationshipData(ownerID);
            if (relData == null) return;

            int level = relData.CurrentRelationshipLevel;
            int minLevel = Traverse.Create(stockEntryData)
                .Field("m_MinRelationshipLevel").GetValue<int>();
            int maxLevel = Traverse.Create(stockEntryData)
                .Field("m_MaxRelationshipLevel").GetValue<int>();

            // If player is above the entry's max but the max was at the vanilla cap,
            // the item was "endgame" and should remain available at extended levels
            if (level >= minLevel && maxLevel >= VanillaLevelCap)
                __result = true;
        }
    }
}
