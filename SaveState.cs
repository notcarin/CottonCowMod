using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TotS.Unlock;

namespace CottonCowMod
{
    /// <summary>
    /// Per-save storage for cow state (milk, streak, delivery), kept in the save file itself.
    ///
    /// Values are stored as "COTTONCOWMOD_STATE_&lt;key&gt;=&lt;value&gt;" strings in UnlockManager's
    /// unlocked-strings set, which the game writes into every save. This means:
    /// - each save slot has its own state, and a new game starts clean
    /// - quitting without saving rolls the state back along with the rest of the save
    /// - vanilla ignores the strings, so saves stay loadable if the mod is removed
    ///
    /// Writes go straight into the set, so they're included in the next save automatically.
    /// </summary>
    public static class SaveState
    {
        private const string Prefix = "COTTONCOWMOD_STATE_";

        public static bool Available => Singleton<UnlockManager>.Exists;

        public static string Get(string key)
        {
            var strings = UnlockedStrings();
            if (strings == null)
                return null;

            string prefix = Prefix + key + "=";
            foreach (string s in strings)
            {
                if (s.StartsWith(prefix))
                    return s.Substring(prefix.Length);
            }
            return null;
        }

        public static void Set(string key, string value)
        {
            var strings = UnlockedStrings();
            if (strings == null)
            {
                CottonCowModPlugin.Log.LogWarning($"SaveState: Cannot set {key}, no save loaded.");
                return;
            }

            string prefix = Prefix + key + "=";
            strings.RemoveWhere(s => s.StartsWith(prefix));
            if (value != null)
                strings.Add(prefix + value);
        }

        public static bool Has(string key) => Get(key) != null;

        public static bool GetBool(string key) => Get(key) == "1";
        public static void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

        public static int GetInt(string key, int fallback = 0) =>
            int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value : fallback;

        public static void SetInt(string key, int value) =>
            Set(key, value.ToString(CultureInfo.InvariantCulture));

        public static float GetFloat(string key, float fallback = 0f) =>
            float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;

        public static void SetFloat(string key, float value) =>
            Set(key, value.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>
        /// Key for a per-cow value, e.g. CowKey(true, "HasMilk") → "Cow1HasMilk".
        /// </summary>
        public static string CowKey(bool isCow1, string name) => (isCow1 ? "Cow1" : "Cow2") + name;

        /// <summary>
        /// All of this mod's stored values, for the bug report.
        /// </summary>
        public static List<string> All()
        {
            var result = new List<string>();
            var strings = UnlockedStrings();
            if (strings == null)
                return result;
            foreach (string s in strings)
            {
                if (s.StartsWith(Prefix))
                    result.Add(s.Substring(Prefix.Length));
            }
            result.Sort();
            return result;
        }

        private static HashSet<string> UnlockedStrings()
        {
            if (!Singleton<UnlockManager>.Exists)
                return null;
            return Traverse.Create(Singleton<UnlockManager>.Instance)
                .Field("m_UnlockedStrings").GetValue<HashSet<string>>();
        }
    }
}
