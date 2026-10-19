using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace CottonCowMod
{
    /// <summary>
    /// Handles settings left in the main config file by 1.0.1 and earlier.
    ///
    /// 1.0.1 stored cow state ([Internal]) and developer tools ([Dev], [Debug], [Spawning])
    /// in the main config, which ModdingTools shows in Settings > Mods. Those entries are
    /// no longer bound, so BepInEx keeps them as "orphaned entries". We read the [Internal]
    /// values once so PlayerCows can move them into the first save loaded, then delete all
    /// the old entries so the file and the Mods tab only show player settings.
    /// </summary>
    internal static class LegacyConfig
    {
        private static readonly string[] DevSections = { "Dev", "Debug", "Spawning" };
        private const string InternalSection = "Internal";

        private static ConfigFile _config;
        private static ConfigEntry<bool> _migrated;
        private static readonly Dictionary<string, string> _state = new Dictionary<string, string>();

        public static void Load(ConfigFile config, ConfigFile devConfig)
        {
            _config = config;
            _migrated = devConfig.Bind("Internal", "LegacyStateMigrated", false,
                "Whether cow state from 1.0.1's config has been moved into a save. Managed automatically.");

            var orphans = Orphans();
            if (orphans == null)
                return;

            foreach (var entry in orphans)
            {
                if (entry.Key.Section == InternalSection)
                    _state[entry.Key.Key] = entry.Value;
            }

            if (!_migrated.Value && !HasMeaningfulState())
                _migrated.Value = true;

            RemoveOrphans(removeState: _migrated.Value);
        }

        /// <summary>
        /// True if 1.0.1 left cow state that hasn't been moved into a save yet.
        /// </summary>
        public static bool HasPendingState => _migrated != null && !_migrated.Value && _state.Count > 0;

        public static bool GetBool(string key) =>
            _state.TryGetValue(key, out string value) && value.Trim().ToLowerInvariant() == "true";

        public static int GetInt(string key) =>
            _state.TryGetValue(key, out string value) &&
            int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result : 0;

        public static float GetFloat(string key) =>
            _state.TryGetValue(key, out string value) &&
            float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                ? result : 0f;

        /// <summary>
        /// Called once the old state has been copied into a save. The old values are
        /// only migrated once, since they can't be tied to a particular save slot.
        /// </summary>
        public static void MarkMigrated()
        {
            _migrated.Value = true;
            _state.Clear();
            RemoveOrphans(removeState: true);
            CottonCowModPlugin.Log.LogInfo("LegacyConfig: Old cow state migrated and removed from config.");
        }

        private static bool HasMeaningfulState()
        {
            return GetBool("Cow1HasMilk") || GetBool("Cow2HasMilk")
                || GetFloat("Cow1Streak") > 0f || GetFloat("Cow2Streak") > 0f
                || GetBool("Cow1PendingActivation") || GetBool("Cow2PendingActivation");
        }

        private static void RemoveOrphans(bool removeState)
        {
            var orphans = Orphans();
            if (orphans == null)
                return;

            var toRemove = orphans.Keys
                .Where(d => DevSections.Contains(d.Section) || (removeState && d.Section == InternalSection))
                .ToList();
            if (toRemove.Count == 0)
                return;

            foreach (var definition in toRemove)
                orphans.Remove(definition);
            _config.Save();
            CottonCowModPlugin.Log.LogInfo($"LegacyConfig: Removed {toRemove.Count} old entries from config.");
        }

        private static Dictionary<ConfigDefinition, string> Orphans()
        {
            try
            {
                return Traverse.Create(_config).Property("OrphanedEntries")
                    .GetValue<Dictionary<ConfigDefinition, string>>();
            }
            catch (System.Exception ex)
            {
                CottonCowModPlugin.Log.LogWarning($"LegacyConfig: Could not read old config entries: {ex.Message}");
                return null;
            }
        }
    }
}
