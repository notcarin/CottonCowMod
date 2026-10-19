using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using CottonCowMod.Patches;
using TMPro;
using TotS;
using TotS.Garden;
using TotS.Unlock;
using UnityEngine;
using UnityEngine.UI;

namespace CottonCowMod
{
    /// <summary>
    /// Builds a bug report players can share without digging through the BepInEx folder.
    ///
    /// Triggered by the "Create Bug Report" config toggle. ModdingTools turns every bool
    /// config entry into a toggle in Settings > Mods, so players flip it there. The report
    /// is saved to the Desktop and copied to the clipboard, then the toggle is switched
    /// back off and its label is updated to confirm.
    ///
    /// The report contains: versions, installed plugins, cow unlock state, Cotton
    /// relationship levels, this mod's config, and this session's log lines from this mod
    /// plus any warnings/errors. The Windows user folder is scrubbed from paths.
    /// </summary>
    public static class BugReport
    {
        internal const string ToggleKey = "Create Bug Report";
        private const string FileName = "CottonCowMod_BugReport.txt";

        // Reddit comments cap at 10,000 characters; keep the clipboard copy under that
        private const int ClipboardCharLimit = 9500;
        private const int FileLogLineLimit = 2000;
        private const long LogTailBytes = 4 * 1024 * 1024;

        private static readonly string[] CottonNPCs = { "Farmer_Cotton", "Young_Tom_Cotton" };

        public static void Initialize(ConfigEntry<bool> toggle, ConfigFile config)
        {
            // Never start switched on (e.g. if the game closed mid-report)
            toggle.Value = false;

            toggle.SettingChanged += (sender, args) =>
            {
                if (!toggle.Value)
                    return;

                string message = Create(config);
                toggle.Value = false;
                UpdateMenuToggle(message);
            };
        }

        /// <summary>
        /// Writes the report to the Desktop and copies it to the clipboard.
        /// Returns a short message to show the player.
        /// </summary>
        private static string Create(ConfigFile config)
        {
            try
            {
                string summary = BuildSummary(config);
                List<string> logLines = ReadRelevantLogLines();

                string fullReport = summary + FormatLog(logLines, int.MaxValue);
                string savedPath = SaveToFile(Scrub(fullReport));

                string clipboardReport = Scrub(summary + FormatLog(logLines, ClipboardCharLimit - summary.Length));
                GUIUtility.systemCopyBuffer = clipboardReport;

                CottonCowModPlugin.Log.LogInfo($"BugReport: Saved to {savedPath} and copied to clipboard.");
                return savedPath != null
                    ? "Bug report copied! Also saved to Desktop."
                    : "Bug report copied to clipboard!";
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"BugReport: Failed to create report: {ex}");
                return "Bug report failed - please send LogOutput.log";
            }
        }

        internal static string BuildSummary(ConfigFile config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Cotton Cow Mod Bug Report ===");
            sb.AppendLine($"Mod version: {CottonCowModPlugin.Version}");
            sb.AppendLine($"Game version: {Application.version}");
            sb.AppendLine($"OS: {SystemInfo.operatingSystem}");
            sb.AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm}");

            sb.AppendLine();
            sb.AppendLine("--- Installed plugins ---");
            foreach (var plugin in Chainloader.PluginInfos.Values)
                sb.AppendLine($"{plugin.Metadata.Name} {plugin.Metadata.Version} ({plugin.Metadata.GUID})");

            sb.AppendLine();
            sb.AppendLine("--- Cow state ---");
            AppendSafely(sb, () => AppendCowState(sb));

            sb.AppendLine();
            sb.AppendLine("--- Cotton relationships ---");
            AppendSafely(sb, () => AppendRelationships(sb));

            sb.AppendLine();
            sb.AppendLine("--- Config ---");
            foreach (var file in new[] { config, CottonCowModPlugin.DevConfig })
            {
                if (file == null)
                    continue;
                foreach (var entry in file.Select(pair => pair.Value))
                    sb.AppendLine($"[{entry.Definition.Section}] {entry.Definition.Key} = {entry.BoxedValue}");
            }

            return sb.ToString();
        }

        private static void AppendCowState(StringBuilder sb)
        {
            if (!Singleton<UnlockManager>.Exists)
            {
                sb.AppendLine("No save loaded.");
                return;
            }

            var unlockMgr = Singleton<UnlockManager>.Instance;
            string Has(string key) => unlockMgr.ContainsUnlockString(key) ? "yes" : "no";

            sb.AppendLine($"Cow1 (Farmer Cotton): unlock={Has("RELATIONSHIPUNLOCK_CowUnlock_1")} " +
                          $"active={PlayerCows.Cow1Active} spawned={CowSpawner.Cow1Spawned} " +
                          $"needSpaceLetter={Has("COTTONCOWMOD_NeedSpaceLetter_Farmer_Cotton")}");
            sb.AppendLine($"Cow2 (Young Tom):     unlock={Has("RELATIONSHIPUNLOCK_CowUnlock_2")} " +
                          $"active={PlayerCows.Cow2Active} spawned={CowSpawner.Cow2Spawned} " +
                          $"needSpaceLetter={Has("COTTONCOWMOD_NeedSpaceLetter_Young_Tom_Cotton")}");
            sb.AppendLine($"Save ready: {PlayerCows.SaveReady}");
            sb.AppendLine($"Saved state: {string.Join(", ", SaveState.All())}");
            sb.AppendLine($"Mod sees Garden Area 5 unlocked: {PlayerCows.IsGardenArea5Unlocked}");
            sb.AppendLine($"Garden Area 5 loaded in world: {CowSpawner.GardenArea5Found}");
            foreach (bool isCow1 in new[] { true, false })
            {
                var cowGO = isCow1 ? CowSpawner.GetCow1GO() : CowSpawner.GetCow2GO();
                if (cowGO != null)
                    sb.AppendLine($"{(isCow1 ? "Cow1" : "Cow2")} position: {cowGO.transform.position} " +
                                  $"in glade: {(GladeArea.Built ? GladeArea.Contains(cowGO.transform.position).ToString() : "unknown")}");
            }
            sb.AppendLine($"Trough given: {Has(CowTroughManager.TroughGivenUnlockString)} " +
                          $"trough inventory: {CowTroughManager.Instance.HasInventory()} " +
                          $"UI found: {CowTroughManager.CachedUIPrefab != null}");

            var controller = Singleton<GardenManager>.Exists
                ? Singleton<GardenManager>.Instance.GardenAreaUnlockController
                : null;
            for (int area = 2; area <= 5; area++)
            {
                string raw = $"Unlock_Garden_Area_{area}";
                string game = controller != null
                    ? controller.IsAreaUnlocked((GardenAreaPart)(area - 1)).ToString()
                    : "n/a";
                sb.AppendLine($"Garden Area {area}: game={game} raw={Has(raw)} prefixed={Has("GARDEN_AREA_" + raw)}");
            }
        }

        private static void AppendRelationships(StringBuilder sb)
        {
            if (!Singleton<RelationshipManager>.Exists)
            {
                sb.AppendLine("No save loaded.");
                return;
            }

            var allData = Singleton<RelationshipManager>.Instance.AllRelationshipData;
            foreach (string npc in CottonNPCs)
            {
                if (allData != null && allData.TryGetValue(npc, out var data))
                    sb.AppendLine($"{npc}: level {data.CurrentRelationshipLevel}/{data.MaxLevel}, XP {data.CurrentXP}");
                else
                    sb.AppendLine($"{npc}: no data");
            }
        }

        private static void AppendSafely(StringBuilder sb, Action append)
        {
            try
            {
                append();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(error reading state: {ex.Message})");
            }
        }

        /// <summary>
        /// Reads this session's LogOutput.log, keeping this mod's lines and any warnings
        /// or errors from other sources (with their stack trace continuation lines).
        /// </summary>
        private static List<string> ReadRelevantLogLines()
        {
            var lines = new List<string>();
            string logPath = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            if (!File.Exists(logPath))
                return lines;

            // BepInEx holds the log open for writing, so allow shared access
            using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                // The log can grow to hundreds of MB (other mods log heavily), so only read
                // the end of it to keep the menu responsive
                if (stream.Length > LogTailBytes)
                {
                    stream.Seek(-LogTailBytes, SeekOrigin.End);
                    reader.ReadLine(); // skip the partial line we landed in
                }

                bool keepContinuation = false;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("["))
                    {
                        keepContinuation = line.Contains(":Cotton Cow Mod]")
                            || line.StartsWith("[Warning")
                            || line.StartsWith("[Error")
                            || line.StartsWith("[Fatal");
                        if (keepContinuation)
                            lines.Add(line);
                    }
                    else if (keepContinuation && line.Length > 0)
                    {
                        lines.Add(line);
                    }
                }
            }

            if (lines.Count > FileLogLineLimit)
                lines.RemoveRange(0, lines.Count - FileLogLineLimit);
            return lines;
        }

        /// <summary>
        /// Formats log lines, keeping the most recent ones that fit within charBudget.
        /// </summary>
        private static string FormatLog(List<string> lines, int charBudget)
        {
            const string header = "\n--- Log (this mod + warnings/errors) ---\n";
            int budget = charBudget - header.Length;

            var kept = new List<string>();
            for (int i = lines.Count - 1; i >= 0 && budget > 0; i--)
            {
                budget -= lines[i].Length + Environment.NewLine.Length;
                if (budget >= 0)
                    kept.Add(lines[i]);
            }
            kept.Reverse();

            var sb = new StringBuilder(header);
            if (kept.Count < lines.Count)
                sb.AppendLine($"({lines.Count - kept.Count} earlier lines omitted)");
            foreach (string line in kept)
                sb.AppendLine(line);
            return sb.ToString();
        }

        /// <summary>
        /// Saves to the Desktop, falling back to the BepInEx folder. Returns the path, or null on failure.
        /// </summary>
        private static string SaveToFile(string report)
        {
            foreach (string folder in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                         Paths.BepInExRootPath
                     })
            {
                try
                {
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                        continue;
                    string path = Path.Combine(folder, FileName);
                    File.WriteAllText(path, report);
                    return path;
                }
                catch (Exception ex)
                {
                    CottonCowModPlugin.Log.LogWarning($"BugReport: Could not save to {folder}: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Replaces the player's Windows user folder in paths so their username isn't shared.
        /// </summary>
        private static string Scrub(string text)
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                text = ReplaceIgnoreCase(text, profile, "%USERPROFILE%");
                text = ReplaceIgnoreCase(text, profile.Replace('\\', '/'), "%USERPROFILE%");
            }
            return text;
        }

        private static string ReplaceIgnoreCase(string text, string oldValue, string newValue)
        {
            int index = text.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                text = text.Substring(0, index) + newValue + text.Substring(index + oldValue.Length);
                index = text.IndexOf(oldValue, index + newValue.Length, StringComparison.OrdinalIgnoreCase);
            }
            return text;
        }

        /// <summary>
        /// ModdingTools names each toggle's GameObject after its config key and doesn't refresh
        /// it when the value changes, so switch it back off and show the result in its label.
        /// </summary>
        private static void UpdateMenuToggle(string message)
        {
            try
            {
                var toggleGO = GameObject.Find(ToggleKey);
                if (toggleGO == null)
                    return;

                toggleGO.GetComponentInChildren<Toggle>()?.SetIsOnWithoutNotify(false);
                var label = toggleGO.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = message;
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogWarning($"BugReport: Could not update menu toggle: {ex.Message}");
            }
        }
    }
}
