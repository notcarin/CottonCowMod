using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using TotS;
using TotS.BuySell;
using TotS.SaveGame;
using TotS.Tutorialisation;
using UnityEngine;

namespace CottonCowTestProbe
{
    /// <summary>
    /// Test-only plugin, run with Cotton Cow Mod removed. Loads a save the mod wrote and
    /// reports what vanilla sees for the Cotton family: relationship levels, and how many
    /// shop stock entries are available at that level (zero means an empty shop, the bug
    /// players hit after uninstalling 1.0.1).
    ///
    /// Runs when BepInEx/config/CottonCowMod.probe.txt exists (save=&lt;file name&gt;), writes
    /// BepInEx/TestResults/probe.txt, then quits.
    /// </summary>
    [BepInPlugin("com.cottoncowmod.testprobe", "Cotton Cow Test Probe", "1.0.0")]
    public class ProbePlugin : BaseUnityPlugin
    {
        private static readonly string TriggerPath = Path.Combine(Paths.ConfigPath, "CottonCowMod.probe.txt");
        private static readonly string ResultsDir = Path.Combine(Paths.BepInExRootPath, "TestResults");
        private static readonly string[] CottonNPCs = { "Farmer_Cotton", "Young_Tom_Cotton" };

        private StreamWriter _out;
        private int _failed;

        private void Awake()
        {
            if (!File.Exists(TriggerPath))
                return;
            var go = new GameObject("CottonCowTestProbe");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeRunner>().Plugin = this;
        }

        private class ProbeRunner : MonoBehaviour
        {
            public ProbePlugin Plugin;
            private IEnumerator Start() => Plugin.Run();
        }

        private IEnumerator Run()
        {
            Directory.CreateDirectory(ResultsDir);
            _out = new StreamWriter(Path.Combine(ResultsDir, "probe.txt")) { AutoFlush = true };
            string save = File.ReadAllLines(TriggerPath)
                .Select(l => l.Split(new[] { '=' }, 2))
                .Where(p => p.Length == 2 && p[0].Trim() == "save")
                .Select(p => p[1].Trim())
                .FirstOrDefault() ?? "";
            Write($"Probe (Cotton Cow Mod not loaded: {!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.cottoncowmod.tots")})");
            Write($"Save: {save}");

            float start = Time.realtimeSinceStartup;
            while (SaveGameManager.Instance == null || SaveGameManager.Instance.FileSystem == null ||
                   Singleton<TotsSystemGameStates>.Instance == null)
                yield return null;

            var states = Singleton<TotsSystemGameStates>.Instance;
            while (states.Current != TotsSystemGameStates.RunningState.FrontEnd && Time.realtimeSinceStartup - start < 60f)
                yield return null;
            if (states.Current != TotsSystemGameStates.RunningState.FrontEnd)
                states.BootFrontEnd();
            while (states.Current != TotsSystemGameStates.RunningState.FrontEnd)
                yield return null;
            yield return new WaitForSeconds(3f);

            string fullPath = Path.Combine(Application.persistentDataPath, "Saves", save);
            if (!SaveGameManager.Instance.FileSystem.TryGetFile(fullPath, out var file) || file == null)
            {
                Check("save found", false, fullPath);
                Finish();
                yield break;
            }

            Singleton<TutorialManager>.Instance.Tutorials.Skip(shouldSkip: true);
            SaveGameManager.Instance.LoadGame(file, reloadWorld: true);
            states.LoadWorld();
            start = Time.realtimeSinceStartup;
            while (!(states.Current == TotsSystemGameStates.RunningState.InWorld &&
                     Singleton<GameStateManager>.Instance.CurrentState is InteractionGameState.DefaultGameState))
            {
                if (Time.realtimeSinceStartup - start > 400f)
                {
                    Check("world loaded", false, "timed out");
                    Finish();
                    yield break;
                }
                yield return null;
            }
            yield return new WaitForSeconds(5f);
            Write("World loaded.");

            var shops = FindObjectOfType<ShopStockHandler>();
            foreach (string npc in CottonNPCs)
            {
                var data = Singleton<RelationshipManager>.Instance.GetRelationshipData(npc);
                if (!Check($"{npc} relationship exists", data != null))
                    continue;
                int level = data.CurrentRelationshipLevel;
                Write($"{npc}: level {level}/{data.MaxLevel}, XP {data.CurrentXP}/{data.MaxXP}, thresholds {data.LevelXPThresholds.Length}");
                Check($"{npc} max level is vanilla", data.MaxLevel <= 10, $"max {data.MaxLevel}");

                if (shops != null)
                {
                    var entries = shops.GetShopStockEntries(npc);
                    int inRange = entries.Count(e => e.ShopStockEntryData.RelationshipInRange(level));
                    Check($"{npc} shop has stock at level {level}", inRange > 0, $"{inRange}/{entries.Count} entries in range");
                }
                else
                {
                    Check("shop stock handler found", false);
                }
            }

            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(ResultsDir, "probe.png"));
            yield return new WaitForSeconds(1f);

            // Save as vanilla would after playing without the mod, so a reinstall can be tested
            // on it (run-uninstall-test.sh keeps it in TestFixtures)
            Check("vanilla save written",
                SaveGameManager.Instance.SaveGame("Cotton Cow Vanilla", SaveGameManager.SaveType.Manual) != null);
            yield return new WaitForSeconds(5f);
            Finish();
        }

        private bool Check(string name, bool ok, string detail = "")
        {
            if (!ok)
                _failed++;
            Write($"{(ok ? "PASS" : "FAIL")} {name}" + (detail.Length > 0 ? $" ({detail})" : ""));
            return ok;
        }

        private void Write(string line)
        {
            _out.WriteLine(line);
            Logger.LogInfo("PROBE " + line);
        }

        private void Finish()
        {
            Write($"FINISHED: {(_failed == 0 ? "PASS" : "FAIL")}");
            _out.Dispose();
            File.WriteAllText(Path.Combine(ResultsDir, "status.txt"), _failed == 0 ? "PASS" : "FAIL");
            Application.Quit();
        }
    }
}
