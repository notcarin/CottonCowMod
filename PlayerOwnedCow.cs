using System.Collections;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Tag component added to player-owned cow GameObjects.
    /// Used to identify our modded cows vs NPC cows for interaction patching (Step 8).
    /// </summary>
    public class PlayerOwnedCow : MonoBehaviour
    {
        /// <summary>Whether this cow currently has milk ready to collect.</summary>
        public bool HasMilk { get; set; }

        /// <summary>Quality level of the milk (determined by food quality).</summary>
        public int MilkQuality { get; set; }

        /// <summary>Which Cotton NPC unlocked this cow ("Farmer_Cotton" or "Young_Tom_Cotton").</summary>
        public string OwnerCottonNPC { get; set; }

        /// <summary>The NPCInstanceInfo for returning the cow to the pool on despawn.</summary>
        public object NpcInstanceInfo { get; set; }
    }

    /// <summary>
    /// Waits one frame for CowMilkIndicator.Start() to finish creating the indicator,
    /// then activates it. Destroys itself after firing.
    /// </summary>
    public class DeferredMilkIndicatorActivator : MonoBehaviour
    {
        IEnumerator Start()
        {
            yield return null; // wait one frame for CowMilkIndicator.Start()
            var indicator = GetComponent<CowMilkIndicator>();
            if (indicator != null)
                indicator.SetMilkAvailable(true);
            Destroy(this);
        }
    }
}
