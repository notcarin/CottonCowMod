using System;
using TotS.Events;
using TotS.Items;
using TotS.Inventory;
using TotS.Scripts.DateTime;
using TotS.Unlock;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Tracks cow unlock state via dual-gate system:
    /// - Relationship level 11 with a Cotton NPC (fires RELATIONSHIPUNLOCK_CowUnlock_1 or _2)
    /// - Garden Area 5 unlocked
    /// Both conditions must be met for each cow to become active.
    ///
    /// When a cow becomes active:
    /// - Sets a pending activation flag (persisted via config)
    /// - On the next DateChangedEvent (next morning): spawns the cow, delivers trough
    ///   to Storage (Garden tab), and sends a mail notification
    ///
    /// This is a static class (not MonoBehaviour) because DontDestroyOnLoad objects
    /// created during BepInEx chainloader don't receive Update() callbacks.
    /// Initialization is triggered from InventoryManagerPatch when the game scene loads.
    /// </summary>
    public static class PlayerCows
    {
        private static bool _cow1RelationshipUnlocked;
        private static bool _cow2RelationshipUnlocked;
        private static bool _gardenArea5Unlocked;
        private static bool _cow1Active;
        private static bool _cow2Active;
        private static bool _initialized;
        private static bool _firstEvalLogged;
        private static bool _dateEventSubscribed;

        public static bool Cow1Active => _cow1Active;
        public static bool Cow2Active => _cow2Active;

        /// <summary>
        /// Called from InventoryManagerPatch during scene load.
        /// Subscribes to UnlockManager and evaluates current state.
        /// Safe to call multiple times (handles scene reloads).
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
                _firstEvalLogged = false;

                // Unsubscribe first to avoid double-subscription on scene reload
                if (_initialized)
                {
                    Singleton<UnlockManager>.Instance.UnlockRequest -= OnUnlockRequest;
                }
                if (_dateEventSubscribed)
                {
                    GameEvents<DateChangedEvent>.Ignore(OnDateChanged);
                    _dateEventSubscribed = false;
                }

                // Subscribe to unlock events (fires for future unlocks)
                Singleton<UnlockManager>.Instance.UnlockRequest += OnUnlockRequest;
                _initialized = true;
                CottonCowModPlugin.Log.LogInfo("PlayerCows: Subscribed to UnlockManager.");

                // Subscribe to date changes for deferred activation
                GameEvents<DateChangedEvent>.Listen(OnDateChanged);
                _dateEventSubscribed = true;
                CottonCowModPlugin.Log.LogInfo("PlayerCows: Subscribed to DateChangedEvent.");

                // Cache the UIPrefab for trough interaction
                CowTroughManager.CacheUIPrefabIfNeeded();

                // Initialize daily milk production system
                CowProductionManager.Initialize();

                // Process pending activations immediately on reload
                // (deferral is only for the initial unlock moment so the cow appears
                // the next morning — on reload the cow should just spawn right away)
                if (CottonCowModPlugin.Cow1PendingActivation.Value ||
                    CottonCowModPlugin.Cow2PendingActivation.Value)
                {
                    CottonCowModPlugin.Log.LogInfo(
                        "PlayerCows: Processing pending activations from previous session.");
                    ProcessPendingActivations();
                }

                // Evaluate current state (for previously earned unlocks from save data)
                EvaluateCowState();
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"PlayerCows: Initialize() exception: {ex}");
            }
        }

        private static void OnUnlockRequest(string unlockString, bool showPrompt)
        {
            // Re-evaluate on any unlock event (could be relationship or garden area)
            EvaluateCowState();
        }

        private static void OnDateChanged(DateChangedEvent evt)
        {
            try
            {
                ProcessPendingActivations();
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"PlayerCows: OnDateChanged exception: {ex}");
            }
        }

        /// <summary>
        /// Called on each DateChangedEvent. If any cows have pending activation,
        /// verifies unlock conditions are still met, then spawns them, delivers
        /// the trough, and sends a mail notification from the gifting NPC only.
        /// Stale pending flags (e.g. from a previous debug session) are cleared
        /// without spawning or sending mail.
        /// </summary>
        private static void ProcessPendingActivations()
        {
            bool cow1Pending = CottonCowModPlugin.Cow1PendingActivation.Value;
            bool cow2Pending = CottonCowModPlugin.Cow2PendingActivation.Value;

            if (!cow1Pending && !cow2Pending)
                return;

            // Re-check unlock conditions to guard against stale pending flags
            var unlockMgr = Singleton<UnlockManager>.Exists
                ? Singleton<UnlockManager>.Instance : null;
            bool debugForce = CottonCowModPlugin.DebugForceUnlock != null
                           && CottonCowModPlugin.DebugForceUnlock.Value;
            bool gardenOk = debugForce
                || (unlockMgr != null && unlockMgr.ContainsUnlockString("Unlock_Garden_Area_5"));
            bool cow1Unlocked = debugForce
                || (unlockMgr != null && unlockMgr.ContainsUnlockString("RELATIONSHIPUNLOCK_CowUnlock_1"));
            bool cow2Unlocked = debugForce
                || (unlockMgr != null && unlockMgr.ContainsUnlockString("RELATIONSHIPUNLOCK_CowUnlock_2"));

            CottonCowModPlugin.Log.LogInfo(
                $"PlayerCows: Processing pending activations " +
                $"(Cow1={cow1Pending}[unlock={cow1Unlocked}], Cow2={cow2Pending}[unlock={cow2Unlocked}], Garden={gardenOk}).");

            if (cow1Pending)
            {
                CottonCowModPlugin.Cow1PendingActivation.Value = false;
                if (cow1Unlocked && gardenOk)
                {
                    _cow1Active = true;
                    CowSpawner.SpawnCow("Farmer_Cotton", true);
                    GiveTroughIfNeeded();
                    CowMailSender.QueueCowLetter("Farmer_Cotton");
                    CottonCowModPlugin.Log.LogInfo(
                        "PlayerCows: Cow1 activated (deferred). Spawned, trough delivered, mail sent.");
                }
                else
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "PlayerCows: Cow1 pending flag was stale (unlock conditions not met). Cleared.");
                }
            }

            if (cow2Pending)
            {
                CottonCowModPlugin.Cow2PendingActivation.Value = false;
                if (cow2Unlocked && gardenOk)
                {
                    _cow2Active = true;
                    CowSpawner.SpawnCow("Young_Tom_Cotton", false);
                    GiveTroughIfNeeded();
                    CowMailSender.QueueCowLetter("Young_Tom_Cotton");
                    CottonCowModPlugin.Log.LogInfo(
                        "PlayerCows: Cow2 activated (deferred). Spawned, trough delivered, mail sent.");
                }
                else
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "PlayerCows: Cow2 pending flag was stale (unlock conditions not met). Cleared.");
                }
            }
        }

        public static void EvaluateCowState()
        {
            if (!Singleton<UnlockManager>.Exists)
                return;

            var unlockMgr = Singleton<UnlockManager>.Instance;
            bool debugForce = CottonCowModPlugin.DebugForceUnlock != null
                           && CottonCowModPlugin.DebugForceUnlock.Value;

            _cow1RelationshipUnlocked = debugForce
                || unlockMgr.ContainsUnlockString("RELATIONSHIPUNLOCK_CowUnlock_1");
            _cow2RelationshipUnlocked = debugForce
                || unlockMgr.ContainsUnlockString("RELATIONSHIPUNLOCK_CowUnlock_2");
            _gardenArea5Unlocked = debugForce
                || unlockMgr.ContainsUnlockString("Unlock_Garden_Area_5");

            bool newCow1 = _cow1RelationshipUnlocked && _gardenArea5Unlocked;
            bool newCow2 = _cow2RelationshipUnlocked && _gardenArea5Unlocked;

            // Account for pending activations — they count as "will be active"
            bool cow1Pending = CottonCowModPlugin.Cow1PendingActivation.Value;
            bool cow2Pending = CottonCowModPlugin.Cow2PendingActivation.Value;

            bool stateChanged = (newCow1 != _cow1Active && !cow1Pending)
                             || (newCow2 != _cow2Active && !cow2Pending);

            // Only log on first evaluation or state change (avoid spam from unlock replay)
            if (!_firstEvalLogged || stateChanged)
            {
                string prefix = stateChanged && _firstEvalLogged ? "State changed!" : "Initial state:";
                CottonCowModPlugin.Log.LogInfo(
                    $"PlayerCows: {prefix} Cow1={newCow1} Cow2={newCow2} " +
                    $"(Rel1={_cow1RelationshipUnlocked} Rel2={_cow2RelationshipUnlocked} " +
                    $"Garden5={_gardenArea5Unlocked})" +
                    (cow1Pending || cow2Pending ? $" [Pending: C1={cow1Pending} C2={cow2Pending}]" : "") +
                    (debugForce ? " [DEBUG FORCED]" : ""));
                _firstEvalLogged = true;
            }

            if (stateChanged)
            {
                // Handle cow 1 state changes
                if (newCow1 && !_cow1Active && !cow1Pending)
                {
                    OnCowActivated(true, "Farmer_Cotton");
                }
                else if (!newCow1 && _cow1Active)
                {
                    CowSpawner.DespawnCow(true);
                }

                // Handle cow 2 state changes
                if (newCow2 && !_cow2Active && !cow2Pending)
                {
                    OnCowActivated(false, "Young_Tom_Cotton");
                }
                else if (!newCow2 && _cow2Active)
                {
                    CowSpawner.DespawnCow(false);
                }
            }

            // Send "need more space" letters when relationship is unlocked but garden isn't
            if (_cow1RelationshipUnlocked && !_gardenArea5Unlocked)
                CowMailSender.QueueNeedSpaceLetter("Farmer_Cotton");
            if (_cow2RelationshipUnlocked && !_gardenArea5Unlocked)
                CowMailSender.QueueNeedSpaceLetter("Young_Tom_Cotton");

            _cow1Active = newCow1;
            _cow2Active = newCow2;

            // Re-read pending state — OnCowActivated may have set new pending flags
            // above, so the local cow1Pending/cow2Pending variables are now stale
            bool cow1NowPending = CottonCowModPlugin.Cow1PendingActivation.Value;
            bool cow2NowPending = CottonCowModPlugin.Cow2PendingActivation.Value;

            // Ensure cows are spawned on scene load if conditions are already met
            // (but NOT if they're pending — those wait for the next morning)
            if (_cow1Active && !CowSpawner.Cow1Spawned && !cow1NowPending)
            {
                CowSpawner.SpawnCow("Farmer_Cotton", true);
            }
            if (_cow2Active && !CowSpawner.Cow2Spawned && !cow2NowPending)
            {
                CowSpawner.SpawnCow("Young_Tom_Cotton", false);
            }
        }

        /// <summary>
        /// Called when a cow first becomes active (unlock conditions met).
        /// Instead of spawning immediately, sets a pending activation flag.
        /// The cow will actually spawn on the next DateChangedEvent (next morning).
        /// </summary>
        private static void OnCowActivated(bool isCow1, string ownerNPC)
        {
            CottonCowModPlugin.Log.LogInfo(
                $"PlayerCows: {(isCow1 ? "Cow1" : "Cow2")} activation deferred for {ownerNPC} " +
                "(will spawn next morning).");

            if (isCow1)
                CottonCowModPlugin.Cow1PendingActivation.Value = true;
            else
                CottonCowModPlugin.Cow2PendingActivation.Value = true;
        }

        /// <summary>
        /// Gives the player a CowFeedingTrough by adding it to the Storage inventory.
        /// The trough has the gardening trait, so it appears in the Garden tab in decoration mode.
        /// Tracked via a persistent unlock string so it survives save/load.
        /// </summary>
        private static void GiveTroughIfNeeded()
        {
            try
            {
                var unlockMgr = Singleton<UnlockManager>.Instance;
                if (unlockMgr.ContainsUnlockString(CowTroughManager.TroughGivenUnlockString))
                {
                    CottonCowModPlugin.Log.LogInfo("PlayerCows: CowFeedingTrough already given.");
                    return;
                }

                var troughType = CowTroughManager.CowTroughItemType;
                if (troughType == null)
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "PlayerCows: CowFeedingTrough ItemType not available! " +
                        "ItemManagerPatch may not have run yet.");
                    return;
                }

                var item = Singleton<ItemManager>.Instance.CreateItem(troughType);
                var storage = Singleton<InventoryManager>.Instance.Storage;
                storage.Add(item, true);

                unlockMgr.AddUnlockString(CowTroughManager.TroughGivenUnlockString);
                CottonCowModPlugin.Log.LogInfo(
                    "PlayerCows: CowFeedingTrough added to Storage (Garden tab).");
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"PlayerCows: GiveTroughIfNeeded exception: {ex}");
            }
        }
    }
}
