#if COTTONCOW_TESTS
using System.Collections.Generic;
using HarmonyLib;
using TotS;
using TotS.Inventory;
using TotS.Items;
using TotS.Mail;
using TotS.Unlock;
using UnityEngine;

namespace CottonCowMod.Testing
{
    /// <summary>
    /// Game-state helpers for test suites. They drive the game through the same code paths
    /// as normal play wherever possible (level changes, unlock requests, day transitions).
    /// </summary>
    internal static class TestHelpers
    {
        public static RelationshipManager.RelationshipData Relationship(string npc) =>
            Singleton<RelationshipManager>.Instance.AllRelationshipData.TryGetValue(npc, out var data) ? data : null;

        /// <summary>
        /// Sets a relationship level through LevelingHandler.SetLevel, which raises
        /// LevelChanged like a real level-up (rewards, the cow unlock patch, etc.).
        /// </summary>
        public static void SetRelationshipLevel(string npc, int level)
        {
            var handler = Traverse.Create(Relationship(npc)).Field("m_LevelingHandler").GetValue();
            Traverse.Create(handler).Method("SetLevel", level, false).GetValue();
        }

        /// <summary>
        /// Unlocks a garden area the way club rewards do: an unlock request the game's
        /// GardenAreaUnlockController handles.
        /// </summary>
        public static void UnlockGardenArea(int area) =>
            Singleton<UnlockManager>.Instance.NotifyUnlockRequest($"Unlock_Garden_Area_{area}", false);

        public static bool HasUnlock(string key) => Singleton<UnlockManager>.Instance.ContainsUnlockString(key);

        public static int CountMail(string mailID)
        {
            var mail = Traverse.Create(MailManager.Instance);
            int count = 0;
            foreach (string list in new[] { "m_QueuedMail", "m_ActiveMail", "m_ArchivedMail" })
            {
                var items = mail.Field(list).GetValue<List<MailManager.MailItem>>();
                if (items == null)
                    continue;
                foreach (var item in items)
                {
                    if (item.MailParams.m_MailID == mailID)
                        count++;
                }
            }
            return count;
        }

        public static string CowLetterID(bool isCow1) => "COTTONCOWMOD_CowLetter_" + PlayerCows.OwnerNPC(isCow1);

        public static int CountItems(Inventory inventory, string itemTypeName) =>
            inventory == null ? 0 : inventory.Count(item => item.ItemType != null && item.ItemType.name == itemTypeName);

        /// <summary>
        /// Counts Cow Feeding Troughs anywhere in the save (any inventory or placed), using
        /// ItemManager's registry of every item instance.
        /// </summary>
        public static int CountCowTroughs()
        {
            var troughType = CowTroughManager.CowTroughItemType;
            var instances = Traverse.Create(Singleton<ItemManager>.Instance).Field("m_ItemInstances")
                .GetValue<System.Collections.IDictionary>();
            int count = 0;
            foreach (var value in instances.Values)
            {
                if (value is Item item && item.ItemType == troughType)
                    count++;
            }
            return count;
        }

        public static bool AddToInventory(Inventory inventory, string itemTypeName, int quality, int count)
        {
            var type = Singleton<ItemManager>.Instance.GetItemType(it => it.name == itemTypeName);
            if (type == null)
                return false;
            for (int i = 0; i < count; i++)
            {
                if (!inventory.Add(new Variation(type, quality).CreateItem()))
                    return false;
            }
            return true;
        }

        public static GameObject Cow(bool isCow1) => isCow1 ? CowSpawner.GetCow1GO() : CowSpawner.GetCow2GO();

        public static PlayerOwnedCow CowTag(bool isCow1) => Cow(isCow1)?.GetComponent<PlayerOwnedCow>();

        /// <summary>
        /// Moves the player a few meters from a point, facing it, so screenshots show it.
        /// </summary>
        public static void WarpPlayerNear(Vector3 target)
        {
            var player = Player.Instance;
            // Stand on the glade side of the target, so we don't land off the edge of the map
            Vector3 away = GladeArea.Built && (GladeArea.Center - target).sqrMagnitude > 1f
                ? (GladeArea.Center - target).WithY(0f).normalized
                : new Vector3(0.7f, 0f, -0.7f);
            Vector3 from = target + away * 6f;
            if (UnityEngine.AI.NavMesh.SamplePosition(from, out var hit, 4f, -1))
                from = hit.position;
            var rotation = Quaternion.LookRotation((target - from).WithY(0f));
            player.Movement.WarpImmediately(from, rotation);
        }

        private static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
    }
}
#endif
