using System;
using TotS.Events;
using TotS.Items;
using TotS.Scripts.DateTime;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Manages the daily milk production cycle for player-owned cows.
    /// Each in-game day: check trough for food, consume one item per cow,
    /// calculate milk quality based on food quality + feeding streak, set HasMilk flag.
    ///
    /// Mirrors the chicken egg system (SpawnCountOverrideOnePerUnlockedChicken).
    /// Streak data is persisted via BepInEx config entries.
    /// </summary>
    public static class CowProductionManager
    {
        private static bool _subscribed;

        // Quality streak multipliers (indexed by quality-1): Q1→+0.1, Q2→+0.2, Q3→+0.5
        private static readonly float[] StreakIncrements = { 0.1f, 0.2f, 0.5f };

        public static void Initialize()
        {
            if (_subscribed)
                GameEvents<PostDateChangedEvent>.Ignore(OnDayChanged);

            GameEvents<PostDateChangedEvent>.Listen(OnDayChanged);
            _subscribed = true;

            CottonCowModPlugin.Log.LogInfo("CowProductionManager: Subscribed to day change events.");
        }

        public static void Cleanup()
        {
            if (_subscribed)
            {
                GameEvents<PostDateChangedEvent>.Ignore(OnDayChanged);
                _subscribed = false;
            }
        }

        private static void OnDayChanged(PostDateChangedEvent evt)
        {
            try
            {
                ProcessDailyProduction();
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"CowProductionManager: OnDayChanged exception: {ex}");
            }
        }

        private static void ProcessDailyProduction()
        {
            var trough = CowTroughManager.Instance.GetInventory();
            if (trough == null)
            {
                CottonCowModPlugin.Log.LogInfo("CowProductionManager: No CowTrough inventory, skipping.");
                return;
            }

            // Process Cow 1
            if (PlayerCows.Cow1Active)
            {
                var cowGO = CowSpawner.GetCow1GO();
                if (cowGO != null)
                    ProcessCowProduction(cowGO, true, trough);
            }

            // Process Cow 2
            if (PlayerCows.Cow2Active)
            {
                var cowGO = CowSpawner.GetCow2GO();
                if (cowGO != null)
                    ProcessCowProduction(cowGO, false, trough);
            }
        }

        private static void ProcessCowProduction(GameObject cowGO, bool isCow1, TotS.Inventory.Inventory trough)
        {
            var playerCow = cowGO.GetComponent<PlayerOwnedCow>();
            if (playerCow == null) return;

            string cowLabel = isCow1 ? "Cow1" : "Cow2";

            if (playerCow.HasMilk)
                return;

            // Get current streak from config
            float currentStreak = isCow1
                ? CottonCowModPlugin.Cow1Streak.Value
                : CottonCowModPlugin.Cow2Streak.Value;

            // Check for food in trough
            if (trough.Items.Count > 0)
            {
                // Get the first food item
                var items = trough.Items;
                Item food = items[0];

                // Read food quality
                int foodQuality = 1;
                if (food.TryGetAspect<QualityAspect.Instance>(out var qualityAspect))
                    foodQuality = qualityAspect.Quality;

                // Remove the food item from the trough (consume it)
                trough.Remove(food, destroyItem: true);

                // Update streak based on food quality
                int streakIndex = Mathf.Clamp(foodQuality - 1, 0, StreakIncrements.Length - 1);
                float increment = StreakIncrements[streakIndex];
                currentStreak += increment;

                // Calculate milk quality
                // Formula from chicken system: Max(Clamp(CeilToInt(Random(0, streak)), 1, 3), foodQuality)
                int streakQuality = Mathf.Clamp(
                    Mathf.CeilToInt(UnityEngine.Random.Range(0f, currentStreak)), 1, 3);
                int milkQuality = Mathf.Max(streakQuality, Mathf.Clamp(foodQuality, 1, 3));

                // Set milk available
                playerCow.HasMilk = true;
                playerCow.MilkQuality = milkQuality;

                // Persist milk state to config (survives save/load)
                SaveMilkState(isCow1, true, milkQuality);

                // Update visual indicator
                var indicator = cowGO.GetComponent<CowMilkIndicator>();
                if (indicator != null)
                    indicator.SetMilkAvailable(true);

                // Save streak
                SaveStreak(isCow1, currentStreak);

                CottonCowModPlugin.Log.LogInfo(
                    $"CowProductionManager: {cowLabel} fed Q{foodQuality} food, " +
                    $"streak={currentStreak:F1}, milk Q{milkQuality}");
            }
            else
            {
                // No food — reset streak
                if (currentStreak > 0f)
                {
                    CottonCowModPlugin.Log.LogInfo(
                        $"CowProductionManager: {cowLabel} no food, streak reset from {currentStreak:F1}");
                    SaveStreak(isCow1, 0f);
                }
            }
        }

        private static void SaveStreak(bool isCow1, float value)
        {
            if (isCow1)
                CottonCowModPlugin.Cow1Streak.Value = value;
            else
                CottonCowModPlugin.Cow2Streak.Value = value;
        }

        public static void SaveMilkState(bool isCow1, bool hasMilk, int quality)
        {
            if (isCow1)
            {
                CottonCowModPlugin.Cow1HasMilk.Value = hasMilk;
                CottonCowModPlugin.Cow1MilkQuality.Value = quality;
            }
            else
            {
                CottonCowModPlugin.Cow2HasMilk.Value = hasMilk;
                CottonCowModPlugin.Cow2MilkQuality.Value = quality;
            }
        }

        public static void Reset()
        {
            Cleanup();
        }
    }
}
