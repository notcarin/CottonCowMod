using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using TotS;
using TotS.Cooking;
using TotS.Data;
using TotS.Items;
using UnityEngine;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Injects level 11 relationship rewards for Cotton NPCs into the game's reward databases.
    /// Hooks RewardsManager.Awake so both the RewardsDatabase and RelationshipRewardsDatabase
    /// are guaranteed to be loaded.
    ///
    /// Farmer Cotton L11: 1 village point, 2x Ingredient_Milk (Q3), 1x Garden_Bed_Planks_Circle_Large
    /// Young Tom Cotton L11: 1 village point, 2x Ingredient_Milk (Q3), loot table roll
    ///
    /// Reward keys use "L11" instead of "Level_11" to avoid a substring match bug in
    /// RewardsManager.GetLevelOfReward() where Contains("Level_1") would misidentify priority.
    /// </summary>
    [HarmonyPatch(typeof(RewardsManager), "Awake")]
    public static class RelationshipRewardsPatch
    {
        private const string FarmerCottonRewardKey = "Reward_Farmer_Cotton_L11";
        private const string YoungTomRewardKey = "Reward_Young_Tom_Cotton_L11";

        [HarmonyPostfix]
        static void Postfix(RewardsManager __instance)
        {
            try
            {
                PatchRelationshipRewardsDatabase();
                PatchRewardsDatabase(__instance);
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"RelationshipRewardsPatch: Exception during reward injection: {ex}");
            }
        }

        /// <summary>
        /// Adds level 11 entries to the RelationshipRewardsDatabase for both Cotton NPCs.
        /// </summary>
        private static void PatchRelationshipRewardsDatabase()
        {
            if (!Singleton<RelationshipManager>.Exists)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "RelationshipRewardsPatch: RelationshipManager not available.");
                return;
            }

            var relMgr = Singleton<RelationshipManager>.Instance;
            var rewardsDb = Traverse.Create(relMgr)
                .Field("m_RelationshipRewardsDatabase")
                .GetValue<RelationshipRewardsDatabase>();

            if (rewardsDb == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "RelationshipRewardsPatch: RelationshipRewardsDatabase is null.");
                return;
            }

            var newEntries = new List<RelationshipRewardsDatabase.RelationshipRewardsDatabaseEntry>();

            foreach (var entry in rewardsDb.Entries)
            {
                if (entry.RelationshipTargetID == "Farmer_Cotton" ||
                    entry.RelationshipTargetID == "Young_Tom_Cotton")
                {
                    string rewardKey = entry.RelationshipTargetID == "Farmer_Cotton"
                        ? FarmerCottonRewardKey
                        : YoungTomRewardKey;

                    // Check if level 11 already exists
                    if (!string.IsNullOrEmpty(entry.GetKeyForLevel(11)))
                    {
                        CottonCowModPlugin.Log.LogInfo(
                            $"RelationshipRewardsPatch: {entry.RelationshipTargetID} already has L11 reward.");
                        newEntries.Add(entry);
                        continue;
                    }

                    // Read existing rewards array via reflection
                    var existingRewards = Traverse.Create(entry)
                        .Field("m_NPCRelationshipRewards")
                        .GetValue<RelationshipRewardsDatabase.RelationshipRewardsDatabaseEntry.NPCRelationshipRewards[]>();

                    var extendedRewards = existingRewards != null
                        ? existingRewards.ToList()
                        : new List<RelationshipRewardsDatabase.RelationshipRewardsDatabaseEntry.NPCRelationshipRewards>();

                    extendedRewards.Add(
                        new RelationshipRewardsDatabase.RelationshipRewardsDatabaseEntry.NPCRelationshipRewards(
                            11, rewardKey));

                    // Create new entry with extended rewards, preserving milestones
                    var newEntry = new RelationshipRewardsDatabase.RelationshipRewardsDatabaseEntry(
                        entry.RelationshipTargetID,
                        extendedRewards.ToArray(),
                        entry.GetMilestoneRewards);

                    newEntries.Add(newEntry);

                    CottonCowModPlugin.Log.LogInfo(
                        $"RelationshipRewardsPatch: Added L11 reward key \"{rewardKey}\" " +
                        $"for {entry.RelationshipTargetID}.");
                }
                else
                {
                    newEntries.Add(entry);
                }
            }

            rewardsDb.Replace(newEntries);
        }

        /// <summary>
        /// Adds RewardsLevel entries for both Cotton NPC level 11 rewards to the RewardsDatabase.
        /// </summary>
        private static void PatchRewardsDatabase(RewardsManager rewardsMgr)
        {
            var rewardsDb = Traverse.Create(rewardsMgr)
                .Field("m_Database").GetValue<RewardsDatabase>();

            if (rewardsDb == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "RelationshipRewardsPatch: RewardsDatabase is null.");
                return;
            }

            // Check if already patched
            if (rewardsDb.TryGetReward(FarmerCottonRewardKey, out _))
            {
                CottonCowModPlugin.Log.LogInfo(
                    "RelationshipRewardsPatch: RewardsDatabase already has L11 entries.");
                return;
            }

            // Find required ItemTypes
            var milkType = FindItemType("Ingredient_Milk");
            var gardenBedType = FindItemType("Garden_Bed_Planks_Circle_Large");

            if (milkType == null)
            {
                CottonCowModPlugin.Log.LogError(
                    "RelationshipRewardsPatch: Could not find Ingredient_Milk ItemType!");
                return;
            }

            var entries = rewardsDb.Entries.ToList();

            // Farmer Cotton: 1 VP, 2x Milk (Q3), 1x Large Circle Garden Bed
            var farmerItems = new List<RewardsDatabase.ItemReward>();
            farmerItems.Add(CreateItemReward(milkType, 2, 3));
            if (gardenBedType != null)
                farmerItems.Add(CreateItemReward(gardenBedType, 1, 1));
            else
                CottonCowModPlugin.Log.LogWarning(
                    "RelationshipRewardsPatch: Garden_Bed_Planks_Circle_Large not found, " +
                    "Farmer Cotton reward will only include milk.");

            entries.Add(CreateRewardsLevel(
                FarmerCottonRewardKey,
                villagePoints: 1,
                items: farmerItems.ToArray(),
                lootTableIDs: new string[0]));

            // Young Tom: 1 VP, 2x Milk (Q3), loot table roll
            entries.Add(CreateRewardsLevel(
                YoungTomRewardKey,
                villagePoints: 1,
                items: new[] { CreateItemReward(milkType, 2, 3) },
                lootTableIDs: new[] { "Craving_TomC_Basic_Table" }));

            rewardsDb.Replace(entries);

            CottonCowModPlugin.Log.LogInfo(
                "RelationshipRewardsPatch: Injected L11 reward entries for both Cotton NPCs.");
        }

        private static ItemType FindItemType(string name)
        {
            // Search all loaded ItemType assets
            var allTypes = Resources.FindObjectsOfTypeAll<ItemType>();
            foreach (var t in allTypes)
            {
                if (t.name == name)
                    return t;
            }
            return null;
        }

        private static RewardsDatabase.ItemReward CreateItemReward(ItemType itemType, int count, int quality)
        {
            var config = new VariationConfig(itemType, quality, FlavourProfile.FlavourType.None);

            var itemReward = (RewardsDatabase.ItemReward)
                FormatterServices.GetUninitializedObject(typeof(RewardsDatabase.ItemReward));

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(RewardsDatabase.ItemReward);
            type.GetField("m_VariationConfig", flags).SetValue(itemReward, config);
            type.GetField("m_Count", flags).SetValue(itemReward, count);

            return itemReward;
        }

        private static RewardsDatabase.RewardsLevel CreateRewardsLevel(
            string rewardID,
            int villagePoints,
            RewardsDatabase.ItemReward[] items,
            string[] lootTableIDs)
        {
            var level = (RewardsDatabase.RewardsLevel)
                FormatterServices.GetUninitializedObject(typeof(RewardsDatabase.RewardsLevel));

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(RewardsDatabase.RewardsLevel);

            type.GetField("m_RewardID", flags).SetValue(level, rewardID);
            type.GetField("m_RelationshipPoints", flags).SetValue(level, 0);
            type.GetField("m_VillagePoints", flags).SetValue(level, villagePoints);
            type.GetField("m_Coins", flags).SetValue(level, 0);
            type.GetField("m_ChoiceOfItem", flags).SetValue(level, false);
            type.GetField("m_ClubReward", flags).SetValue(level,
                new RewardsDatabase.ClubReward("", 0));
            type.GetField("m_RewardItems", flags).SetValue(level, items);
            type.GetField("m_LootTableIDs", flags).SetValue(level, lootTableIDs);
            type.GetField("m_UnlockIDs", flags).SetValue(level, new string[0]);

            return level;
        }
    }
}
