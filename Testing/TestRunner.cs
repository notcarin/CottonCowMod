#if COTTONCOW_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using TotS;
using TotS.DateTime;
using TotS.SaveGame;
using TotS.Tutorialisation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CottonCowMod.Testing
{
    /// <summary>
    /// Automated in-game test harness. Only compiled in the Test configuration
    /// (dotnet build -c Test), so release builds never contain it.
    ///
    /// Runs when BepInEx/config/CottonCowMod.tests.txt exists. That file holds
    /// key=value lines: save=&lt;file name in the Saves folder&gt;, suite=&lt;suite name&gt;.
    /// The runner loads the save, runs the suite, writes results and screenshots to
    /// BepInEx/TestResults/, then quits the game. Testing/run-tests.sh drives it.
    /// </summary>
    public class TestRunner : MonoBehaviour
    {
        private const float TimeoutSeconds = 900f;

        internal static readonly string TriggerPath = Path.Combine(Paths.ConfigPath, "CottonCowMod.tests.txt");
        internal static readonly string ResultsDir = Path.Combine(Paths.BepInExRootPath, "TestResults");

        private readonly Dictionary<string, string> _settings = new Dictionary<string, string>();
        private StreamWriter _results;
        private int _passed;
        private int _failed;
        private float _startTime;
        private bool _finished;

        public static void StartIfRequested()
        {
            if (!File.Exists(TriggerPath))
                return;

            var go = new GameObject("CottonCowTestRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<TestRunner>();
            CottonCowModPlugin.Log.LogWarning("TestRunner: Test mode enabled.");
        }

        private IEnumerator Start()
        {
            _startTime = Time.realtimeSinceStartup;
            foreach (string line in File.ReadAllLines(TriggerPath))
            {
                int eq = line.IndexOf('=');
                if (eq > 0)
                    _settings[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            Directory.CreateDirectory(ResultsDir);
            foreach (string old in Directory.GetFiles(ResultsDir))
                File.Delete(old);
            _results = new StreamWriter(Path.Combine(ResultsDir, "results.txt")) { AutoFlush = true };

            string suiteName = Setting("suite", "smoke");
            Log($"Suite: {suiteName}, save: {Setting("save", "(none)")}");

            IEnumerator suite = TestSuites.Get(suiteName, this);
            if (suite == null)
            {
                Fail("suite exists", $"Unknown suite '{suiteName}'");
            }
            else
            {
                yield return RunGuarded(suite);
            }

            Finish("done");
        }

        private void Update()
        {
            if (!_finished && Time.realtimeSinceStartup - _startTime > TimeoutSeconds)
            {
                Fail("timeout", $"Suite did not finish within {TimeoutSeconds}s");
                Finish("timeout");
            }
        }

        /// <summary>
        /// Runs a coroutine, recording any exception as a failure instead of stopping silently.
        /// </summary>
        internal IEnumerator RunGuarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                object current;
                try
                {
                    if (!stack.Peek().MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    current = stack.Peek().Current;
                }
                catch (Exception ex)
                {
                    Fail("exception", ex.ToString());
                    yield break;
                }

                if (current is IEnumerator nested)
                    stack.Push(nested);
                else
                    yield return current;
            }
        }

        private void Finish(string reason)
        {
            if (_finished)
                return;
            _finished = true;
            Log($"FINISHED ({reason}): {_passed} passed, {_failed} failed");
            _results?.Dispose();
            File.WriteAllText(Path.Combine(ResultsDir, "status.txt"), _failed == 0 ? "PASS" : "FAIL");
            Application.Quit();
        }

        // --- API for suites ---

        internal string Setting(string key, string fallback) =>
            _settings.TryGetValue(key, out var value) ? value : fallback;

        internal void Log(string message)
        {
            string line = $"[{Time.realtimeSinceStartup - _startTime,7:F1}s] {message}";
            _results?.WriteLine(line);
            CottonCowModPlugin.Log.LogInfo("TEST " + message);
        }

        internal bool Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                _passed++;
                Log($"PASS {name}" + (detail.Length > 0 ? $" ({detail})" : ""));
            }
            else
            {
                Fail(name, detail);
            }
            return condition;
        }

        internal void Fail(string name, string detail)
        {
            _failed++;
            Log($"FAIL {name}: {detail}");
        }

        internal IEnumerator Screenshot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(ResultsDir, name + ".png"));
            Log($"Screenshot: {name}.png");
            yield return null;
        }

        /// <summary>
        /// Waits for a condition. With pressContinue, taps E every couple of seconds to get past
        /// "press to continue" screens (day transitions, post-load cards); only use it while the
        /// player doesn't have control, where E can't interact with anything in the world.
        /// </summary>
        internal IEnumerator WaitUntil(string what, Func<bool> condition, float timeoutSeconds,
            bool pressContinue = false)
        {
            float start = Time.realtimeSinceStartup;
            float nextPress = start + ContinuePressInterval;
            int presses = 0;
            while (!SafeEval(condition))
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds)
                {
                    Fail("wait: " + what, $"timed out after {timeoutSeconds}s");
                    yield break;
                }
                if (pressContinue && Time.realtimeSinceStartup >= nextPress)
                {
                    yield return TapKey(Key.E);
                    presses++;
                    nextPress = Time.realtimeSinceStartup + ContinuePressInterval;
                    continue;
                }
                yield return null;
            }
            Log($"Ready: {what} ({Time.realtimeSinceStartup - start:F1}s" +
                (presses > 0 ? $", pressed E {presses}x" : "") + ")");
        }

        private const float ContinuePressInterval = 2f;

        /// <summary>
        /// Simulates a key press and release through the Input System.
        /// </summary>
        private static IEnumerator TapKey(Key key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                yield break;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private static bool SafeEval(Func<bool> condition)
        {
            try
            {
                return condition();
            }
            catch
            {
                return false;
            }
        }

        internal static bool InDefaultGameState() =>
            Singleton<GameStateManager>.Instance != null &&
            Singleton<GameStateManager>.Instance.CurrentState is InteractionGameState.DefaultGameState;

        /// <summary>
        /// Loads a save by file name (as in the Saves folder) and waits until the player can move.
        /// Mirrors the game's BenchmarkTool.LoadSaveAndWaitForWorld.
        /// </summary>
        internal IEnumerator LoadSave(string fileName)
        {
            yield return WaitUntil("save system ready", () =>
                SaveGameManager.Instance != null && SaveGameManager.Instance.FileSystem != null &&
                Singleton<TotsSystemGameStates>.Instance != null, 120f);

            string fullPath = Path.Combine(Application.persistentDataPath, "Saves", fileName);
            SaveGameManager.ISaveFile file = null;
            bool found = SaveGameManager.Instance.FileSystem.TryGetFile(fullPath, out file) && file != null;
            if (!Check("save file found", found, fileName))
                yield break;

            yield return LoadSave(file);
        }

        internal IEnumerator LoadSave(SaveGameManager.ISaveFile file)
        {
            var states = Singleton<TotsSystemGameStates>.Instance;
            if (states.Current == TotsSystemGameStates.RunningState.InWorld)
            {
                // RestartGame flags the front end as active immediately, while the world is
                // still unloading, so also wait for the player to be gone
                Log("Returning to the main menu before loading.");
                yield return WaitUntil("save system idle", () => SaveGameManager.Instance.IsIdle, 60f);
                Singleton<TotsGameStates>.Instance.RestartGame();
                yield return WaitUntil("world unloaded", () => Player.Instance == null, 120f);
                yield return new WaitForSeconds(5f);
            }
            else if (states.Current != TotsSystemGameStates.RunningState.FrontEnd)
            {
                // At startup the legal/splash screens come first; give them a moment, then skip them
                float start = Time.realtimeSinceStartup;
                while (states.Current != TotsSystemGameStates.RunningState.FrontEnd &&
                       Time.realtimeSinceStartup - start < 15f)
                    yield return null;
                if (states.Current != TotsSystemGameStates.RunningState.FrontEnd)
                {
                    Log($"Skipping startup screens (state: {states.Current}).");
                    states.BootFrontEnd();
                }
            }
            yield return WaitUntil("main menu", () =>
                states.Current == TotsSystemGameStates.RunningState.FrontEnd, 120f);
            // LoadGame silently does nothing while another save operation is running
            yield return WaitUntil("save system idle", () => SaveGameManager.Instance.IsIdle, 60f);
            yield return new WaitForSeconds(3f);
            Log($"Loading save: {file.DisplayName}");

            Singleton<TutorialManager>.Instance.Tutorials.Skip(shouldSkip: true);
            SaveGameManager.Instance.LoadGame(file, reloadWorld: true);
            Singleton<TotsSystemGameStates>.Instance.LoadWorld();
            Singleton<TutorialManager>.Instance.Tutorials.Skip(shouldSkip: true);

            yield return WaitUntil("in world", () =>
                Singleton<TotsSystemGameStates>.Instance.Current == TotsSystemGameStates.RunningState.InWorld, 300f);
            yield return WaitUntil("player has control", InDefaultGameState, 300f, pressContinue: true);
            Singleton<TutorialManager>.Instance.Tutorials.Skip(shouldSkip: true);
            yield return new WaitForSeconds(3f);
        }

        /// <summary>
        /// Saves to a new manual save and returns it via the callback.
        /// </summary>
        internal IEnumerator SaveGame(string displayName, Action<SaveGameManager.ISaveFile> onSaved)
        {
            var file = SaveGameManager.Instance.SaveGame(displayName, SaveGameManager.SaveType.Manual);
            if (!Check("save started", file != null, displayName))
                yield break;
            yield return new WaitForSeconds(5f);
            onSaved?.Invoke(file);
        }

        /// <summary>
        /// Sleeps to the next morning using the game's day transition and waits for control.
        /// </summary>
        internal IEnumerator SleepToNextMorning()
        {
            Singleton<DateTimeManager>.Instance.DoDayTransition(DayTransitionGameState.sDebugSequenceIdentifier, 1);
            yield return WaitUntil("day transition started", () => !InDefaultGameState(), 30f);
            yield return WaitUntil("next morning", InDefaultGameState, 180f, pressContinue: true);
            yield return new WaitForSeconds(3f);
        }
    }
}
#endif
