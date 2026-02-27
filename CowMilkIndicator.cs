using TotS.Character.NPCCharacter;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Visual indicator component attached to player-owned cow GameObjects.
    /// Clones the NPC quest indicator (exclamation point "!") and floats it above the cow
    /// when milk is available. Falls back to a simple yellow sphere if no NPC indicator
    /// is found in the scene.
    /// Hidden by default; toggled by CowProductionManager (on) and AnimalInteractionPatch (off).
    /// </summary>
    public class CowMilkIndicator : MonoBehaviour
    {
        private GameObject _indicator;
        private float _baseHeight = 1.5f;

        void Start()
        {
            CreateIndicator();
        }

        private void CreateIndicator()
        {
            _indicator = TryCloneQuestIndicator();

            if (_indicator == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowMilkIndicator: No NPC quest indicator found, using fallback sphere.");
                _indicator = CreateFallbackSphere();
            }

            if (_indicator != null)
            {
                _indicator.transform.SetParent(transform);
                _indicator.transform.localPosition = new Vector3(0f, _baseHeight, 0f);
                _indicator.SetActive(false);
            }
        }

        /// <summary>
        /// Finds any NPC with a quest indicator in the scene and clones it.
        /// The quest indicator is a simple "!" visual that all NPCs share.
        /// </summary>
        private GameObject TryCloneQuestIndicator()
        {
            var dialogues = FindObjectsOfType<NPCDialogue>(true); // include inactive
            foreach (var dialogue in dialogues)
            {
                var indicator = dialogue.QuestIndicator;
                if (indicator != null)
                {
                    // Clone the indicator (works even if it's currently inactive)
                    var clone = Instantiate(indicator);
                    clone.name = "CowMilkIndicator";

                    // Reset local transform so positioning is clean
                    clone.transform.localRotation = Quaternion.identity;
                    clone.transform.localScale = indicator.transform.localScale;

                    return clone;
                }
            }

            return null;
        }

        /// <summary>
        /// Fallback: creates a simple yellow sphere if NPC indicator cloning fails.
        /// </summary>
        private GameObject CreateFallbackSphere()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "CowMilkIndicatorFallback";
            sphere.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

            // Remove collider
            var col = sphere.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            // Yellow material
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Standard");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.color = new Color(1f, 0.92f, 0.016f);
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(1f, 0.92f, 0.016f) * 0.5f);
                    renderer.material = mat;
                }
            }

            return sphere;
        }

        public void SetMilkAvailable(bool available)
        {
            if (_indicator != null)
                _indicator.SetActive(available);
        }

        void Update()
        {
            // Gentle bob animation when visible
            if (_indicator == null || !_indicator.activeSelf) return;

            float bob = Mathf.Sin(Time.time * 2f) * 0.15f;
            _indicator.transform.localPosition = new Vector3(0f, _baseHeight + bob, 0f);
        }

        void OnDestroy()
        {
            if (_indicator != null)
                Destroy(_indicator);
        }
    }
}
