using TMPro;
using TotS;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Overrides the rejection text shown when a player tries to place an invalid
    /// item in the CowTrough. The chicken trough's Animator animation clip sets a
    /// TMP_Text named "Text" to "Must be a Nut, Grain or Berry". This component
    /// finds that text in the StorageUIScreen hierarchy and overrides it each frame.
    /// </summary>
    public class CowTroughRejectionTextOverride : MonoBehaviour
    {
        private const string ChickenTroughRejectionText = "Must be a Nut, Grain or Berry";

        private TMP_Text _rejectionText;
        private bool _searched;

        private void LateUpdate()
        {
            // Find the rejection text component once
            if (!_searched)
            {
                foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.gameObject.name == "Text" &&
                        (text.text == ChickenTroughRejectionText || text.text == CowDiet.DietDescription))
                    {
                        _rejectionText = text;

                        var localizedText = text.GetComponent<LocalizedText>();
                        if (localizedText != null)
                            localizedText.enabled = false;

                        break;
                    }
                }
                _searched = true;
            }

            if (_rejectionText != null && _rejectionText.text != CowDiet.DietDescription)
                _rejectionText.text = CowDiet.DietDescription;
        }
    }
}
