using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Then restores the XP beyond vanilla's max that RelationshipSavePatch stored.
    /// </summary>
    [HarmonyPatch(typeof(RelationshipManager), "ReadVersionData2")]
    public static class LevelCapSaveLoadPatch
    {
        [HarmonyPostfix]
        static void Postfix(RelationshipManager __instance)
        {
            LevelCapPatches.PatchLoadedRelationshipData(__instance, "SaveLoad");
            RelationshipSavePatch.RestoreExtraXp(__instance);
        }
    }

    /// <summary>
    /// Keeps saves loadable without the mod.
    ///
    /// The save stores each relationship's max level, XP thresholds and XP. If it holds
    /// Cotton levels 11-13 and the mod is removed, the game rebuilds the relationship from
    /// those saved values, and the Cottons' shops go permanently empty (vanilla shop stock
    /// only covers levels up to 10).
    ///
    /// So while the game writes relationships, Cotton data is temporarily set back to vanilla
    /// values (max level 10, XP capped at vanilla's max). The real XP is stored as
    /// "COTTONCOWMOD_XP_&lt;npc&gt;=&lt;xp&gt;" in RelationshipManager's own unlock strings, which are
    /// written in the same record and ignored by vanilla. RestoreExtraXp puts it back on load.
    ///
    /// Saves broken by earlier versions (saved with max level 13) are repaired the next time
    /// they're saved with this version installed.
    /// </summary>
    [HarmonyPatch(typeof(RelationshipManager), "WriteVersionData2")]
    public static class RelationshipSavePatch
    {
        internal const int VanillaMaxLevel = 10;
        private const string XpPrefix = "COTTONCOWMOD_XP_";

        private class HandlerSnapshot
        {
            public Traverse Handler;
            public int MaxLevel;
            public int[] Thresholds;
            public int MaxXp;
            public int CurrentXp;
            public int CurrentLevel;
        }

        [HarmonyPrefix]
        static void Prefix(RelationshipManager __instance, out List<HandlerSnapshot> __state)
        {
            __state = new List<HandlerSnapshot>();
            try
            {
                var unlockStrings = UnlockStrings(__instance);
                foreach (var kvp in __instance.AllRelationshipData)
                {
                    if (!LevelCapPatches.IsCottonNPC(kvp.Key))
                        continue;

                    var handler = Traverse.Create(Traverse.Create(kvp.Value).Field("m_LevelingHandler").GetValue());
                    var thresholds = handler.Field("LevelXpThresholds").GetValue<int[]>();
                    if (thresholds == null || thresholds.Length <= VanillaMaxLevel)
                        continue;

                    var snapshot = new HandlerSnapshot
                    {
                        Handler = handler,
                        MaxLevel = handler.Field("MaxLevel").GetValue<int>(),
                        Thresholds = thresholds,
                        MaxXp = handler.Field("MaxXp").GetValue<int>(),
                        CurrentXp = handler.Field("m_CurrentXP").GetValue<int>(),
                        CurrentLevel = handler.Field("m_CurrentLevel").GetValue<int>()
                    };
                    __state.Add(snapshot);

                    // Store the real XP where vanilla ignores it
                    string prefix = XpPrefix + kvp.Key + "=";
                    unlockStrings.RemoveAll(s => s.StartsWith(prefix));
                    unlockStrings.Add(prefix + snapshot.CurrentXp.ToString(CultureInfo.InvariantCulture));

                    // Write vanilla values
                    var vanillaThresholds = new int[VanillaMaxLevel];
                    Array.Copy(thresholds, vanillaThresholds, VanillaMaxLevel);
                    int vanillaMaxXp = 0;
                    foreach (int xp in vanillaThresholds)
                        vanillaMaxXp += xp;

                    handler.Field("LevelXpThresholds").SetValue(vanillaThresholds);
                    handler.Field("MaxLevel").SetValue(VanillaMaxLevel);
                    handler.Field("MaxXp").SetValue(vanillaMaxXp);
                    handler.Field("m_CurrentXP").SetValue(Math.Min(snapshot.CurrentXp, vanillaMaxXp));
                    handler.Field("m_CurrentLevel").SetValue(Math.Min(snapshot.CurrentLevel, VanillaMaxLevel));
                }
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"RelationshipSavePatch: Prefix exception: {ex}");
            }
        }

        /// <summary>
        /// Puts the real values back after the write, even if the write threw.
        /// </summary>
        [HarmonyFinalizer]
        static void Finalizer(List<HandlerSnapshot> __state)
        {
            if (__state == null)
                return;

            foreach (var snapshot in __state)
            {
                snapshot.Handler.Field("LevelXpThresholds").SetValue(snapshot.Thresholds);
                snapshot.Handler.Field("MaxLevel").SetValue(snapshot.MaxLevel);
                snapshot.Handler.Field("MaxXp").SetValue(snapshot.MaxXp);
                snapshot.Handler.Field("m_CurrentXP").SetValue(snapshot.CurrentXp);
                snapshot.Handler.Field("m_CurrentLevel").SetValue(snapshot.CurrentLevel);
            }
        }

        /// <summary>
        /// Restores XP above vanilla's max for Cotton NPCs, after the level cap has been
        /// extended. Sets the fields directly: going through SetXp would raise LevelChanged,
        /// re-queueing the level 11-13 rewards and the cow unlock.
        /// </summary>
        internal static void RestoreExtraXp(RelationshipManager manager)
        {
            try
            {
                var unlockStrings = UnlockStrings(manager);
                foreach (var kvp in manager.AllRelationshipData)
                {
                    if (!LevelCapPatches.IsCottonNPC(kvp.Key))
                        continue;

                    string prefix = XpPrefix + kvp.Key + "=";
                    string stored = unlockStrings.Find(s => s.StartsWith(prefix));
                    if (stored == null ||
                        !int.TryParse(stored.Substring(prefix.Length), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int storedXp))
                        continue;

                    var handler = Traverse.Create(Traverse.Create(kvp.Value).Field("m_LevelingHandler").GetValue());
                    int currentXp = handler.Field("m_CurrentXP").GetValue<int>();
                    if (storedXp <= currentXp)
                        continue;

                    int xp = Math.Min(storedXp, handler.Field("MaxXp").GetValue<int>());
                    int level = handler.Method("CalculateLevelFromXP", xp).GetValue<int>();
                    handler.Field("m_CurrentXP").SetValue(xp);
                    handler.Field("m_CurrentLevel").SetValue(level);

                    CottonCowModPlugin.Log.LogInfo(
                        $"RelationshipSavePatch: Restored {kvp.Key} to XP {xp} (level {level}).");
                }
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"RelationshipSavePatch: RestoreExtraXp exception: {ex}");
            }
        }

        private static List<string> UnlockStrings(RelationshipManager manager)
        {
            var field = Traverse.Create(manager).Field("m_UnlockStrings");
            var list = field.GetValue<List<string>>();
            if (list == null)
            {
                list = new List<string>();
                field.SetValue(list);
            }
            return list;
        }
    }
}
