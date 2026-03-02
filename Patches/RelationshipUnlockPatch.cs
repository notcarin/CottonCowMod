using HarmonyLib;
using TotS;
using TotS.Unlock;

namespace CottonCowMod.Patches
{
    /// <summary>
    /// Fires cow unlock strings when a Cotton NPC reaches relationship level 13.
    /// Adds the string to UnlockManager for persistence and fires NotifyUnlockRequest
    /// so PlayerCows and other listeners are notified.
    /// </summary>
    [HarmonyPatch(typeof(RelationshipManager), "RelationshipLevelChanged")]
    public static class RelationshipUnlockPatch
    {
        [HarmonyPostfix]
        static void Postfix(string relationshipTargetKey, int oldLevel, int newLevel, bool showPrompt)
        {
            if (newLevel != 13)
                return;

            string unlockString = null;

            if (relationshipTargetKey == "Farmer_Cotton")
                unlockString = "RELATIONSHIPUNLOCK_CowUnlock_1";
            else if (relationshipTargetKey == "Young_Tom_Cotton")
                unlockString = "RELATIONSHIPUNLOCK_CowUnlock_2";

            if (unlockString == null)
                return;

            CottonCowModPlugin.Log.LogInfo(
                $"RelationshipUnlockPatch: {relationshipTargetKey} reached level {newLevel}! " +
                $"Firing unlock: {unlockString}");

            // Add to persistent unlock storage
            Singleton<UnlockManager>.Instance.AddUnlockString(unlockString);
            // Fire event so PlayerCows and other systems are notified
            Singleton<UnlockManager>.Instance.NotifyUnlockRequest(unlockString, showPrompt);
        }
    }
}
