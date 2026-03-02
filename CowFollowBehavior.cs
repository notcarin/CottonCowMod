using TotS;
using TotS.Animals;
using TotS.Character;
using TotS.Character.NPCCharacter;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// When the CowTrough is empty, makes the cow follow the player at a leisurely pace,
    /// staying ~1.75m back and mooing periodically. Uses the same state machine pause/resume
    /// pattern the game uses during petting interactions.
    ///
    /// Three states:
    /// - Dormant: checks conditions every 0.5s (trough empty + player nearby + player in area)
    /// - Following: pauses state machine, drives NavMesh toward player, moos every ~8s
    /// - Stops following when trough gets food, player leaves area, or player is too far
    /// </summary>
    public class CowFollowBehavior : MonoBehaviour, IMovementRequestCallback
    {
        // Tuning
        private const float DetectionRange = 15f;
        private const float FollowDistance = 1.75f;
        private const float GiveUpRange = 20f;
        private const float FollowSpeed = 2f;
        private const float MooInterval = 8f;
        private const float CheckInterval = 0.5f;

        // References
        private AnimalHub _hub;
        private AnimalSfxAnimationEvent _sfxEvent;
        private Transform _playerTransform;

        // State
        private bool _isFollowing;
        private float _checkTimer;
        private float _mooTimer;
        private float _randomOffset;
        private float _lateralSign;

        void Start()
        {
            _hub = GetComponent<AnimalHub>();
            _sfxEvent = GetComponentInChildren<AnimalSfxAnimationEvent>();
            _randomOffset = Random.Range(0f, 0.3f);
            _lateralSign = Random.value > 0.5f ? 1f : -1f;

            if (_hub == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowFollowBehavior: No AnimalHub found, disabling.");
                enabled = false;
            }
        }

        void Update()
        {
            if (_hub == null) return;

            if (_isFollowing)
                TickFollowing();
            else
                TickDormant();
        }

        private void TickDormant()
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = CheckInterval;

            if (ShouldFollow())
                EnterFollowing();
        }

        private void TickFollowing()
        {
            // Check exit conditions every frame
            if (!ShouldFollow())
            {
                ExitFollowing();
                return;
            }

            // Moo periodically
            _mooTimer -= Time.deltaTime;
            if (_mooTimer <= 0f)
            {
                _sfxEvent?.PlayVocalization();
                _mooTimer = MooInterval + Random.Range(-1f, 1f);
            }

            // Re-path toward player
            FollowPlayer();
        }

        private bool ShouldFollow()
        {
            // Trough must be empty
            var trough = CowTroughManager.Instance.GetInventory();
            if (trough != null && trough.Items.Count > 0)
                return false;

            // Need a player reference
            if (_playerTransform == null)
            {
                if (!Singleton<PlayerManager>.Exists) return false;
                var player = Singleton<PlayerManager>.Instance.ActivePlayer;
                if (player == null) return false;
                _playerTransform = player.Transform;
            }
            if (_playerTransform == null) return false;

            float distance = Vector3.Distance(transform.position, _playerTransform.position);

            // Player must be within detection range (or give-up range if already following)
            float maxRange = _isFollowing ? GiveUpRange : DetectionRange;
            if (distance > maxRange)
                return false;

            return true;
        }

        private void EnterFollowing()
        {
            _isFollowing = true;
            _mooTimer = Random.Range(1f, 3f); // first moo comes quickly

            // Pause the cow's normal AI
            if (_hub.StateMachine != null)
            {
                _hub.StateMachine.Exit();
                _hub.StateMachine.enabled = false;
            }

            _hub.Movement.CancelActiveMovementRequest();
            _hub.AnimalAnimator.ResetBlendState();
            _hub.AnimalAnimator.CanFidget = true;

            // Initial moo to signal start of follow
            _sfxEvent?.PlayVocalization();

            FollowPlayer();

            CottonCowModPlugin.Log.LogInfo("CowFollowBehavior: Started following player.");
        }

        private void ExitFollowing()
        {
            _isFollowing = false;
            _checkTimer = CheckInterval;

            _hub.Movement.CancelActiveMovementRequest();
            _hub.AnimalAnimator.CanFidget = true;

            // Resume the cow's normal AI
            if (_hub.StateMachine != null)
            {
                _hub.StateMachine.enabled = true;
                _hub.StateMachine.Enter();
            }

            CottonCowModPlugin.Log.LogInfo("CowFollowBehavior: Stopped following player.");
        }

        private void FollowPlayer()
        {
            if (_playerTransform == null || _hub.Movement == null) return;

            Vector3 cowPos = transform.position;
            Vector3 toPlayer = _playerTransform.position - cowPos;
            float distance = toPlayer.magnitude;

            // Already close enough — don't move
            if (distance <= FollowDistance + _randomOffset)
                return;

            // Calculate target position: stop FollowDistance away from player,
            // offset laterally so two cows don't walk side by side
            Vector3 forward = toPlayer.normalized;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward) * _lateralSign * 1.2f;
            Vector3 targetPos = cowPos + forward *
                (distance - FollowDistance - _randomOffset) + lateral;

            // Clamp to roam area
            var areaBound = _hub.AreaBoundLocationObject;
            if (areaBound != null)
            {
                if (!areaBound.TrySamplePositionInArea(targetPos, out targetPos))
                    return;
            }

            // Issue NavMesh pathfind request
            if (!_hub.Movement.NavMeshAgent.pathPending)
            {
                var args = new CharacterMovement.MovementRequestArgs(
                    new Location(targetPos, Quaternion.identity),
                    FollowSpeed,
                    applyDestinationRotation: false,
                    CharacterMovement.MovementRequestArgs.DestinationMode.SnapToNavMesh,
                    fastPathingFinding: true
                );
                _hub.Movement.PathfindToDestination(this, args);
            }
        }

        // --- IMovementRequestCallback ---

        public void StartedMovement(IPathfindingCharacterMovement sender, IMovementRequest request) { }

        public void CompletedMovement(IPathfindingCharacterMovement sender, IMovementRequest request) { }

        public void CancelledMovement(IPathfindingCharacterMovement sender, IMovementRequest request) { }

        public void InvalidMovement(IPathfindingCharacterMovement sender, IMovementRequest request) { }

        public void PausedMovement(IPathfindingCharacterMovement sender, IMovementRequest request) { }

        public void ResumedMovement(IPathfindingCharacterMovement sender, IMovementRequest request) { }

        // --- Cleanup ---

        void OnDisable()
        {
            if (_isFollowing)
                ExitFollowing();
        }

        void OnDestroy()
        {
            if (_isFollowing)
                ExitFollowing();
        }
    }
}
