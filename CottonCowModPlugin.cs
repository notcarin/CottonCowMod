using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CottonCowMod.Patches;
using HarmonyLib;
using TotS;
using TotS.Inventory;
using TotS.Unlock;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CottonCowMod
{
    [BepInPlugin("com.cottoncowmod.tots", "Cotton Cow Mod", "0.1.0")]
    public class CottonCowModPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony _harmony;

        // --- Debug ---
        internal static ConfigEntry<bool> DebugForceUnlock;

        // --- Spawning ---
        internal static ConfigEntry<string> CowSpawnPosition;
        internal static ConfigEntry<string> TroughSpawnPosition;
        internal static ConfigEntry<Key> TagPositionKey;
        internal static ConfigEntry<Key> TagTroughPositionKey;
        internal static ConfigEntry<float> CowRoamRadius;

        // --- Streak persistence (per-cow, survives save/load) ---
        internal static ConfigEntry<float> Cow1Streak;
        internal static ConfigEntry<float> Cow2Streak;

        // --- Milk state persistence (survives save/load) ---
        internal static ConfigEntry<bool> Cow1HasMilk;
        internal static ConfigEntry<int> Cow1MilkQuality;
        internal static ConfigEntry<bool> Cow2HasMilk;
        internal static ConfigEntry<int> Cow2MilkQuality;

        // --- Pending activation (deferred spawn, survives save/quit/reload) ---
        internal static ConfigEntry<bool> Cow1PendingActivation;
        internal static ConfigEntry<bool> Cow2PendingActivation;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Cotton Cow Mod v0.1.0 loading...");

            // Debug
            DebugForceUnlock = Config.Bind("Debug", "ForceUnlock", false,
                "Forces both cow unlock gates to true (bypasses relationship + garden area checks). For testing only.");

            // Spawning
            CowSpawnPosition = Config.Bind("Spawning", "CowSpawnPosition", "",
                "Custom cow spawn position as \"x,y,z\". Press TagPositionKey (F9) in-game to set.");
            TroughSpawnPosition = Config.Bind("Spawning", "TroughSpawnPosition", "",
                "Custom trough placement position+rotation as \"x,y,z,yRot\". Press TagTroughPositionKey (F10) in-game to set.");
            TagPositionKey = Config.Bind("Spawning", "TagPositionKey", Key.F9,
                "Key to save the player's current position as the cow spawn point.");
            TagTroughPositionKey = Config.Bind("Spawning", "TagTroughPositionKey", Key.F10,
                "Key to save the player's current position as the trough placement point.");
            CowRoamRadius = Config.Bind("Spawning", "CowRoamRadius", 8f,
                "Half-size of the cow roaming area in meters.");

            // Streak persistence
            Cow1Streak = Config.Bind("Internal", "Cow1Streak", 0f,
                "Feeding streak for Cow 1. Managed automatically — do not edit.");
            Cow2Streak = Config.Bind("Internal", "Cow2Streak", 0f,
                "Feeding streak for Cow 2. Managed automatically — do not edit.");

            // Milk state persistence
            Cow1HasMilk = Config.Bind("Internal", "Cow1HasMilk", false,
                "Whether Cow 1 has milk ready. Managed automatically — do not edit.");
            Cow1MilkQuality = Config.Bind("Internal", "Cow1MilkQuality", 0,
                "Milk quality for Cow 1. Managed automatically — do not edit.");
            Cow2HasMilk = Config.Bind("Internal", "Cow2HasMilk", false,
                "Whether Cow 2 has milk ready. Managed automatically — do not edit.");
            Cow2MilkQuality = Config.Bind("Internal", "Cow2MilkQuality", 0,
                "Milk quality for Cow 2. Managed automatically — do not edit.");

            // Pending activation persistence
            Cow1PendingActivation = Config.Bind("Internal", "Cow1PendingActivation", false,
                "Whether Cow 1 is waiting to spawn next morning. Managed automatically — do not edit.");
            Cow2PendingActivation = Config.Bind("Internal", "Cow2PendingActivation", false,
                "Whether Cow 2 is waiting to spawn next morning. Managed automatically — do not edit.");

            if (DebugForceUnlock.Value)
                Log.LogWarning("DEBUG MODE: ForceUnlock is enabled.");

            _harmony = new Harmony("com.cottoncowmod.tots");
            _harmony.PatchAll();

            Log.LogInfo("Cotton Cow Mod loaded successfully.");
        }

        private void Update()
        {
            // Position tagging tools only available when DebugForceUnlock is enabled
            if (DebugForceUnlock == null || !DebugForceUnlock.Value)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[TagPositionKey.Value].wasPressedThisFrame)
                TagPlayerPosition(CowSpawnPosition, "CowSpawn");

            if (keyboard[TagTroughPositionKey.Value].wasPressedThisFrame)
                TagPlayerPosition(TroughSpawnPosition, "TroughSpawn");

            if (keyboard[Key.F11].wasPressedThisFrame)
                ResetCowUnlocks();

            if (keyboard[Key.F12].wasPressedThisFrame)
                DebugResetCottonRelationships();
        }

        /// <summary>
        /// Debug command (F11): Removes cow unlock strings from UnlockManager,
        /// despawns cows, and clears all persisted cow state.
        /// After pressing F11, set DebugForceUnlock=false and reload the save
        /// to test the real level 11 unlock flow.
        /// </summary>
        private void ResetCowUnlocks()
        {
            if (!Singleton<UnlockManager>.Exists)
            {
                Log.LogWarning("ResetCowUnlocks: UnlockManager not available.");
                return;
            }

            var unlockMgr = Singleton<UnlockManager>.Instance;

            // Remove cow unlock strings (both old and new trough tracking)
            unlockMgr.RemoveUnlockString("RELATIONSHIPUNLOCK_CowUnlock_1");
            unlockMgr.RemoveUnlockString("RELATIONSHIPUNLOCK_CowUnlock_2");
            unlockMgr.RemoveUnlockString(CowTroughManager.TroughGivenUnlockString);
            unlockMgr.RemoveUnlockString("COTTONCOWMOD_TroughGiven"); // old system
            unlockMgr.RemoveUnlockString("COTTONCOWMOD_NeedSpaceLetter_Farmer_Cotton");
            unlockMgr.RemoveUnlockString("COTTONCOWMOD_NeedSpaceLetter_Young_Tom_Cotton");

            // Despawn active cows
            if (CowSpawner.Cow1Spawned)
                CowSpawner.DespawnCow(true);
            if (CowSpawner.Cow2Spawned)
                CowSpawner.DespawnCow(false);

            // Reset in-memory state
            CowSpawner.Reset();

            // Clear persisted milk/streak state
            Cow1HasMilk.Value = false;
            Cow1MilkQuality.Value = 0;
            Cow2HasMilk.Value = false;
            Cow2MilkQuality.Value = 0;
            Cow1Streak.Value = 0f;
            Cow2Streak.Value = 0f;

            // Clear pending activation state
            Cow1PendingActivation.Value = false;
            Cow2PendingActivation.Value = false;

            // Remove cow trough from storage so re-unlock gives a fresh one
            try
            {
                var storage = Singleton<InventoryManager>.Instance.Storage;
                var troughType = CowTroughManager.CowTroughItemType;
                if (storage != null && troughType != null)
                {
                    int removed = storage.RemoveAll(
                        item => item.ItemType == troughType, destroyItems: true);
                    if (removed > 0)
                        Log.LogInfo($"ResetCowUnlocks: Removed {removed} CowFeedingTrough(s) from storage.");
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"ResetCowUnlocks: Could not clean trough from storage: {ex.Message}");
            }

            Log.LogWarning(
                "ResetCowUnlocks: Cleared all cow unlock strings, despawned cows, " +
                "reset milk/streak/pending state. Set DebugForceUnlock=false and reload save to test.");
        }

        /// <summary>
        /// Debug command (F12): Sets Cotton NPC relationships back to level 10
        /// (just below level 11 threshold), removes cow unlock strings, and despawns cows.
        /// After pressing F12, give gifts to Cotton NPCs to test the L11-L13 unlock flow.
        /// </summary>
        private void DebugResetCottonRelationships()
        {
            if (!Singleton<RelationshipManager>.Exists)
            {
                Log.LogWarning("DebugResetCottonRelationships: RelationshipManager not available.");
                return;
            }

            // First reset cow unlocks (same as F11)
            ResetCowUnlocks();

            // Now set Cotton NPC relationship levels back to 10
            var relMgr = Singleton<RelationshipManager>.Instance;
            var allData = relMgr.AllRelationshipData;
            if (allData == null)
            {
                Log.LogWarning("DebugResetCottonRelationships: AllRelationshipData is null.");
                return;
            }

            foreach (var kvp in allData)
            {
                if (!LevelCapPatches.IsCottonNPC(kvp.Key))
                    continue;

                var data = kvp.Value;
                var handler = Traverse.Create(data).Field("m_LevelingHandler").GetValue();
                if (handler == null)
                {
                    Log.LogWarning(
                        $"DebugResetCottonRelationships: No LevelingHandler for {kvp.Key}.");
                    continue;
                }

                var ht = Traverse.Create(handler);
                var thresholds = ht.Field("LevelXpThresholds").GetValue<int[]>();

                // Calculate XP total for "level 10, almost at 11"
                // Sum thresholds[0..9] to get total XP at level 10 start,
                // then add most of our L11 XP constant (not the in-memory value,
                // which may still reflect the old 500 threshold from save data)
                int targetXp = 0;
                if (thresholds != null)
                {
                    for (int i = 0; i < 10 && i < thresholds.Length; i++)
                        targetXp += thresholds[i];

                    targetXp += Math.Max(0, LevelCapPatches.XpPerAddedLevel - 10);
                }

                // Directly set private fields to avoid auto-level-up side effects
                ht.Field("m_CurrentLevel").SetValue(10);
                ht.Field("m_CurrentXP").SetValue(targetXp);

                Log.LogInfo(
                    $"DebugResetCottonRelationships: Set {kvp.Key} to level 10, XP={targetXp} " +
                    $"(~10 XP from level 11).");
            }

            // Re-evaluate cow state (cows should be inactive since unlock strings were removed)
            PlayerCows.EvaluateCowState();

            Log.LogWarning(
                "DebugResetCottonRelationships: Cotton NPCs set to level 10 (~10 XP from 11). " +
                "Give a gift to a Cotton NPC to re-trigger the cow unlock.");
        }

        private void TagPlayerPosition(ConfigEntry<string> configEntry, string label)
        {
            var player = Player.Instance;
            if (player == null)
            {
                Log.LogWarning($"{label}: No player found — are you in-game?");
                return;
            }

            Vector3 pos = player.transform.position;
            string posStr;

            if (label == "TroughSpawn")
            {
                // Save position + Y rotation for trough orientation
                float yRot = player.transform.eulerAngles.y;
                posStr = $"{pos.x:F2},{pos.y:F2},{pos.z:F2},{yRot:F2}";
            }
            else
            {
                posStr = $"{pos.x:F2},{pos.y:F2},{pos.z:F2}";
            }

            configEntry.Value = posStr;
            Config.Save();

            Log.LogInfo($"{label}: Saved position: {posStr}");
            Log.LogInfo($"{label}: Restart or reload the save for changes to take effect.");
        }

        internal static bool TryGetCustomSpawnPosition(out Vector3 position)
        {
            return TryParsePosition(CowSpawnPosition, out position);
        }

        internal static bool TryGetTroughPosition(out Vector3 position, out float yRotation)
        {
            position = Vector3.zero;
            yRotation = 0f;

            if (TroughSpawnPosition == null || string.IsNullOrEmpty(TroughSpawnPosition.Value))
                return false;

            string[] parts = TroughSpawnPosition.Value.Split(',');
            if (parts.Length < 3)
                return false;

            var style = System.Globalization.NumberStyles.Float;
            var culture = System.Globalization.CultureInfo.InvariantCulture;

            if (float.TryParse(parts[0].Trim(), style, culture, out float x) &&
                float.TryParse(parts[1].Trim(), style, culture, out float y) &&
                float.TryParse(parts[2].Trim(), style, culture, out float z))
            {
                position = new Vector3(x, y, z);

                // Optional 4th value: Y rotation
                if (parts.Length >= 4)
                    float.TryParse(parts[3].Trim(), style, culture, out yRotation);

                return true;
            }
            return false;
        }

        internal static bool TryParsePosition(ConfigEntry<string> entry, out Vector3 position)
        {
            position = Vector3.zero;
            if (entry == null || string.IsNullOrEmpty(entry.Value))
                return false;

            string[] parts = entry.Value.Split(',');
            if (parts.Length < 3)
                return false;

            var style = System.Globalization.NumberStyles.Float;
            var culture = System.Globalization.CultureInfo.InvariantCulture;

            if (float.TryParse(parts[0].Trim(), style, culture, out float x) &&
                float.TryParse(parts[1].Trim(), style, culture, out float y) &&
                float.TryParse(parts[2].Trim(), style, culture, out float z))
            {
                position = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
