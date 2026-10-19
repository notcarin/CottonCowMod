using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CottonCowMod.Patches;
using HarmonyLib;
using TotS;
using TotS.DateTime;
using TotS.Inventory;
using TotS.Unlock;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CottonCowMod
{
    [BepInPlugin("com.cottoncowmod.tots", "Cotton Cow Mod", Version)]
    public class CottonCowModPlugin : BaseUnityPlugin
    {
        internal const string Version = "1.0.2";

        internal static ManualLogSource Log;
        internal static CottonCowModPlugin Instance;
        private Harmony _harmony;

        // --- Player settings (Config; ModdingTools shows these in Settings > Mods) ---
        internal static ConfigEntry<bool> CreateBugReport;

        // --- Developer settings (DevConfig, a separate file the Mods tab doesn't show) ---
        internal static ConfigFile DevConfig;
        internal static ConfigEntry<bool> DevTimeSkip;
        internal static ConfigEntry<Key> TimeSkipKey;
        internal static ConfigEntry<bool> DevLockGarden;
        internal static ConfigEntry<bool> DebugForceUnlock;
        internal static ConfigEntry<string> CowSpawnPosition;
        internal static ConfigEntry<Key> TagPositionKey;
        internal static ConfigEntry<float> CowRoamRadius;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Log.LogInfo($"Cotton Cow Mod v{Version} loading...");

            CreateBugReport = Config.Bind("Bug Report", BugReport.ToggleKey, false,
                "Turn on to save a bug report to your Desktop and copy it to the clipboard. " +
                "Paste it into your bug report message. Switches itself back off.");
            BugReport.Initialize(CreateBugReport, Config);

            // ModdingTools turns every entry in Config into a control in Settings > Mods,
            // so developer tools live in their own file where players won't stumble on them.
            DevConfig = new ConfigFile(
                System.IO.Path.Combine(Paths.ConfigPath, "com.cottoncowmod.tots.dev.cfg"), true);
            DevTimeSkip = DevConfig.Bind("Dev", "TimeSkip", false,
                "Enables TimeSkipKey to skip to the next morning. Does not affect cow unlock gates.");
            TimeSkipKey = DevConfig.Bind("Dev", "TimeSkipKey", Key.F8,
                "Key to skip to the next in-game morning.");
            DevLockGarden = DevConfig.Bind("Dev", "LockGarden", false,
                "Pretends Garden Area 5 is not unlocked (for testing need-space letters). Does not modify save data.");
            DebugForceUnlock = DevConfig.Bind("Debug", "ForceUnlock", false,
                "Forces both cow unlock gates to true (bypasses relationship + garden area checks). " +
                "Also enables F9 (tag spawn), F11 (reset cow unlocks) and F12 (reset Cotton relationships to 10).");
            CowSpawnPosition = DevConfig.Bind("Spawning", "CowSpawnPosition", "",
                "Custom cow spawn position as \"x,y,z\", overriding the glade. Press TagPositionKey in-game to set.");
            TagPositionKey = DevConfig.Bind("Spawning", "TagPositionKey", Key.F9,
                "Key to save the player's current position as the cow spawn point.");
            CowRoamRadius = DevConfig.Bind("Spawning", "CowRoamRadius", 8f,
                "Half-size of the cow roaming box in meters when the glade can't be found.");

            // 1.0.1 kept cow state and dev settings in Config; pick up the old state for migration
            LegacyConfig.Load(Config, DevConfig);

            if (DebugForceUnlock.Value)
                Log.LogWarning("DEBUG MODE: ForceUnlock is enabled.");

            _harmony = new Harmony("com.cottoncowmod.tots");
            _harmony.PatchAll();

            Log.LogInfo("Cotton Cow Mod loaded successfully.");

#if COTTONCOW_TESTS
            Testing.TestRunner.StartIfRequested();
#endif
        }

        private void Update()
        {
            // Detect when a loaded save is ready, then evaluate cows
            PlayerCows.Update();
            CowSpawner.Update();

            // Flush any deferred mail (MailManager may not be ready during early init)
            CowMailSender.FlushPendingLetters();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Dev time skip (independent of DebugForceUnlock)
            if (DevTimeSkip != null && DevTimeSkip.Value &&
                keyboard[TimeSkipKey.Value].wasPressedThisFrame)
                SkipToNextMorning();

            // Position tagging tools only available when DebugForceUnlock is enabled
            if (DebugForceUnlock == null || !DebugForceUnlock.Value)
                return;

            if (keyboard[TagPositionKey.Value].wasPressedThisFrame)
                TagPlayerPosition(CowSpawnPosition, "CowSpawn");

            if (keyboard[Key.F11].wasPressedThisFrame)
                ResetCowUnlocks();

            if (keyboard[Key.F12].wasPressedThisFrame)
                DebugResetCottonRelationships();
        }

        private void SkipToNextMorning()
        {
            if (!Singleton<DateTimeManager>.Exists)
            {
                Log.LogWarning("DevTimeSkip: DateTimeManager not available.");
                return;
            }

            Singleton<DateTimeManager>.Instance.DoDayTransition(
                DayTransitionGameState.sDebugSequenceIdentifier, 1);

            Log.LogInfo("DevTimeSkip: Skipping to next morning.");
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

            // Clear this save's milk/streak/delivery state
            foreach (string entry in SaveState.All())
                SaveState.Set(entry.Substring(0, entry.IndexOf('=')), null);

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
            string posStr = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2},{1:F2},{2:F2}", pos.x, pos.y, pos.z);

            configEntry.Value = posStr;
            DevConfig.Save();

            Log.LogInfo($"{label}: Saved position: {posStr}");
            Log.LogInfo($"{label}: Restart or reload the save for changes to take effect.");
        }

        internal static bool TryGetCustomSpawnPosition(out Vector3 position)
        {
            return TryParsePosition(CowSpawnPosition, out position);
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
