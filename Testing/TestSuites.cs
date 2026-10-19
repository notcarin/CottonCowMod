#if COTTONCOW_TESTS
using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using TotS;
using TotS.Inventory;
using TotS.SaveGame;
using TotS.Unlock;
using CottonCowMod.Patches;
using UnityEngine;

namespace CottonCowMod.Testing
{
    /// <summary>
    /// Test suites, selected by the suite= line in the trigger file.
    /// </summary>
    internal static class TestSuites
    {
        private const string Farmer = "Farmer_Cotton";
        private const string YoungTom = "Young_Tom_Cotton";

        public static IEnumerator Get(string name, TestRunner t)
        {
            switch (name)
            {
                case "smoke": return Smoke(t);
                case "lifecycle": return Lifecycle(t);
                case "reload": return Reload(t);
                case "upgrade": return Upgrade(t);
                case "photos": return PhotoSuite.Run(t);
                default: return null;
            }
        }

        /// <summary>
        /// Loads the save, checks the mod initialized, and records its view of the game state.
        /// </summary>
        private static IEnumerator Smoke(TestRunner t)
        {
            yield return t.LoadSave(t.Setting("save", ""));

            t.Check("player exists", Player.Instance != null);
            t.Check("unlock manager exists", Singleton<UnlockManager>.Exists);
            t.Check("cow trough inventory created", CowTroughManager.Instance.HasInventory());
            yield return t.WaitUntil("mod sees save ready", () => PlayerCows.SaveReady, 30f);
            t.Check("trough UI prefab cached", CowTroughManager.CachedUIPrefab != null);

            WriteState("state.txt");
            yield return t.Screenshot("smoke-loaded");

            // The bug report toggle, as ModdingTools' Mods tab would flip it
            string desktopReport = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "CottonCowMod_BugReport.txt");
            File.Delete(desktopReport);
            CottonCowModPlugin.CreateBugReport.Value = true;
            yield return null;
            t.Check("bug report toggle switched itself off", !CottonCowModPlugin.CreateBugReport.Value);
            t.Check("bug report saved to Desktop", File.Exists(desktopReport));
            if (File.Exists(desktopReport))
            {
                string report = File.ReadAllText(desktopReport);
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                t.Check("bug report has the summary and log",
                    report.StartsWith("=== Cotton Cow Mod Bug Report") && report.Contains("--- Log"));
                t.Check("bug report has no Windows user folder",
                    report.IndexOf(profile, StringComparison.OrdinalIgnoreCase) < 0 &&
                    report.IndexOf(profile.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) < 0);
                File.Copy(desktopReport, Path.Combine(TestRunner.ResultsDir, "bug-report.txt"), true);
                File.Delete(desktopReport);
            }
            string clipboard = GUIUtility.systemCopyBuffer ?? "";
            t.Check("bug report copied to clipboard",
                clipboard.StartsWith("=== Cotton Cow Mod Bug Report") && clipboard.Length <= 9500,
                $"{clipboard.Length} chars");
        }

        /// <summary>
        /// A player's whole journey on a save from before the cows: reach level 13, unlock
        /// the glade, receive the cows, check they stay in the glade, feed and milk them,
        /// then save and reload and check nothing was lost or sent twice.
        /// </summary>
        private static IEnumerator Lifecycle(TestRunner t)
        {
            yield return t.LoadSave(t.Setting("save", ""));
            yield return t.WaitUntil("mod sees save ready", () => PlayerCows.SaveReady, 30f);
            WriteState("state-start.txt");

            t.Check("save starts without delivered cows",
                !SaveState.GetBool("Cow1Delivered") && !SaveState.GetBool("Cow2Delivered"),
                string.Join(", ", SaveState.All()));
            t.Check("trough UI prefab cached", CowTroughManager.CachedUIPrefab != null);
            bool gardenAtStart = PlayerCows.IsGardenArea5Unlocked;
            t.Log($"Glade unlocked at start: {gardenAtStart}");

            // --- Relationship unlock ---
            TestHelpers.SetRelationshipLevel(Farmer, 13);
            TestHelpers.SetRelationshipLevel(YoungTom, 13);
            yield return new WaitForSeconds(1f);
            t.Check("Farmer Cotton level 13", TestHelpers.Relationship(Farmer).CurrentRelationshipLevel == 13,
                $"level {TestHelpers.Relationship(Farmer).CurrentRelationshipLevel}");
            t.Check("Young Tom level 13", TestHelpers.Relationship(YoungTom).CurrentRelationshipLevel == 13,
                $"level {TestHelpers.Relationship(YoungTom).CurrentRelationshipLevel}");
            t.Check("cow unlocks recorded",
                TestHelpers.HasUnlock(PlayerCows.UnlockString(true)) && TestHelpers.HasUnlock(PlayerCows.UnlockString(false)));

            if (!gardenAtStart)
            {
                t.Check("need-space letters sent",
                    TestHelpers.HasUnlock("COTTONCOWMOD_NeedSpaceLetter_" + Farmer) &&
                    TestHelpers.HasUnlock("COTTONCOWMOD_NeedSpaceLetter_" + YoungTom));
                t.Check("no cow pending without the glade",
                    !SaveState.GetBool("Cow1Pending") && !SaveState.GetBool("Cow2Pending"));
            }

            // --- Glade unlock, the way club rewards do it ---
            for (int area = 2; area <= 5; area++)
                TestHelpers.UnlockGardenArea(area);
            yield return new WaitForSeconds(2f);
            t.Check("mod sees glade unlocked", PlayerCows.IsGardenArea5Unlocked);
            bool pendingNow = SaveState.GetBool("Cow1Pending") && SaveState.GetBool("Cow2Pending");
            t.Log($"Cows pending right after unlock: {pendingNow} (otherwise picked up next morning)");

            // --- Delivery ---
            yield return t.SleepToNextMorning();
            if (!SaveState.GetBool("Cow1Delivered"))
            {
                t.Log("Not delivered after one night (unlock was picked up in the morning), sleeping again.");
                yield return t.SleepToNextMorning();
            }
            foreach (bool isCow1 in new[] { true, false })
            {
                string cow = isCow1 ? "Cow1" : "Cow2";
                t.Check($"{cow} delivered", SaveState.GetBool(cow + "Delivered"));
                t.Check($"{cow} pending cleared", !SaveState.GetBool(cow + "Pending"));
                t.Check($"{cow} letter sent once", TestHelpers.CountMail(TestHelpers.CowLetterID(isCow1)) == 1,
                    $"count {TestHelpers.CountMail(TestHelpers.CowLetterID(isCow1))}");
            }
            t.Check("trough given once", TestHelpers.HasUnlock(CowTroughManager.TroughGivenUnlockString) &&
                                         TestHelpers.CountCowTroughs() == 1, $"troughs {TestHelpers.CountCowTroughs()}");

            yield return t.WaitUntil("cows spawned", () => CowSpawner.Cow1Spawned && CowSpawner.Cow2Spawned, 120f);
            t.Check("glade found", GladeArea.Built);
            yield return CheckCowsStayInGlade(t, 60f);

            TestHelpers.WarpPlayerNear(TestHelpers.Cow(true).transform.position);
            yield return new WaitForSeconds(4f);
            yield return t.Screenshot("cows-in-glade");

            // --- Feeding and milking ---
            var trough = CowTroughManager.Instance.GetInventory();
            t.Check("food added to trough", TestHelpers.AddToInventory(trough, "Ingredient_Carrot", 2, 2));
            yield return t.SleepToNextMorning();
            t.Check("trough food eaten", trough.Items.Count == 0, $"{trough.Items.Count} left");
            t.Check("Cow1 has milk", TestHelpers.CowTag(true)?.HasMilk == true);
            t.Check("Cow2 has milk", TestHelpers.CowTag(false)?.HasMilk == true);
            t.Check("milk saved per save", SaveState.GetBool("Cow1HasMilk") && SaveState.GetBool("Cow2HasMilk"));

            var backpack = Singleton<InventoryManager>.Instance.Backpack;
            int milkBefore = TestHelpers.CountItems(backpack, "Ingredient_Milk");
            AnimalInteractionPatch.GiveMilkToPlayer(TestHelpers.CowTag(true), TestHelpers.Cow(true));
            t.Check("milking gives milk", TestHelpers.CountItems(backpack, "Ingredient_Milk") == milkBefore + 1,
                $"{milkBefore} -> {TestHelpers.CountItems(backpack, "Ingredient_Milk")}");
            t.Check("Cow1 milk cleared", TestHelpers.CowTag(true).HasMilk == false && !SaveState.GetBool("Cow1HasMilk"));

            // --- Save (Cow2 keeps her milk); the "reload" suite checks it in a fresh launch ---
            CheckSavedRelationshipsAreVanilla(t);
            yield return t.SaveGame("Cotton Cow Test", null);
            t.Check("Cotton levels intact after saving",
                TestHelpers.Relationship(Farmer).CurrentRelationshipLevel == 13 &&
                TestHelpers.Relationship(Farmer).MaxLevel == 13);
            WriteState("state-end.txt");
        }

        /// <summary>
        /// Run in a fresh game launch on the "Cotton Cow Test" save the lifecycle suite made,
        /// like a player coming back the next day: cows are there straight away, nothing is
        /// sent twice, and milk and Cotton levels survived the save.
        /// </summary>
        private static IEnumerator Reload(TestRunner t)
        {
            yield return t.LoadSave(t.Setting("save", ""));
            yield return t.WaitUntil("mod sees save ready", () => PlayerCows.SaveReady, 30f);
            WriteState("state-start.txt");

            yield return t.WaitUntil("cows there right after loading",
                () => CowSpawner.Cow1Spawned && CowSpawner.Cow2Spawned, 60f);
            t.Check("cows still delivered", SaveState.GetBool("Cow1Delivered") && SaveState.GetBool("Cow2Delivered"));
            t.Check("no cow pending again", !SaveState.GetBool("Cow1Pending") && !SaveState.GetBool("Cow2Pending"));
            t.Check("no letters re-sent on load",
                TestHelpers.CountMail(TestHelpers.CowLetterID(true)) == 1 &&
                TestHelpers.CountMail(TestHelpers.CowLetterID(false)) == 1,
                $"Cow1 {TestHelpers.CountMail(TestHelpers.CowLetterID(true))}, Cow2 {TestHelpers.CountMail(TestHelpers.CowLetterID(false))}");
            t.Check("Cow1 still milked after loading", TestHelpers.CowTag(true)?.HasMilk == false);
            t.Check("Cow2 milk kept after loading", TestHelpers.CowTag(false)?.HasMilk == true);
            t.Check("Farmer Cotton level 13 after loading",
                TestHelpers.Relationship(Farmer).CurrentRelationshipLevel == 13,
                $"level {TestHelpers.Relationship(Farmer).CurrentRelationshipLevel}, XP {TestHelpers.Relationship(Farmer).CurrentXP}");
            t.Check("Young Tom level 13 after loading",
                TestHelpers.Relationship(YoungTom).CurrentRelationshipLevel == 13,
                $"level {TestHelpers.Relationship(YoungTom).CurrentRelationshipLevel}, XP {TestHelpers.Relationship(YoungTom).CurrentXP}");
            t.Check("trough still single", TestHelpers.CountCowTroughs() == 1, $"troughs {TestHelpers.CountCowTroughs()}");

            yield return CheckCowsStayInGlade(t, 30f);
            TestHelpers.WarpPlayerNear(TestHelpers.Cow(false).transform.position);
            yield return new WaitForSeconds(4f);
            yield return t.Screenshot("after-reload");

            // A night after loading: still no repeat letters, and the trough keeps working
            var trough = CowTroughManager.Instance.GetInventory();
            t.Check("food added to trough", TestHelpers.AddToInventory(trough, "Ingredient_Apple", 1, 1));
            yield return t.SleepToNextMorning();
            t.Check("Cow1 fed and has milk", TestHelpers.CowTag(true)?.HasMilk == true);
            t.Check("still one letter each after a night",
                TestHelpers.CountMail(TestHelpers.CowLetterID(true)) == 1 &&
                TestHelpers.CountMail(TestHelpers.CowLetterID(false)) == 1);
            WriteState("state-end.txt");
        }

        /// <summary>
        /// Loads a save from an older version (cows already delivered) and checks the
        /// migration: cows marked delivered and present immediately, no repeat letters.
        /// </summary>
        private static IEnumerator Upgrade(TestRunner t)
        {
            yield return t.LoadSave(t.Setting("save", ""));
            yield return t.WaitUntil("mod sees save ready", () => PlayerCows.SaveReady, 30f);
            WriteState("state-start.txt");

            t.Check("save state version written", SaveState.Has("Version"));
            foreach (bool isCow1 in new[] { true, false })
            {
                string cow = isCow1 ? "Cow1" : "Cow2";
                bool unlocked = TestHelpers.HasUnlock(PlayerCows.UnlockString(isCow1));
                t.Log($"{cow}: unlocked={unlocked} delivered={SaveState.GetBool(cow + "Delivered")} " +
                      $"letters={TestHelpers.CountMail(TestHelpers.CowLetterID(isCow1))}");
                if (unlocked && PlayerCows.IsGardenArea5Unlocked &&
                    TestHelpers.HasUnlock(CowTroughManager.TroughGivenUnlockString))
                {
                    t.Check($"{cow} migrated as delivered", SaveState.GetBool(cow + "Delivered"));
                    t.Check($"{cow} not pending again", !SaveState.GetBool(cow + "Pending"));
                }
            }

            yield return t.WaitUntil("delivered cows spawned on load", () =>
                (!SaveState.GetBool("Cow1Delivered") || CowSpawner.Cow1Spawned) &&
                (!SaveState.GetBool("Cow2Delivered") || CowSpawner.Cow2Spawned), 60f);

            if (CowSpawner.Cow1Spawned || CowSpawner.Cow2Spawned)
            {
                yield return CheckCowsStayInGlade(t, 30f);
                var cow = CowSpawner.Cow1Spawned ? TestHelpers.Cow(true) : TestHelpers.Cow(false);
                TestHelpers.WarpPlayerNear(cow.transform.position);
                yield return new WaitForSeconds(4f);
                yield return t.Screenshot("upgrade-cows");
            }

            // The repair path for saves broken by uninstalling 1.0.1: load with 1.0.2, sleep, save.
            // run-uninstall-test.sh on the resulting "Cotton Cow Upgraded" save checks the result.
            int levelBefore = TestHelpers.Relationship(Farmer).CurrentRelationshipLevel;
            yield return t.SleepToNextMorning();
            int letters1 = TestHelpers.CountMail(TestHelpers.CowLetterID(true));
            int letters2 = TestHelpers.CountMail(TestHelpers.CowLetterID(false));
            t.Log($"Letters after a night: Cow1={letters1} Cow2={letters2}");
            t.Check("Cotton level kept overnight",
                TestHelpers.Relationship(Farmer).CurrentRelationshipLevel == levelBefore,
                $"{levelBefore} -> {TestHelpers.Relationship(Farmer).CurrentRelationshipLevel}");
            CheckSavedRelationshipsAreVanilla(t);
            yield return t.SaveGame("Cotton Cow Upgraded", null);
            WriteState("state-end.txt");
        }

        /// <summary>
        /// Samples spawned cows' positions every second and checks they stay in the glade:
        /// never more than 4m outside, and never more than 2m outside for over 12 seconds
        /// (brushing the edge is fine; the leash brings strays back). Logs every outside sample.
        /// </summary>
        private static IEnumerator CheckCowsStayInGlade(TestRunner t, float seconds)
        {
            const float MaxDistance = 4f;
            const float StrayDistance = 2f;
            const float MaxStraySeconds = 12f;

            int samples = 0;
            int outside = 0;
            float maxDistance = 0f;
            var straySince = new float[2] { -1f, -1f };
            float longestStray = 0f;
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                for (int i = 0; i < 2; i++)
                {
                    bool isCow1 = i == 0;
                    var cow = TestHelpers.Cow(isCow1);
                    if (cow == null)
                        continue;
                    samples++;
                    float distance = GladeArea.DistanceOutside(cow.transform.position);
                    maxDistance = Mathf.Max(maxDistance, distance);
                    if (distance > 0f)
                    {
                        outside++;
                        var follow = cow.GetComponent<CowFollowBehavior>();
                        t.Log($"  {(isCow1 ? "Cow1" : "Cow2")} {distance:F1}m outside at {cow.transform.position}" +
                              (follow != null && follow.IsFollowing ? " (following player)" : "") +
                              (follow != null && follow.IsReturning ? " (walking back)" : ""));
                    }

                    if (distance > StrayDistance)
                    {
                        if (straySince[i] < 0f)
                            straySince[i] = Time.realtimeSinceStartup;
                        longestStray = Mathf.Max(longestStray, Time.realtimeSinceStartup - straySince[i]);
                    }
                    else
                    {
                        straySince[i] = -1f;
                    }
                }
                yield return new WaitForSeconds(1f);
            }
            t.Check($"cows stay in glade ({seconds:F0}s)",
                samples > 0 && maxDistance <= MaxDistance && longestStray <= MaxStraySeconds,
                $"{outside}/{samples} samples outside, furthest {maxDistance:F1}m, longest stray {longestStray:F0}s");
        }

        /// <summary>
        /// Writes the relationship save record into memory (through our save patch) and checks
        /// the Cotton entries are what vanilla expects: max level 10, XP within vanilla's max.
        /// </summary>
        private static void CheckSavedRelationshipsAreVanilla(TestRunner t)
        {
            try
            {
                var manager = Singleton<RelationshipManager>.Instance;
                var stream = new MemoryStream();
                Traverse.Create(manager).Method("WriteVersionData2", new BinaryWriter(stream)).GetValue();
                stream.Position = 0;

                var recordType = AccessTools.Inner(typeof(RelationshipManager), "PersistentDataVersion2");
                var serializer = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("ProtoBuf.Serializer"))
                    .First(type => type != null);
                var prefixStyle = Enum.Parse(serializer.Assembly.GetType("ProtoBuf.PrefixStyle"), "Fixed32");
                var deserialize = serializer.GetMethods()
                    .First(m => m.Name == "DeserializeWithLengthPrefix" && m.IsGenericMethod && m.GetParameters().Length == 2)
                    .MakeGenericMethod(recordType);
                object record = deserialize.Invoke(null, new[] { stream, prefixStyle });

                var entries = Traverse.Create(record).Field("relationshipDataRepresentations").GetValue<Array>();
                foreach (object entry in entries)
                {
                    var e = Traverse.Create(entry);
                    string key = e.Field("key").GetValue<string>();
                    if (!LevelCapPatches.IsCottonNPC(key))
                        continue;
                    int maxLevel = e.Field("maxLevel").GetValue<int>();
                    int[] thresholds = e.Field("levelXPThresholds").GetValue<int[]>();
                    int xp = e.Field("currentXP").GetValue<int>();
                    t.Check($"{key} saved vanilla-safe",
                        maxLevel == RelationshipSavePatch.VanillaMaxLevel && thresholds.Length == RelationshipSavePatch.VanillaMaxLevel
                        && xp <= thresholds.Sum(),
                        $"maxLevel {maxLevel}, thresholds {thresholds.Length}, XP {xp}/{thresholds.Sum()}");
                }

                var unlockStrings = Traverse.Create(record).Field("unlockStrings").GetValue<System.Collections.Generic.List<string>>();
                t.Check("real Cotton XP stored in save",
                    unlockStrings != null && unlockStrings.Count(s => s.StartsWith("COTTONCOWMOD_XP_")) == 2,
                    unlockStrings == null ? "no unlock strings" : string.Join(", ", unlockStrings.Where(s => s.StartsWith("COTTONCOWMOD_XP_"))));
                t.Check("live data restored after write", TestHelpers.Relationship(Farmer).MaxLevel == 13);
            }
            catch (Exception ex)
            {
                t.Fail("saved relationships vanilla-safe", ex.ToString());
            }
        }

        private static void WriteState(string fileName)
        {
            File.WriteAllText(Path.Combine(TestRunner.ResultsDir, fileName),
                BugReport.BuildSummary(CottonCowModPlugin.Instance.Config));
        }
    }
}
#endif
