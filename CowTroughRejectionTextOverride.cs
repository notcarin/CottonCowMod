using System.Collections.Generic;
using TMPro;
using TotS;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Overrides the rejection text shown when a player tries to place an invalid
    /// item in the CowTrough. The default text ("food items only") is baked into
    /// the Animator animation clip on the StorageTransferIcon prefab and can't be
    /// changed through normal runtime code.
    ///
    /// This component is added to the StorageUIScreen instance when CowTrough is open.
    /// It finds all TMP_Text children in the transfer icon hierarchy, disables their
    /// LocalizedText components (to prevent reversion), and overrides the text with
    /// CowDiet.DietDescription. LateUpdate re-applies the override each frame in case
    /// the Animator animation clip writes to the text property directly.
    ///
    /// Automatically destroyed when the StorageUIScreen is destroyed (UI closes).
    /// </summary>
    public class CowTroughRejectionTextOverride : MonoBehaviour
    {
        private readonly List<TMP_Text> _cachedTexts = new List<TMP_Text>();

        /// <summary>
        /// Called by StorageUIScreenPatch after the UI initializes.
        /// Scans the transfer icon's hierarchy for TMP_Text components and overrides them.
        /// </summary>
        public void Initialize(Transform transferIconTransform)
        {
            if (transferIconTransform == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughRejectionTextOverride: transferIconTransform is null.");
                return;
            }

            // Find ALL TMP_Text in the hierarchy, including inactive children
            // (the animation reveals them by activating their GameObjects)
            var texts = transferIconTransform.GetComponentsInChildren<TMP_Text>(true);

            foreach (var text in texts)
            {
                // Disable LocalizedText to prevent the localization system from
                // reverting our override on language change or LateUpdate
                var localizedText = text.GetComponent<LocalizedText>();
                if (localizedText != null)
                    localizedText.enabled = false;

                text.text = CowDiet.DietDescription;
                _cachedTexts.Add(text);
            }

            if (_cachedTexts.Count > 0)
                CottonCowModPlugin.Log.LogInfo(
                    $"CowTroughRejectionTextOverride: Overrode {_cachedTexts.Count} text(s) " +
                    $"with cow diet description.");
            else
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughRejectionTextOverride: No TMP_Text found in transfer icon hierarchy.");
        }

        /// <summary>
        /// Re-applies the text override each frame. Needed because the Animator
        /// animation clip may write directly to the TMP_Text.text property when
        /// the rejection animation plays, overwriting our initial set.
        /// </summary>
        private void LateUpdate()
        {
            for (int i = 0; i < _cachedTexts.Count; i++)
            {
                var text = _cachedTexts[i];
                if (text != null && text.text != CowDiet.DietDescription)
                    text.text = CowDiet.DietDescription;
            }
        }
    }
}
