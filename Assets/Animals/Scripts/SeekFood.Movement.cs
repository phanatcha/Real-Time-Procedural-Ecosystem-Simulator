using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Boreal.Animals
{
    // Part of SeekFood (see SeekFood.cs). Getting about: stamina, speed, wandering, fleeing, escaping the island's edge, and setting routes.
    public partial class SeekFood
    {
        [Header("Stamina")]
        [Min(0.01f)] public float maxStamina = 100f;
        [Min(0f)] public float currentStamina;
        [Min(0f)] public float staminaRecoveryPerSecond = 8f;
        [Min(0f)] public float attackStaminaCost = 10f;
        [Min(0f)] public float fleeStaminaCostPerSecond = 15f;
        [Min(1f)] public float fleeSpeedMultiplier = 1.35f;
        [Range(0.1f, 1f)] public float exhaustedSpeedMultiplier = 0.65f;
        public float StaminaFraction => currentStamina / Mathf.Max(0.01f, maxStamina);
        // Speed from the genes alone, before body parts adjust it. Speed in water is based on it, since legs and
        // fins work differently there.
        private float geneMoveSpeed;

        // Follows prey or a partner, refreshing the route as the target moves, no more often than the refresh
        // interval allows.
        void UpdatePathToMovingTarget(SeekFood target)
        {
            Vector3 targetPosition = target.transform.position;
            float movementThreshold = Mathf.Max(0f, preyPathTargetMovementThreshold);
            bool targetChanged = lastPreyPathTarget != target;
            bool targetMoved = !hasPreyPathDestination ||
                               (targetPosition - lastPreyPathDestination).sqrMagnitude >=
                               movementThreshold * movementThreshold;
            bool pathMissing = !agent.pathPending && !agent.hasPath;

            if (preyPathRefreshTimer <= 0f &&
                (targetChanged || targetMoved || pathMissing))
            {
                if (TrySetPreyDestination(targetPosition))
                {
                    lastPreyPathTarget = target;
                    lastPreyPathDestination = targetPosition;
                    hasPreyPathDestination = true;
                }
                preyPathRefreshTimer = CalculatePreyPathRefreshInterval();
            }
        }

        void UpdateStamina(float deltaTime)
        {
            if (currentState == State.Fleeing)
            {
                currentStamina = Mathf.Max(0f, currentStamina - fleeStaminaCostPerSecond * deltaTime);
            }
            else
            {
                currentStamina = Mathf.Min(maxStamina,
                                           currentStamina + staminaRecoveryPerSecond * deltaTime *
                                           (thermalResponse != null ? thermalResponse.StaminaRecoveryMultiplier : 1f) *
                                           (1f - Mathf.Clamp01(smokeFelt)));
            }
        }

        void RefreshMovementSpeed()
        {
            if (agent == null)
            {
                return;
            }

            float multiplier = 1f;
            if (currentState == State.Fleeing)
            {
                multiplier = currentStamina > 0f ? fleeSpeedMultiplier : exhaustedSpeedMultiplier;
            }

            float temperatureMultiplier = thermalResponse != null ? thermalResponse.MovementMultiplier : 1f;
            float baseSpeed = inWater
                ? (geneMoveSpeed > 0f ? geneMoveSpeed : moveSpeed) * waterMovement.SpeedMultiplier(bodyEffects.swimmingAbility)
                : moveSpeed * snowSpeedMultiplier;
            float agedMultiplier = Mathf.Lerp(Mathf.Clamp01(agedSpeedFloor), 1f, Vigour);
            agent.speed = Mathf.Max(0.01f, baseSpeed * growth * multiplier * temperatureMultiplier * agedMultiplier);
        }

        public float GetCurrentMovementSpeed()
        {
            return agent == null ? Mathf.Max(0.01f, moveSpeed) : Mathf.Max(0.01f, agent.speed);
        }

        void UpdateFleeing(float deltaTime)
        {
            timer += deltaTime;
            if (timer >= fleeDuration)
            {
                currentFleeThreat = null;
                fleeingFire = false;
                currentFleeDirection = Vector3.zero;
                currentState = State.Idling;
                timer = actionTimer;
                decisionTimer = 0f;
                return;
            }

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                SetFleeDestination(currentFleeDirection, currentFleeThreat);
            }
        }

        void SetFleeDestination(Vector3 direction, SeekFood fallbackThreat)
        {
            Vector3 away = direction;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f && IsOtherLivingAnimal(fallbackThreat))
            {
                away = transform.position - fallbackThreat.transform.position;
                away.y = 0f;
            }

            if (away.sqrMagnitude < 0.01f)
            {
                Vector2 randomDirection = Random.insideUnitCircle.normalized;
                away = new Vector3(randomDirection.x, 0f, randomDirection.y);
            }

            Vector3 candidate = transform.position + away.normalized * fleeDistance;
            ClampDestinationCandidate(ref candidate);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 25f, DestinationAreas))
            {
                TrySetDestination(hit.position);
            }
        }

        void UpdateEscaping(float deltaTime)
        {
            timer += deltaTime;
            if (timer >= actionTimer)
            {
                currentState = State.Idling;
                timer = actionTimer;
                decisionTimer = 0f;
            }
        }

        void UpdateIdleAndWander(float deltaTime)
        {
            if (currentState == State.Idling)
            {
                timer += deltaTime;
                if (timer >= actionTimer)
                {
                    PickNextAction();
                    timer = 0f;
                }
                return;
            }

            if (currentState == State.Wandering && !agent.pathPending &&
                (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.1f))
            {
                PickNextAction();
            }
        }

        // Where the animal may choose to wander or flee to: swimmers anywhere they can swim except dead zones, everyone
        // else only onto land, and out over rivers and lakes while they are frozen. Routes there can still cross water
        // when that is worth it.
        int DestinationAreas => CanSwimDeepWater && agent != null
            ? agent.areaMask & ~(1 << WaterAccess.DeadZoneArea)
            : WaterAccess.WalkableAreas;

        void ClampDestinationCandidate(ref Vector3 candidate)
        {
            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            if (!CanSwimDeepWater || terrainWorld == null || !WaterAccess.HasWater)
            {
                ClampCandidateToTerrain(ref candidate);
                return;
            }

            // A swimmer may head out over the water, so it only needs to stay in the habitat, at the height of the
            // ground or the water surface there.
            candidate = terrainWorld.ClampToHabitableBounds(candidate);
            if (terrainWorld.TryGetSample(candidate, out EnvironmentSample sample) && sample.isValid)
            {
                candidate.y = Mathf.Max(sample.position.y, WaterAccess.SurfaceHeight);
            }
        }

        void ClampCandidateToTerrain(ref Vector3 candidate)
        {
            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            if (terrainWorld != null)
            {
                candidate = terrainWorld.ClampToHabitableBounds(candidate);
                if (terrainWorld.TryFindWalkableGround(candidate, 30f, out Vector3 ground))
                {
                    candidate = ground;
                }
                return;
            }

            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                return;
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            const float borderInset = 6f;
            candidate.x = Mathf.Clamp(candidate.x, terrainPosition.x + borderInset, terrainPosition.x + terrainSize.x - borderInset);
            candidate.z = Mathf.Clamp(candidate.z, terrainPosition.z + borderInset, terrainPosition.z + terrainSize.z - borderInset);
            candidate.y = terrainPosition.y + terrain.SampleHeight(candidate);
        }

        bool IsOutsideTerrainBounds()
        {
            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            if (terrainWorld != null)
            {
                return !terrainWorld.IsInsideHabitableBounds(transform.position);
            }

            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                return false;
            }

            Vector3 position = transform.position;
            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            return position.x < terrainPosition.x || position.x > terrainPosition.x + terrainSize.x ||
                   position.z < terrainPosition.z || position.z > terrainPosition.z + terrainSize.z;
        }

        void BeginBoundaryEscape()
        {
            ClearTargets();
            currentState = State.Escaping;
            timer = 0f;

            AnimalTerrainWorld terrainWorld = AnimalTerrainWorld.Active;
            if (terrainWorld != null)
            {
                if (terrainWorld.TryFindWalkableGroundTowardCenter(
                    transform.position, Mathf.Max(50f, wanderRadius), out Vector3 destination) &&
                    NavMesh.SamplePosition(destination, out NavMeshHit terrainHit, 25f, NavMesh.AllAreas))
                {
                    TrySetDestination(terrainHit.position);
                }
                return;
            }

            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                return;
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            Vector3 center = new Vector3(terrainPosition.x + terrainSize.x / 2f,
                                         transform.position.y,
                                         terrainPosition.z + terrainSize.z / 2f);
            if (NavMesh.SamplePosition(center, out NavMeshHit hit, 25f, NavMesh.AllAreas))
            {
                TrySetDestination(hit.position);
            }
        }

        bool TrySetDestination(Vector3 destination)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return false;
            }

            agent.stoppingDistance = Mathf.Max(0f, referenceAgentStoppingDistance);
            destinationSetFrame = Time.frameCount;
            return agent.SetDestination(destination);
        }

        // The frame a route was last asked for. In that frame and the next the agent can report no path even when
        // SetDestination found one, so a chase judged then would be given up at once and the food or water shunned
        // for half a minute.
        int destinationSetFrame = -10;
        bool RouteJustSet => Time.frameCount - destinationSetFrame <= 1;

        bool TrySetPreyDestination(Vector3 destination)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return false;
            }

            float approachDistance = CurrentAttackRange *
                                     Mathf.Clamp(attackApproachRangeFraction, 0.1f, 0.95f);
            agent.stoppingDistance = Mathf.Max(referenceAgentStoppingDistance, approachDistance);
            return agent.SetDestination(destination);
        }

        void TryResetPath()
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.stoppingDistance = Mathf.Max(0f, referenceAgentStoppingDistance);
                agent.ResetPath();
            }
        }
    }
}
