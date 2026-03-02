using System;
using System.Collections.Generic;
using HarmonyLib;
using Lab42.Platforms;
using TotS.Character;
using TotS.World;
using UnityEngine;

namespace CottonCowMod
{
    /// <summary>
    /// Handles finding the cow NPCRace and spawning/despawning cows from the NPC pool.
    /// Cows are spawned in Garden Area 5 and tagged with PlayerOwnedCow.
    ///
    /// Spawning is deferred until Garden Area 5 actually loads in the streaming world,
    /// since it may not be available during early initialization.
    ///
    /// Creates a roaming boundary (BoxCollider trigger + MapLocationArea) so cows
    /// stay near their spawn point. The boundary size is configurable via CowRoamRadius.
    /// </summary>
    public static class CowSpawner
    {
        private static NPCRace _cowRace;
        private static Vector3 _gardenArea5Position;
        private static bool _gardenArea5Found;

        // Track spawned cows (one per unlock)
        private static GameObject _cow1GO;
        private static GameObject _cow2GO;
        private static NPCInstanceInfo _cow1Info;
        private static NPCInstanceInfo _cow2Info;

        // Deferred spawn requests (set when cows should spawn but garden isn't loaded)
        private static bool _cow1PendingSpawn;
        private static bool _cow2PendingSpawn;
        private static bool _cow1DeferralLogged;
        private static bool _cow2DeferralLogged;

        // Roaming boundary
        private static GameObject _boundaryGO;
        private static MapLocationArea _boundaryArea;

        public static bool Cow1Spawned => _cow1GO != null;
        public static bool Cow2Spawned => _cow2GO != null;

        public static GameObject GetCow1GO() => _cow1GO;
        public static GameObject GetCow2GO() => _cow2GO;

        /// <summary>
        /// Called from GardenAreaPatch when Garden Area 5 loads in the streaming world.
        /// Sets the spawn position and processes any deferred spawn requests.
        /// </summary>
        public static void OnGardenArea5Loaded(Vector3 position)
        {
            _gardenArea5Position = position;
            _gardenArea5Found = true;
            CottonCowModPlugin.Log.LogInfo(
                $"CowSpawner: Garden Area 5 position set to {position}");

            // Create the roaming boundary at the spawn position
            CreateRoamingBoundary(position);

            // Process deferred spawns
            if (_cow1PendingSpawn && !Cow1Spawned)
            {
                CottonCowModPlugin.Log.LogInfo("CowSpawner: Processing deferred Cow1 spawn.");
                SpawnCow("Farmer_Cotton", true);
                _cow1PendingSpawn = false;
            }
            if (_cow2PendingSpawn && !Cow2Spawned)
            {
                CottonCowModPlugin.Log.LogInfo("CowSpawner: Processing deferred Cow2 spawn.");
                SpawnCow("Young_Tom_Cotton", false);
                _cow2PendingSpawn = false;
            }
        }

        /// <summary>
        /// Creates a trigger BoxCollider + MapLocationArea at the spawn position.
        /// This defines the roaming area for cows. The AnimalWanderState checks
        /// AreaBoundLocationObject.TrySamplePositionInArea() which delegates to
        /// MapLocationArea.SamplePosition() → ClosestPoint on the collider.
        /// </summary>
        private static void CreateRoamingBoundary(Vector3 center)
        {
            if (_boundaryGO != null)
                return;

            float radius = CottonCowModPlugin.CowRoamRadius != null
                ? CottonCowModPlugin.CowRoamRadius.Value
                : 12f;

            try
            {
                // Create the boundary GO disabled so we can set up components before OnEnable fires
                _boundaryGO = new GameObject("CowPastureBoundary");
                _boundaryGO.SetActive(false);
                _boundaryGO.transform.position = center;

                // Box collider defines the roaming area (trigger = walkthrough, no physics blocking)
                var box = _boundaryGO.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(radius * 2f, 20f, radius * 2f);
                box.center = Vector3.zero;

                // MapLocationArea holds the collider list that AnimalWanderState queries
                _boundaryArea = _boundaryGO.AddComponent<MapLocationArea>();

                // Pre-initialize the colliders list so it's ready before OnEnable
                Traverse.Create(_boundaryArea).Field("m_Colliders")
                    .SetValue(new List<MapLocationAreaCollider>());

                // MapLocationAreaCollider wraps the BoxCollider for the area system
                var areaCol = _boundaryGO.AddComponent<MapLocationAreaCollider>();
                Traverse.Create(areaCol).Field("m_Collider").SetValue(box);
                Traverse.Create(areaCol).Field("m_MapLocationArea").SetValue(_boundaryArea);

                // Enable the GO — BoxCollider becomes active, OnEnable fires
                // MapLocationArea.OnEnable tries to register with MapLocationManager (may warn with null location, that's OK)
                // MapLocationAreaCollider.OnEnable calls _boundaryArea.AddAreaCollider(this)
                _boundaryGO.SetActive(true);

                // Ensure the collider is registered (in case OnEnable order didn't auto-register)
                var colliders = Traverse.Create(_boundaryArea).Field("m_Colliders")
                    .GetValue<List<MapLocationAreaCollider>>();
                if (colliders != null && !colliders.Contains(areaCol))
                {
                    colliders.Add(areaCol);
                }

                CottonCowModPlugin.Log.LogInfo(
                    $"CowSpawner: Created roaming boundary at {center}, " +
                    $"size={radius * 2f}x{radius * 2f}m, " +
                    $"colliders registered: {colliders?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"CowSpawner: CreateRoamingBoundary exception: {ex}");
            }
        }

        /// <summary>
        /// After spawning a cow, wires its AreaBoundLocationObject to our custom boundary.
        /// This constrains the cow's AnimalWanderState to stay within the boundary box.
        /// </summary>
        private static void AssignRoamingBoundary(GameObject cowGO)
        {
            if (_boundaryArea == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    "CowSpawner: No roaming boundary available to assign.");
                return;
            }

            var areaBound = cowGO.GetComponent<AreaBoundLocationObject>();
            if (areaBound == null)
            {
                CottonCowModPlugin.Log.LogWarning(
                    $"CowSpawner: No AreaBoundLocationObject on {cowGO.name}, cow may roam freely.");
                return;
            }

            // Set m_MapLocationArea directly, bypassing the WorldSystemIdentifier lookup
            Traverse.Create(areaBound).Field("m_MapLocationArea").SetValue(_boundaryArea);

            CottonCowModPlugin.Log.LogInfo(
                $"CowSpawner: Assigned roaming boundary to {cowGO.name}.");
        }

        /// <summary>
        /// Finds the cow NPCRace by searching loaded ScriptableObject assets.
        /// </summary>
        public static bool FindCowRace()
        {
            if (_cowRace != null)
                return true;

            var allRaces = Resources.FindObjectsOfTypeAll<NPCRace>();
            foreach (var race in allRaces)
            {
                if (race.IsAnimal && race.name == "Cow")
                {
                    _cowRace = race;
                    break;
                }
            }

            if (_cowRace != null)
            {
                CottonCowModPlugin.Log.LogInfo($"CowSpawner: Using cow race: {_cowRace.name}");
                return true;
            }

            CottonCowModPlugin.Log.LogWarning("CowSpawner: Could not find cow NPCRace!");
            return false;
        }

        /// <summary>
        /// Spawns a cow at Garden Area 5 from the NPC pool.
        /// If Garden Area 5 isn't loaded yet, defers the spawn.
        /// Returns the spawned GameObject, or null on failure/deferral.
        /// </summary>
        public static GameObject SpawnCow(string ownerNPC, bool isCow1)
        {
            // Check if already spawned
            if (isCow1 && _cow1GO != null) return _cow1GO;
            if (!isCow1 && _cow2GO != null) return _cow2GO;

            if (_cowRace == null && !FindCowRace())
                return null;

            // If garden area isn't loaded, defer the spawn (log once per cow)
            if (!_gardenArea5Found)
            {
                if (isCow1)
                {
                    _cow1PendingSpawn = true;
                    if (!_cow1DeferralLogged)
                    {
                        CottonCowModPlugin.Log.LogInfo("CowSpawner: Garden Area 5 not loaded yet, deferring Cow1 spawn.");
                        _cow1DeferralLogged = true;
                    }
                }
                else
                {
                    _cow2PendingSpawn = true;
                    if (!_cow2DeferralLogged)
                    {
                        CottonCowModPlugin.Log.LogInfo("CowSpawner: Garden Area 5 not loaded yet, deferring Cow2 spawn.");
                        _cow2DeferralLogged = true;
                    }
                }
                return null;
            }

            try
            {
                // Offset the second cow's position slightly so they don't overlap
                Vector3 spawnPos = _gardenArea5Position;
                if (!isCow1)
                    spawnPos += new Vector3(2f, 0f, 2f);

                var location = new Location(spawnPos, Quaternion.identity);

                // Request a cow from the NPC pool
                var request = new NPCSpawnRequest(1, 10, _cowRace);
                try
                {
                    Singleton<PlatformManager>.Instance.CurrentPlatform
                        .SearchForCandidateNPCs(ref request);

                    if (!request.IsFulfilled())
                    {
                        CottonCowModPlugin.Log.LogWarning(
                            "CowSpawner: No cow instances available in the NPC pool.");
                        return null;
                    }

                    NPCInstanceInfo npcInfo;
                    var cowGO = request.SpawnCandidateNPCAtIndexAtLocation(location, out npcInfo);

                    if (cowGO == null)
                    {
                        CottonCowModPlugin.Log.LogWarning(
                            "CowSpawner: SpawnCandidate returned null.");
                        return null;
                    }

                    // Add our tag component
                    var tag = cowGO.AddComponent<PlayerOwnedCow>();
                    tag.OwnerCottonNPC = ownerNPC;
                    tag.NpcInstanceInfo = npcInfo;

                    // Add milk indicator
                    var milkIndicator = cowGO.AddComponent<CowMilkIndicator>();

                    // Restore persisted milk state from config
                    bool savedHasMilk = isCow1
                        ? CottonCowModPlugin.Cow1HasMilk.Value
                        : CottonCowModPlugin.Cow2HasMilk.Value;
                    int savedMilkQuality = isCow1
                        ? CottonCowModPlugin.Cow1MilkQuality.Value
                        : CottonCowModPlugin.Cow2MilkQuality.Value;

                    if (savedHasMilk)
                    {
                        tag.HasMilk = true;
                        tag.MilkQuality = savedMilkQuality;
                        // Indicator's Start() hasn't run yet, so we defer the visual update
                        cowGO.AddComponent<DeferredMilkIndicatorActivator>();
                        CottonCowModPlugin.Log.LogInfo(
                            $"CowSpawner: Restored milk state for {(isCow1 ? "Cow1" : "Cow2")}: " +
                            $"Q{savedMilkQuality}");
                    }

                    // Add hungry follow behavior
                    cowGO.AddComponent<CowFollowBehavior>();

                    // Constrain roaming to our boundary
                    AssignRoamingBoundary(cowGO);

                    // Track it
                    if (isCow1)
                    {
                        _cow1GO = cowGO;
                        _cow1Info = npcInfo;
                    }
                    else
                    {
                        _cow2GO = cowGO;
                        _cow2Info = npcInfo;
                    }

                    CottonCowModPlugin.Log.LogInfo(
                        $"CowSpawner: Spawned {(isCow1 ? "Cow1" : "Cow2")} for {ownerNPC} " +
                        $"at {spawnPos}. GameObject: {cowGO.name}");

                    return cowGO;
                }
                finally
                {
                    request.Dispose();
                }
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"CowSpawner: SpawnCow exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Returns a spawned cow to the NPC pool.
        /// </summary>
        public static void DespawnCow(bool isCow1)
        {
            try
            {
                var info = isCow1 ? _cow1Info : _cow2Info;

                if (info != null)
                {
                    Singleton<PlatformManager>.Instance.CurrentPlatform
                        .ReturnTertiaryNPCInstanceToPool(info);
                    CottonCowModPlugin.Log.LogInfo(
                        $"CowSpawner: Despawned {(isCow1 ? "Cow1" : "Cow2")}.");
                }

                if (isCow1)
                {
                    _cow1GO = null;
                    _cow1Info = null;
                    _cow1PendingSpawn = false;
                }
                else
                {
                    _cow2GO = null;
                    _cow2Info = null;
                    _cow2PendingSpawn = false;
                }
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"CowSpawner: DespawnCow exception: {ex}");
            }
        }

        /// <summary>
        /// Reset state (called on scene reload / cleanup).
        /// </summary>
        public static void Reset()
        {
            _cow1GO = null;
            _cow2GO = null;
            _cow1Info = null;
            _cow2Info = null;
            _cow1PendingSpawn = false;
            _cow2PendingSpawn = false;
            _cow1DeferralLogged = false;
            _cow2DeferralLogged = false;
            _gardenArea5Found = false;
            _cowRace = null;

            if (_boundaryGO != null)
            {
                UnityEngine.Object.Destroy(_boundaryGO);
                _boundaryGO = null;
            }
            _boundaryArea = null;
        }
    }
}
