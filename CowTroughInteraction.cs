using HarmonyLib;
using TotS;
using TotS.Audio;
using TotS.UI;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Added to placed CowFeedingTrough GameObjects to make them function as cow troughs.
    /// Implements IPhysicalStorage so we can create a PhysicalStorageInspectionGameState
    /// that opens the StorageUIScreen with our CowTrough inventory.
    ///
    /// If the trough prefab lacks an Interactable component (as Trough_Metal does),
    /// one is created at runtime and wired up via reflection.
    ///
    /// Applies a warm copper/rust tint to the material so players can distinguish
    /// this from a regular decorative Metal Feeding Trough at a glance.
    /// </summary>
    public class CowTroughInteraction : MonoBehaviour,
        IPhysicalStorage,
        Interactable.IVerbTouch,
        Interactable.IVerbQuickInteract
    {
        private Interactable _interactable;
        private PhysicalStorageInspectionGameState _gameState;
        private ToolTipManager.TipGroup _toolTips;

        /// <summary>
        /// Warm copper/rust tint multiplied with the material's existing base color.
        /// Makes the trough look slightly weathered and warmer than the stock metal.
        /// </summary>
        private static readonly Color TroughTint = new Color(0.82f, 0.65f, 0.50f, 1f);

        // IPhysicalStorage
        public GameObject UIPrefab => CowTroughManager.CachedUIPrefab;
        public StorageInventory StorageInventory => null;
        public UIMusicConfiguration UIMusicConfiguration => null;

        private void Start()
        {
            // Cache the UI prefab if not already done
            CowTroughManager.CacheUIPrefabIfNeeded();

            // Find the Hub (InteractableHub) on this object
            var hub = GetComponent<Hub>();
            if (hub == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughInteraction: No Hub found on trough object!");
                return;
            }

            _interactable = hub.Interactable;

            // If the prefab lacks an Interactable component, create one at runtime
            if (_interactable == null)
            {
                CottonCowModPlugin.Log.LogInfo(
                    "CowTroughInteraction: No Interactable on prefab, creating one at runtime.");
                _interactable = CreateInteractableRuntime(hub);
            }

            if (_interactable == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughInteraction: Failed to create Interactable. Interaction won't work.");
                return;
            }

            // Register our interaction verbs
            _interactable.SetTouchVerb(this);
            _interactable.SetQuickInteractVerb(this);

            // Set up tooltip (show "Use" prompt when player is near)
            try
            {
                _toolTips = new ToolTipManager.TipGroup(
                    ToolTipManager.Instance.DefaultGameTipSite);
                _toolTips.AddTip(
                    ToolTipGlyph.Interaction_Use,
                    "TOOLTIP_DECORATION_STORAGE_CLOSE",
                    ToolTipManager.Tip.DisplayFlags.ForceHideProgress);
                _toolTips.Active = false;
            }
            catch
            {
                // Tooltip setup is non-critical
            }

            // Tint the trough a warm copper/rust color for visual distinction
            ApplyTroughTint();

            CottonCowModPlugin.Log.LogInfo(
                "CowTroughInteraction: Registered interaction verbs successfully.");
        }

        /// <summary>
        /// Tints the trough's materials a warm copper/rust color so it's visually
        /// distinct from a regular decorative Metal Feeding Trough.
        /// Uses renderer.material (not sharedMaterial) to create per-instance copies
        /// that don't affect other Trough_Metal decorations.
        /// </summary>
        private void ApplyTroughTint()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            int tinted = 0;

            foreach (var renderer in renderers)
            {
                // renderer.materials creates instance copies (doesn't affect shared material)
                var materials = renderer.materials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];

                    // Try URP property first, then legacy
                    if (mat.HasProperty("_BaseColor"))
                    {
                        var original = mat.GetColor("_BaseColor");
                        mat.SetColor("_BaseColor", original * TroughTint);
                        changed = true;
                    }
                    else if (mat.HasProperty("_Color"))
                    {
                        var original = mat.GetColor("_Color");
                        mat.SetColor("_Color", original * TroughTint);
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.materials = materials;
                    tinted++;
                }
            }

            if (tinted > 0)
                CottonCowModPlugin.Log.LogInfo(
                    $"CowTroughInteraction: Applied warm tint to {tinted} renderer(s).");
        }

        /// <summary>
        /// Creates an Interactable component at runtime and wires up all references
        /// via Harmony's Traverse (reflection) since AutoPopulate is editor-time only.
        /// Also sets up InteractableCollider on child colliders for raycast detection.
        /// </summary>
        private Interactable CreateInteractableRuntime(Hub hub)
        {
            try
            {
                // Add the Interactable component (Awake runs immediately, sets NavMeshObstacle)
                var interactable = gameObject.AddComponent<Interactable>();

                // Wire InteractableBase.m_Hub → this Hub (for IHubProvider)
                Traverse.Create(interactable).Field("m_Hub").SetValue(hub);

                // Wire InteractableHub.m_Interactable → new Interactable
                Traverse.Create(hub).Field("m_Interactable").SetValue(interactable);

                CottonCowModPlugin.Log.LogInfo(
                    "CowTroughInteraction: Created Interactable and wired Hub references.");

                // Set up InteractableColliders on child objects that have Unity Colliders
                // This is needed so the game's raycast system can map hits to our Interactable
                SetupInteractableColliders(hub, interactable);

                return interactable;
            }
            catch (System.Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"CowTroughInteraction: CreateInteractableRuntime exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Finds all Unity Colliders in child objects and adds InteractableCollider
        /// components so the game's interaction raycast can detect the trough.
        /// </summary>
        private void SetupInteractableColliders(Hub hub, Interactable interactable)
        {
            var colliders = GetComponentsInChildren<Collider>();
            int count = 0;

            foreach (var col in colliders)
            {
                // Skip if already has ANY GameCollider subclass (InteractableCollider
                // or PlaceableCollider). Adding a second GameCollider would conflict
                // with the decoration system's ability to pick up/move the trough.
                if (col.GetComponent<GameCollider>() != null)
                    continue;

                // Skip trigger colliders (they're typically used for other systems)
                if (col.isTrigger)
                    continue;

                try
                {
                    var ic = col.gameObject.AddComponent<InteractableCollider>();

                    // Wire the private serialized fields via reflection
                    Traverse.Create(ic).Field("m_Collider").SetValue(col);
                    Traverse.Create(ic).Field("m_Hub").SetValue(hub);
                    Traverse.Create(ic).Field("m_Interactable").SetValue(interactable);

                    // InteractableCollider.OnEnable() auto-calls interactable.AddCollider(this)
                    count++;
                }
                catch (System.Exception ex)
                {
                    CottonCowModPlugin.Log.LogWarning(
                        $"CowTroughInteraction: Failed to add InteractableCollider to {col.gameObject.name}: {ex.Message}");
                }
            }

            CottonCowModPlugin.Log.LogInfo(
                $"CowTroughInteraction: Added InteractableCollider to {count} collider(s).");
        }

        private void OnDisable()
        {
            if (_interactable != null)
            {
                _interactable.ClearTouchVerb(this);
                _interactable.ClearQuickInteractVerb(this);
            }

            if (_toolTips != null)
                _toolTips.Active = false;
        }

        // IVerbTouch
        void Interactable.IVerbTouch.TouchPreTouch(
            Interactable sender, Interactable.PreTouchArgs args) { }

        void Interactable.IVerbTouch.TouchStart(
            Interactable sender, Interactable.TouchingArgs args)
        {
            if (_toolTips != null)
                _toolTips.Active = true;
        }

        void Interactable.IVerbTouch.TouchUpdate(
            Interactable sender, Interactable.TouchingArgs args) { }

        void Interactable.IVerbTouch.TouchEnd(
            Interactable sender, Interactable.TouchingArgs args)
        {
            if (_toolTips != null)
                _toolTips.Active = false;
        }

        // IVerbQuickInteract
        Holdable.QuickUsingResult Interactable.IVerbQuickInteract.QuickInteractStart(
            Interactable sender, Interactable.QuickInteractingArgs args)
        {
            return Holdable.QuickUsingResult.Continue;
        }

        Holdable.QuickUsingResult Interactable.IVerbQuickInteract.QuickInteractUpdate(
            Interactable sender, Interactable.QuickInteractingArgs args)
        {
            return Holdable.QuickUsingResult.Continue;
        }

        Holdable.QuickUsingResult Interactable.IVerbQuickInteract.QuickInteractEvent(
            Interactable sender, Interactable.QuickInteractingArgs args)
        {
            return Holdable.QuickUsingResult.Continue;
        }

        void Interactable.IVerbQuickInteract.QuickInteractEnd(
            Interactable sender,
            Holdable.QuickUsingEndReason reason,
            Interactable.QuickInteractingArgs args)
        {
            if (reason != Holdable.QuickUsingEndReason.Used)
                return;

            // Retry caching UIPrefab if it wasn't available earlier
            if (CowTroughManager.CachedUIPrefab == null)
                CowTroughManager.CacheUIPrefabIfNeeded();

            if (CowTroughManager.CachedUIPrefab == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowTroughInteraction: No cached UIPrefab, cannot open trough UI.");
                return;
            }

            // Signal the StorageUIScreen patch to swap inventory name
            CowTroughManager.IsOpeningCowTrough = true;

            // Create the game state that opens the storage UI
            _gameState = new PhysicalStorageInspectionGameState(
                this,
                Singleton<TotsGameStates>.Instance.InGameState);
            _gameState.Enabled = true;

            CottonCowModPlugin.Log.LogInfo("CowTroughInteraction: Opened CowTrough UI.");
        }
    }
}
