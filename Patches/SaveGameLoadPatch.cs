using HarmonyLib;
using TotS.SaveGame;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Tells PlayerCows a save (or new game) is starting to load, so it waits for the
    /// load to finish before evaluating cows again.
    /// </summary>
    [HarmonyPatch]
    public static class SaveGameLoadPatch
    {
        [HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.LoadGame))]
        [HarmonyPrefix]
        static void LoadGamePrefix() => PlayerCows.OnLoadStarted();

        [HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.NewGame))]
        [HarmonyPrefix]
        static void NewGamePrefix() => PlayerCows.OnLoadStarted();
    }
}
