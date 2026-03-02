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
    /// Injects level 11–13 relationship rewards for Cotton NPCs into the game's reward databases.
    /// Hooks RewardsManager.Awake so both the RewardsDatabase and RelationshipRewardsDatabase
    /// are guaranteed to be loaded.
    ///
    /// Farmer Cotton:  L11 produce, L12 produce, L13 milk + garden bed (cow unlock)
    /// Young Tom Cotton: L11 dairy, L12 meats, L13 milk + loot roll (cow unlock)
    ///
    /// Reward keys use "L11" instead of "Level_11" to avoid a substring match bug in
    /// RewardsManager.GetLevelOfReward() where Contains("Level_1") would misidentify priority.
    /// </summary>
    [HarmonyPatch(typeof(RewardsManager), "Awake")]
    public static class RelationshipRewardsPatch
    {
        private static readonly string[] FarmerCottonRewardKeys =
        {
            "Reward_Farmer_Cotton_L11",
            "Reward_Farmer_Cotton_L12",
            "Reward_Farmer_Cotton_L13"
        };

        private static readonly string[] YoungTomRewardKeys =
        {
            "Reward_Young_Tom_Cotton_L11",
            "Reward_Young_Tom_Cotton_L12",
            "Reward_Young_Tom_Cotton_L13"
        };

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
        /// Adds level 11–13 entries to the RelationshipRewardsDatabase for both Cotton NPCs.
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
                if (entry.RelationshipTargetID != "Farmer_Cotton" &&
                    entry.RelationshipTargetID != "Young_Tom_Cotton")
                {
                    newEntries.Add(entry);
                    continue;
                }

                var rewardKeys = entry.RelationshipTargetID == "Farmer_Cotton"
                    ? FarmerCottonRewardKeys
                    : YoungTomRewardKeys;

                // Check if already fully patched (L13 present)
                if (!string.IsNullOrEmpty(entry.GetKeyForLevel(13)))
                {
                    CottonCowModPlugin.Log.LogInfo(
                        $"RelationshipRewardsPatch: {entry.RelationshipTargetID} already has L11-L13 rewards.");
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

                // Remove any stale L11 entry from previous mod version
                extendedRewards.RemoveAll(r => r.RelationshipLevel >= 11);

                // Add L11, L12, L13
                for (int i = 0; i < 3; i++)
                {
                    extendedRewards.Add(
                        new RelationshipRewardsDatabase.RelationshipRewardsDatabaseEntry.NPCRelationshipRewards(
                            11 + i, rewardKeys[i]));
                }

                var newEntry = new RelationshipRewardsDatabase.RelationshipRewardsDatabaseEntry(
                    entry.RelationshipTargetID,
                    extendedRewards.ToArray(),
                    entry.GetMilestoneRewards);

                newEntries.Add(newEntry);

                CottonCowModPlugin.Log.LogInfo(
                    $"RelationshipRewardsPatch: Added L11-L13 reward keys for {entry.RelationshipTargetID}.");
            }

            rewardsDb.Replace(newEntries);
        }

        /// <summary>
        /// Adds RewardsLevel entries for Cotton NPC level 11–13 rewards to the RewardsDatabase.
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

            // Check if already patched (L13 is the last one we add)
            if (rewardsDb.TryGetReward(FarmerCottonRewardKeys[2], out _))
            {
                CottonCowModPlugin.Log.LogInfo(
                    "RelationshipRewardsPatch: RewardsDatabase already has L11-L13 entries.");
                return;
            }

            // Find required ItemTypes
            var milkType = FindItemType("Ingredient_Milk");
            var gardenBedType = FindItemType("Garden_Bed_Planks_Circle_Large");
            var parsnipType = FindItemType("Ingredient_Parsnip");
            var radishType = FindItemType("Ingredient_Radish");
            var boxPeppersType = FindItemType("Ingredient_Box_Peppers");
            var spinachType = FindItemType("Ingredient_Spinach");
            var tomatoType = FindItemType("Ingredient_Tomato");
            var creamType = FindItemType("Ingredient_Cream");
            var butterType = FindItemType("Seasoning_Butter");
            var eggType = FindItemType("Ingredient_Egg");
            var muttonType = FindItemType("Ingredient_Meat_Mutton");
            var beefType = FindItemType("Ingredient_Meat_Beef");

            if (milkType == null)
            {
                CottonCowModPlugin.Log.LogError(
                    "RelationshipRewardsPatch: Could not find Ingredient_Milk ItemType!");
                return;
            }

            // Remove any stale L11-only entries from previous mod version
            var entries = rewardsDb.Entries.ToList();
            entries.RemoveAll(e => e.RewardID == "Reward_Farmer_Cotton_L11" ||
                                   e.RewardID == "Reward_Young_Tom_Cotton_L11");

            // --- Farmer Cotton ---

            // L11: 2x Parsnip(Q3), 1x Radish(Q2)
            var farmerL11Items = new List<RewardsDatabase.ItemReward>();
            if (parsnipType != null) farmerL11Items.Add(CreateItemReward(parsnipType, 2, 3));
            if (radishType != null) farmerL11Items.Add(CreateItemReward(radishType, 1, 2));
            entries.Add(CreateRewardsLevel(FarmerCottonRewardKeys[0],
                villagePoints: 1, items: farmerL11Items.ToArray(), lootTableIDs: new string[0]));

            // L12: 1x Box Peppers(Q3), 1x Spinach(Q3), 1x Tomato(Q3)
            var farmerL12Items = new List<RewardsDatabase.ItemReward>();
            if (boxPeppersType != null) farmerL12Items.Add(CreateItemReward(boxPeppersType, 1, 3));
            if (spinachType != null) farmerL12Items.Add(CreateItemReward(spinachType, 1, 3));
            if (tomatoType != null) farmerL12Items.Add(CreateItemReward(tomatoType, 1, 3));
            entries.Add(CreateRewardsLevel(FarmerCottonRewardKeys[1],
                villagePoints: 1, items: farmerL12Items.ToArray(), lootTableIDs: new string[0]));

            // L13: 2x Milk(Q3), 1x Garden Bed (cow unlock milestone)
            var farmerL13Items = new List<RewardsDatabase.ItemReward>();
            farmerL13Items.Add(CreateItemReward(milkType, 2, 3));
            if (gardenBedType != null)
                farmerL13Items.Add(CreateItemReward(gardenBedType, 1, 1));
            entries.Add(CreateRewardsLevel(FarmerCottonRewardKeys[2],
                villagePoints: 1, items: farmerL13Items.ToArray(), lootTableIDs: new string[0]));

            // --- Young Tom Cotton ---

            // L11: 1x Cream(Q2), 1x Butter(Q3), 1x Egg(Q3)
            var tomL11Items = new List<RewardsDatabase.ItemReward>();
            if (creamType != null) tomL11Items.Add(CreateItemReward(creamType, 1, 2));
            if (butterType != null) tomL11Items.Add(CreateItemReward(butterType, 1, 3));
            if (eggType != null) tomL11Items.Add(CreateItemReward(eggType, 1, 3));
            entries.Add(CreateRewardsLevel(YoungTomRewardKeys[0],
                villagePoints: 1, items: tomL11Items.ToArray(), lootTableIDs: new string[0]));

            // L12: 1x Mutton(Q3), 1x Cream(Q3), 1x Beef(Q3)
            var tomL12Items = new List<RewardsDatabase.ItemReward>();
            if (muttonType != null) tomL12Items.Add(CreateItemReward(muttonType, 1, 3));
            if (creamType != null) tomL12Items.Add(CreateItemReward(creamType, 1, 3));
            if (beefType != null) tomL12Items.Add(CreateItemReward(beefType, 1, 3));
            entries.Add(CreateRewardsLevel(YoungTomRewardKeys[1],
                villagePoints: 1, items: tomL12Items.ToArray(), lootTableIDs: new string[0]));

            // L13: 2x Milk(Q3), loot roll (cow unlock milestone)
            entries.Add(CreateRewardsLevel(YoungTomRewardKeys[2],
                villagePoints: 1,
                items: new[] { CreateItemReward(milkType, 2, 3) },
                lootTableIDs: new[] { "Craving_TomC_Basic_Table" }));

            rewardsDb.Replace(entries);

            CottonCowModPlugin.Log.LogInfo(
                "RelationshipRewardsPatch: Injected L11-L13 reward entries for both Cotton NPCs.");
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
