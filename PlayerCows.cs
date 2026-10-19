using System;
using TotS;
using TotS.Events;
using TotS.Garden;
using TotS.Items;
using TotS.Inventory;
using TotS.Scripts.DateTime;
using TotS.Unlock;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Tracks cow unlock state via dual-gate system:
    /// - Relationship level 13 with a Cotton NPC (RELATIONSHIPUNLOCK_CowUnlock_1 or _2)
    /// - Garden Area 5 (the glade) unlocked
    /// Both conditions must be met for each cow.
    ///
    /// Per-save state (see SaveState):
    /// - Pending: conditions met, the cow arrives the next morning
    /// - Delivered: the cow has arrived (letter sent, trough given) and spawns on every load
    ///
    /// Nothing is evaluated until the save has finished loading and the player has control
    /// (see Update), because save systems read their data in no guaranteed order and the
    /// game raises date events while loading.
    ///
    /// This is a static class (not MonoBehaviour) because DontDestroyOnLoad objects
    /// created during BepInEx chainloader don't receive Update() callbacks.
    /// Initialization is triggered from InventoryManagerPatch when the game scene loads.
    /// </summary>
    public static class PlayerCows
    {
        internal const string GardenArea5UnlockString = "Unlock_Garden_Area_5";
        private const int CowUnlockLevel = 13;

        private static bool _cow1Active;
        private static bool _cow2Active;
        private static bool _saveReady;
        private static bool _unlockSubscribed;
        private static bool _dateEventSubscribed;

        public static bool Cow1Active => _cow1Active;
        public static bool Cow2Active => _cow2Active;

        /// <summary>
        /// True once the current save has loaded and the player has control.
        /// </summary>
        public static bool SaveReady => _saveReady;

        /// <summary>
        /// The game records an unlocked garden area under two keys: the raw request string
        /// ("Unlock_Garden_Area_5") and a prefixed key ("GARDEN_AREA_Unlock_Garden_Area_5",
        /// see GardenAreaData.CombinedString). Some saves only contain one of them, so we
        /// accept either, and also ask the game's GardenAreaUnlockController directly
        /// (the same check it uses to show the area's boundary).
        /// </summary>
        internal static bool IsGardenArea5Unlocked
        {
            get
            {
                if (CottonCowModPlugin.DevLockGarden != null && CottonCowModPlugin.DevLockGarden.Value)
                    return false;
                if (CottonCowModPlugin.DebugForceUnlock != null && CottonCowModPlugin.DebugForceUnlock.Value)
                    return true;
                if (!Singleton<UnlockManager>.Exists)
                    return false;

                var unlockMgr = Singleton<UnlockManager>.Instance;
                if (unlockMgr.ContainsUnlockString(GardenArea5UnlockString) ||
                    unlockMgr.ContainsUnlockString("GARDEN_AREA_" + GardenArea5UnlockString))
                    return true;

                var controller = Singleton<GardenManager>.Exists
                    ? Singleton<GardenManager>.Instance.GardenAreaUnlockController
                    : null;
                return controller != null && controller.IsAreaUnlocked(GardenAreaPart.Area5);
            }
        }

        internal static string OwnerNPC(bool isCow1) => isCow1 ? "Farmer_Cotton" : "Young_Tom_Cotton";
        internal static string UnlockString(bool isCow1) =>
            isCow1 ? "RELATIONSHIPUNLOCK_CowUnlock_1" : "RELATIONSHIPUNLOCK_CowUnlock_2";
        private static string Label(bool isCow1) => isCow1 ? "Cow1" : "Cow2";

        /// <summary>
        /// Called from InventoryManagerPatch during scene load.
        /// Subscribes to unlock and date events. Safe to call multiple times (handles scene reloads).
        /// </summary>
        public static void Initialize()
        {
            try
            {
                if (!Singleton<UnlockManager>.Exists)
                {
                    CottonCowModPlugin.Log.LogWarning("PlayerCows: UnlockManager not available yet.");
                    return;
                }

                // Reset state on scene reload
                CowSpawner.Reset();
                _cow1Active = false;
                _cow2Active = false;
                _saveReady = false;

                // Unsubscribe first to avoid double-subscription on scene reload
                if (_unlockSubscribed)
                    Singleton<UnlockManager>.Instance.UnlockRequest -= OnUnlockRequest;
                if (_dateEventSubscribed)
                    GameEvents<DateChangedEvent>.Ignore(OnDateChanged);

                Singleton<UnlockManager>.Instance.UnlockRequest += OnUnlockRequest;
                _unlockSubscribed = true;
                GameEvents<DateChangedEvent>.Listen(OnDateChanged);
                _dateEventSubscribed = true;
                CottonCowModPlugin.Log.LogInfo("PlayerCows: Subscribed to unlock and date events.");

                // Cache the UIPrefab for trough interaction
                CowTroughManager.CacheUIPrefabIfNeeded();

                // Initialize daily milk production system
                CowProductionManager.Initialize();
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"PlayerCows: Initialize() exception: {ex}");
            }
        }

        /// <summary>
        /// Called when the game starts loading a save or a new game (SaveGameLoadPatch).
        /// </summary>
        public static void OnLoadStarted()
        {
            _saveReady = false;
        }

        /// <summary>
        /// Called every frame from the plugin. Detects when the loaded save is ready.
        /// </summary>
        public static void Update()
        {
            if (_saveReady || !IsWorldReady())
                return;

            _saveReady = true;
            CottonCowModPlugin.Log.LogInfo("PlayerCows: Save loaded and player has control.");
            try
            {
                MigrateSaveIfNeeded();
                EvaluateCowState();
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"PlayerCows: Save-ready evaluation exception: {ex}");
            }
        }

        private static bool IsWorldReady()
        {
            return Singleton<TotsSystemGameStates>.Instance != null
                && Singleton<TotsSystemGameStates>.Instance.Current == TotsSystemGameStates.RunningState.InWorld
                && Player.Instance != null
                && Singleton<UnlockManager>.Exists
                && Singleton<GameStateManager>.Instance != null
                && Singleton<GameStateManager>.Instance.CurrentState is InteractionGameState.DefaultGameState;
        }

        private static void OnUnlockRequest(string unlockString, bool showPrompt)
        {
            // Re-evaluate on any unlock event (could be relationship or garden area)
            if (_saveReady)
                EvaluateCowState();
        }

        private static void OnDateChanged(DateChangedEvent evt)
        {
            // The game also raises date events while a save loads; only real mornings count
            if (!_saveReady)
                return;

            try
            {
                ProcessPendingDeliveries();

                // Re-check unlock conditions each morning. OnUnlockRequest can run before
                // the game's own handler records the garden unlock, so an unlock missed at
                // that moment is picked up here (the cow then arrives the following morning).
                EvaluateCowState();
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"PlayerCows: OnDateChanged exception: {ex}");
            }
        }

        /// <summary>
        /// Relationship gate. Also accepts a Cotton NPC already at level 13 without the unlock
        /// string (the string is only added on the transition to 13, which a save can miss),
        /// and adds the missing string.
        /// </summary>
        private static bool IsRelationshipUnlocked(bool isCow1)
        {
            if (CottonCowModPlugin.DebugForceUnlock != null && CottonCowModPlugin.DebugForceUnlock.Value)
                return true;

            var unlockMgr = Singleton<UnlockManager>.Instance;
            string unlockString = UnlockString(isCow1);
            if (unlockMgr.ContainsUnlockString(unlockString))
                return true;

            if (Singleton<RelationshipManager>.Exists &&
                Singleton<RelationshipManager>.Instance.AllRelationshipData.TryGetValue(OwnerNPC(isCow1), out var data) &&
                data.CurrentRelationshipLevel >= CowUnlockLevel)
            {
                CottonCowModPlugin.Log.LogInfo(
                    $"PlayerCows: {OwnerNPC(isCow1)} is level {data.CurrentRelationshipLevel} " +
                    $"without {unlockString}; adding it.");
                unlockMgr.AddUnlockString(unlockString);
                return true;
            }
            return false;
        }

        public static void EvaluateCowState()
        {
            if (!_saveReady || !Singleton<UnlockManager>.Exists)
                return;

            bool garden = IsGardenArea5Unlocked;
            EvaluateCow(true, garden);
            EvaluateCow(false, garden);
        }

        private static void EvaluateCow(bool isCow1, bool garden)
        {
            bool relationship = IsRelationshipUnlocked(isCow1);
            bool eligible = relationship && garden;
            bool delivered = SaveState.GetBool(SaveState.CowKey(isCow1, "Delivered"));
            bool pending = SaveState.GetBool(SaveState.CowKey(isCow1, "Pending"));
            bool active = isCow1 ? _cow1Active : _cow2Active;

            if (eligible && delivered)
            {
                if (!active)
                {
                    SetActive(isCow1, true);
                    CottonCowModPlugin.Log.LogInfo($"PlayerCows: {Label(isCow1)} active (delivered earlier).");
                }
                CowSpawner.SpawnCow(OwnerNPC(isCow1), isCow1);

                // Repairs saves where the trough couldn't be given (e.g. Storage was full)
                GiveTroughIfNeeded();
            }
            else if (eligible && !pending)
            {
                SaveState.SetBool(SaveState.CowKey(isCow1, "Pending"), true);
                CottonCowModPlugin.Log.LogInfo(
                    $"PlayerCows: {Label(isCow1)} unlocked for {OwnerNPC(isCow1)}, arriving next morning.");
            }
            else if (!eligible && active)
            {
                SetActive(isCow1, false);
                CowSpawner.DespawnCow(isCow1);
            }

            // "Need more space" letter when the relationship is there but the glade isn't
            if (relationship && !garden)
                CowMailSender.QueueNeedSpaceLetter(OwnerNPC(isCow1));
        }

        /// <summary>
        /// Called each morning. Delivers cows whose unlock happened since the last morning:
        /// spawns them, gives the trough, and sends the letter (only ever once per cow).
        /// </summary>
        private static void ProcessPendingDeliveries()
        {
            bool garden = IsGardenArea5Unlocked;
            foreach (bool isCow1 in new[] { true, false })
            {
                string pendingKey = SaveState.CowKey(isCow1, "Pending");
                if (!SaveState.GetBool(pendingKey))
                    continue;

                SaveState.Set(pendingKey, null);
                if (!(IsRelationshipUnlocked(isCow1) && garden))
                {
                    CottonCowModPlugin.Log.LogWarning(
                        $"PlayerCows: {Label(isCow1)} pending, but unlock conditions are no longer met. Cleared.");
                    continue;
                }

                SaveState.SetBool(SaveState.CowKey(isCow1, "Delivered"), true);
                SetActive(isCow1, true);
                CowSpawner.SpawnCow(OwnerNPC(isCow1), isCow1);
                GiveTroughIfNeeded();
                CowMailSender.QueueCowLetter(OwnerNPC(isCow1));
                CottonCowModPlugin.Log.LogInfo(
                    $"PlayerCows: {Label(isCow1)} delivered. Spawned, trough given, letter sent.");
            }
        }

        private static void SetActive(bool isCow1, bool active)
        {
            if (isCow1)
                _cow1Active = active;
            else
                _cow2Active = active;
        }

        /// <summary>
        /// Saves from 1.0.1 and earlier have no per-save state. Cows that had arrived then are
        /// marked delivered (so they don't arrive again with another letter), and 1.0.1's
        /// global milk/streak values are copied into the first such save loaded.
        /// </summary>
        private static void MigrateSaveIfNeeded()
        {
            if (SaveState.Has("Version"))
                return;

            var unlockMgr = Singleton<UnlockManager>.Instance;
            bool troughGiven = unlockMgr.ContainsUnlockString(CowTroughManager.TroughGivenUnlockString);
            bool garden = IsGardenArea5Unlocked;
            bool migratedLegacy = false;

            foreach (bool isCow1 in new[] { true, false })
            {
                // In 1.0.1 the trough was given when the first cow arrived
                bool arrived = troughGiven && garden && unlockMgr.ContainsUnlockString(UnlockString(isCow1));
                if (!arrived)
                    continue;

                SaveState.SetBool(SaveState.CowKey(isCow1, "Delivered"), true);
                CottonCowModPlugin.Log.LogInfo($"PlayerCows: Migrated {Label(isCow1)} as already delivered.");

                if (LegacyConfig.HasPendingState)
                {
                    string label = Label(isCow1);
                    SaveState.SetBool(SaveState.CowKey(isCow1, "HasMilk"), LegacyConfig.GetBool(label + "HasMilk"));
                    SaveState.SetInt(SaveState.CowKey(isCow1, "MilkQuality"), LegacyConfig.GetInt(label + "MilkQuality"));
                    SaveState.SetFloat(SaveState.CowKey(isCow1, "Streak"), LegacyConfig.GetFloat(label + "Streak"));
                    migratedLegacy = true;
                }
            }

            if (migratedLegacy)
                LegacyConfig.MarkMigrated();

            SaveState.SetInt("Version", 1);
        }

        /// <summary>
        /// Gives the player a CowFeedingTrough by adding it to the Storage inventory
        /// (or the mailbox if Storage is full). The trough has the gardening trait,
        /// so it appears in the Garden tab in decoration mode.
        /// Tracked via a persistent unlock string, set only once the trough was actually added.
        ///
        /// Also replaces a trough that has gone missing: saving without the mod installed drops
        /// the trough (the game doesn't know the item), so after a reinstall the player would
        /// have cows and no way to feed them.
        /// </summary>
        private static void GiveTroughIfNeeded()
        {
            try
            {
                var unlockMgr = Singleton<UnlockManager>.Instance;
                var troughType = CowTroughManager.CowTroughItemType;
                bool given = unlockMgr.ContainsUnlockString(CowTroughManager.TroughGivenUnlockString);

                if (troughType == null)
                {
                    if (!given)
                        CottonCowModPlugin.Log.LogWarning(
                            "PlayerCows: CowFeedingTrough ItemType not available! " +
                            "ItemManagerPatch may not have run yet.");
                    return;
                }

                // ItemManager knows every item in the save: in any inventory or placed in the world
                if (given)
                {
                    if (Singleton<ItemManager>.Instance.GetItem(item => item.ItemType == troughType) != null)
                        return;
                    CottonCowModPlugin.Log.LogInfo(
                        "PlayerCows: CowFeedingTrough is missing (was the mod uninstalled?), giving a new one.");
                }

                var inventories = Singleton<InventoryManager>.Instance;
                var item = Singleton<ItemManager>.Instance.CreateItem(troughType);
                string destination;
                if (inventories.Storage.Add(item, true))
                    destination = "Storage (Garden tab)";
                else if (inventories.Mailbox.Add(item))
                    destination = "mailbox (Storage was full)";
                else
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "PlayerCows: Storage and mailbox are full, will retry giving the trough tomorrow.");
                    return;
                }

                unlockMgr.AddUnlockString(CowTroughManager.TroughGivenUnlockString);
                CottonCowModPlugin.Log.LogInfo($"PlayerCows: CowFeedingTrough added to {destination}.");
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"PlayerCows: GiveTroughIfNeeded exception: {ex}");
            }
        }
    }
}
