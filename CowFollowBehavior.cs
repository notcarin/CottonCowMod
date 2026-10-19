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
    ///
    /// Also keeps the cow in the glade (the leash): if she's been more than LeashDistance
    /// outside it for LeashDelay seconds (pushed, or a path that cut through the garden),
    /// she walks back to the middle of the glade, or is placed there if she can't get back.
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
        private const float EdgeInset = 1.5f;
        private const float LeashDistance = 2f;
        private const float LeashDelay = 4f;
        private const float ReturnTimeout = 20f;

        // References
        private AnimalHub _hub;
        private AnimalSfxAnimationEvent _sfxEvent;
        private Transform _playerTransform;

        // State
        private bool _isFollowing;
        private bool _isReturning;
        private float _checkTimer;
        private float _mooTimer;
        private float _randomOffset;
        private float _lateralSign;
        private float _outsideTime;
        private float _returnStarted;

        public bool IsFollowing => _isFollowing;
        public bool IsReturning => _isReturning;

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

            if (_isReturning)
                TickReturning();
            else if (_isFollowing)
                TickFollowing();
            else
                TickDormant();
        }

        private void TickDormant()
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = CheckInterval;

            if (CheckLeash())
                return;

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

            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = CheckInterval;
                if (CheckLeash())
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

            PauseAI();

            // Initial moo to signal start of follow
            _sfxEvent?.PlayVocalization();

            FollowPlayer();

            CottonCowModPlugin.Log.LogInfo("CowFollowBehavior: Started following player.");
        }

        private void ExitFollowing()
        {
            _isFollowing = false;
            _checkTimer = CheckInterval;
            ResumeAI();
            CottonCowModPlugin.Log.LogInfo("CowFollowBehavior: Stopped following player.");
        }

        /// <summary>
        /// Pauses the cow's normal AI, the way the game does during petting.
        /// </summary>
        private void PauseAI()
        {
            if (_hub.StateMachine != null)
            {
                _hub.StateMachine.Exit();
                _hub.StateMachine.enabled = false;
            }

            _hub.Movement.CancelActiveMovementRequest();
            _hub.AnimalAnimator.ResetBlendState();
            _hub.AnimalAnimator.CanFidget = true;
        }

        private void ResumeAI()
        {
            _hub.Movement.CancelActiveMovementRequest();
            _hub.AnimalAnimator.CanFidget = true;

            if (_hub.StateMachine != null)
            {
                _hub.StateMachine.enabled = true;
                _hub.StateMachine.Enter();
            }
        }

        // --- Leash ---

        /// <summary>
        /// Tracks how long the cow has been outside the glade (called every CheckInterval)
        /// and starts walking her back once it's too long. Returns true if she's now returning.
        /// </summary>
        private bool CheckLeash()
        {
            if (!GladeArea.Built)
                return false;

            if (GladeArea.DistanceOutside(transform.position) > LeashDistance)
                _outsideTime += CheckInterval;
            else
                _outsideTime = 0f;

            if (_outsideTime < LeashDelay)
                return false;

            if (_isFollowing)
            {
                _isFollowing = false;
                CottonCowModPlugin.Log.LogInfo("CowFollowBehavior: Stopped following player (outside the glade).");
            }
            else
            {
                PauseAI();
            }
            EnterReturning();
            return true;
        }

        private void EnterReturning()
        {
            _isReturning = true;
            _outsideTime = 0f;
            _returnStarted = Time.time;
            CottonCowModPlugin.Log.LogInfo(
                $"CowFollowBehavior: {name} wandered out of the glade at {transform.position}, walking back.");

            var args = new CharacterMovement.MovementRequestArgs(
                new Location(GladeArea.Center, Quaternion.identity),
                FollowSpeed,
                applyDestinationRotation: false,
                CharacterMovement.MovementRequestArgs.DestinationMode.SnapToNavMesh,
                fastPathingFinding: true
            );
            _hub.Movement.PathfindToDestination(this, args);
        }

        private void TickReturning()
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = CheckInterval;

            // Back in, well clear of the edge: carry on as normal
            if (GladeArea.Contains(transform.position) &&
                Vector3.Distance(transform.position, GladeArea.Center) < 4f)
            {
                ExitReturning();
                return;
            }

            if (Time.time - _returnStarted > ReturnTimeout)
            {
                CottonCowModPlugin.Log.LogWarning(
                    $"CowFollowBehavior: {name} couldn't walk back into the glade, placing her there.");
                _hub.Movement.NavMeshAgent.Warp(GladeArea.Center);
                ExitReturning();
            }
        }

        private void ExitReturning()
        {
            _isReturning = false;
            _checkTimer = CheckInterval;
            ResumeAI();
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

            // Clamp to roam area, then pull the target in from the edge so snapping it to the
            // NavMesh can't put it just outside the glade
            var areaBound = _hub.AreaBoundLocationObject;
            if (areaBound != null)
            {
                if (!areaBound.TrySamplePositionInArea(targetPos, out targetPos))
                    return;
            }
            if (GladeArea.Built)
            {
                Vector3 toCenter = GladeArea.Center - targetPos;
                float step = Mathf.Min(EdgeInset, toCenter.magnitude);
                if (step > 0f)
                    targetPos += toCenter.normalized * step;
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
            else if (_isReturning)
                ExitReturning();
        }

        void OnDestroy()
        {
            if (_isFollowing)
                ExitFollowing();
            else if (_isReturning)
                ExitReturning();
        }
    }
}
