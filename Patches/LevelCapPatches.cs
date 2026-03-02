using System;
using HarmonyLib;
using TotS;
using TotS.Data;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Extends the relationship level cap from 10 to 13 for Cotton family NPCs.
    ///
    /// Two-phase approach:
    /// 1. Prefix on SetupData() modifies the database BEFORE RelationshipData objects are created
    ///    (handles fresh games where no save data exists).
    /// 2. Postfix on SetupData() patches the created RelationshipData objects directly
    ///    (handles the case where database entries might be value types).
    /// </summary>
    [HarmonyPatch(typeof(RelationshipManager), "SetupData")]
    public static class LevelCapPatches
    {
        internal const int NewMaxLevel = 13;
        internal const int XpPerAddedLevel = 150;

        internal static bool IsCottonNPC(string name)
        {
            return name == "Farmer_Cotton" || name == "Young_Tom_Cotton";
        }

        [HarmonyPrefix]
        static void Prefix(RelationshipManager __instance)
        {
            var database = Traverse.Create(__instance).Field("m_Database")
                .GetValue<RelationshipLevelsDatabase>();
            if (database == null)
            {
                CottonCowModPlugin.Log.LogWarning("LevelCapPatches: m_Database is null.");
                return;
            }

            foreach (var entry in database.Entries)
            {
                if (string.IsNullOrEmpty(entry.RelationshipTarget))
                    continue;

                if (!IsCottonNPC(entry.RelationshipTarget))
                    continue;

                Traverse.Create(entry).Field("m_MaxLevel").SetValue(NewMaxLevel);

                var xpArray = entry.XpRequiredToUnlockLevel;
                if (xpArray != null && xpArray.Length < NewMaxLevel)
                {
                    var extended = new int[NewMaxLevel];
                    Array.Copy(xpArray, extended, xpArray.Length);
                    for (int i = xpArray.Length; i < NewMaxLevel; i++)
                        extended[i] = XpPerAddedLevel;
                    Traverse.Create(entry).Field("m_XpRequiredToUnlockLevel").SetValue(extended);
                }

                CottonCowModPlugin.Log.LogInfo(
                    $"LevelCapPatches: Patched database entry {entry.RelationshipTarget}");
            }
        }

        [HarmonyPostfix]
        static void Postfix(RelationshipManager __instance)
        {
            PatchLoadedRelationshipData(__instance, "SetupData");
        }

        /// <summary>
        /// Patches the in-memory RelationshipData objects for Cotton NPCs.
        /// Modifies the LevelingHandler's readonly MaxLevel, LevelXpThresholds,
        /// and MaxXp fields via reflection. This is the definitive fix that works
        /// regardless of whether the data came from database or save file.
        /// </summary>
        internal static void PatchLoadedRelationshipData(RelationshipManager manager, string caller)
        {
            var allData = manager.AllRelationshipData;
            if (allData == null) return;

            foreach (var kvp in allData)
            {
                var data = kvp.Value;

                // Check both the dictionary key and the target name
                bool match = IsCottonNPC(kvp.Key)
                          || IsCottonNPC(data.RelationshipTargetName);
                if (!match)
                    continue;

                var handler = Traverse.Create(data).Field("m_LevelingHandler").GetValue();
                if (handler == null) continue;

                var ht = Traverse.Create(handler);

                // Patch the readonly MaxLevel field if needed
                if (data.MaxLevel < NewMaxLevel)
                    ht.Field("MaxLevel").SetValue(NewMaxLevel);

                // Extend or fix the LevelXpThresholds array
                var thresholds = ht.Field("LevelXpThresholds").GetValue<int[]>();
                if (thresholds != null)
                {
                    bool needsUpdate = thresholds.Length < NewMaxLevel
                        || thresholds[NewMaxLevel - 1] != XpPerAddedLevel;

                    if (needsUpdate)
                    {
                        var extended = new int[NewMaxLevel];
                        Array.Copy(thresholds, extended, Math.Min(thresholds.Length, NewMaxLevel));
                        for (int i = Math.Max(thresholds.Length, 10); i < NewMaxLevel; i++)
                            extended[i] = XpPerAddedLevel;
                        ht.Field("LevelXpThresholds").SetValue(extended);
                    }
                }

                // Recalculate the readonly MaxXp (sum of all thresholds)
                var newThresholds = ht.Field("LevelXpThresholds").GetValue<int[]>();
                if (newThresholds != null)
                {
                    int maxXp = 0;
                    for (int i = 0; i < newThresholds.Length; i++)
                        maxXp += newThresholds[i];
                    ht.Field("MaxXp").SetValue(maxXp);
                }

                CottonCowModPlugin.Log.LogInfo(
                    $"LevelCapPatches: [{caller}] Patched {kvp.Key} -> MaxLevel={data.MaxLevel}, L11-L13 XP={XpPerAddedLevel} each");
            }
        }
    }

    /// <summary>
    /// Re-patches Cotton NPC relationships after save data loads.
    /// ReadVersionData2 can create new RelationshipData objects from saved data
    /// that has the original MaxLevel=10, overwriting our SetupData modifications.
    /// </summary>
    [HarmonyPatch(typeof(RelationshipManager), "ReadVersionData2")]
    public static class LevelCapSaveLoadPatch
    {
        [HarmonyPostfix]
        static void Postfix(RelationshipManager __instance)
        {
            LevelCapPatches.PatchLoadedRelationshipData(__instance, "SaveLoad");
        }
    }
}
