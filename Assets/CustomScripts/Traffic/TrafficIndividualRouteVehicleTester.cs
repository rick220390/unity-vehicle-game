using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CustomTraffic.UTS;

namespace CustomTraffic
{
    /// <summary>
    /// Spawns test vehicles, giving every vehicle its own independently
    /// generated UTS route and destination. It maintains a configurable local
    /// traffic population while keeping every vehicle on its own private UTS path.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficIndividualRouteVehicleTester : MonoBehaviour
    {
        [Header("Traffic System")]
        [SerializeField] private TrafficSpawnPositionSelector spawnSelector;
        [SerializeField] private TrafficRoutePlanner routePlanner;
        [SerializeField] private TrafficLaneSpline[] destinationLanes;

        [Header("Vehicle Prefabs")]
        [SerializeField] private GameObject[] vehiclePrefabs;

        [Header("UTS Driving")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 8f;
        [SerializeField, Min(0.1f)] private float acceleration = 2f;
        [SerializeField, Min(0.1f)] private float deceleration = 2f;
        [SerializeField, Min(1f)] private float followingDistance = 8f;
        [SerializeField, Min(0.1f)] private float waypointThreshold = 1.3f;

        [Header("Continuous Traffic")]
        [SerializeField] private bool automaticallyMaintainTraffic = true;
        [SerializeField, Min(0.5f)] private float spawnInterval = 4f;
        [SerializeField, Min(1)] private int maximumActiveVehicles = 6;
        [SerializeField, Min(1)] private int spawnPositionAttempts = 8;
        [SerializeField, Min(1)] private int destinationAttemptsPerPosition = 20;
        [SerializeField, Min(1f)] private float minimumVehicleSpawnSpacing = 25f;

        private readonly List<GameObject> activeTestVehicles = new List<GameObject>();
        private bool spawnInProgress;
        private Coroutine trafficLoop;

        private void Start()
        {
            if (automaticallyMaintainTraffic)
                trafficLoop = StartCoroutine(MaintainTraffic());
        }

        private IEnumerator MaintainTraffic()
        {
            while (true)
            {
                RemoveDestroyedVehicles();
                if (!spawnInProgress && activeTestVehicles.Count < maximumActiveVehicles)
                    StartCoroutine(CreateAndSpawnVehicle());
                yield return new WaitForSeconds(spawnInterval);
            }
        }

        private void RemoveDestroyedVehicles()
        {
            for (int i = activeTestVehicles.Count - 1; i >= 0; i--)
                if (activeTestVehicles[i] == null)
                    activeTestVehicles.RemoveAt(i);
        }

        [ContextMenu("Spawn One Vehicle With Its Own Random Route")]
        public void SpawnOneVehicleWithOwnRoute()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Individual Traffic Test] Enter Play mode before spawning.", this);
                return;
            }

            if (spawnInProgress)
            {
                Debug.LogWarning("[Individual Traffic Test] A vehicle route is already being prepared.", this);
                return;
            }

            RemoveDestroyedVehicles();
            if (activeTestVehicles.Count >= maximumActiveVehicles)
            {
                Debug.LogWarning("[Individual Traffic Test] Maximum active traffic count reached.", this);
                return;
            }

            StartCoroutine(CreateAndSpawnVehicle());
        }

        private IEnumerator CreateAndSpawnVehicle()
        {
            spawnInProgress = true;

            if (spawnSelector == null || routePlanner == null ||
                destinationLanes == null || destinationLanes.Length == 0 ||
                vehiclePrefabs == null || vehiclePrefabs.Length == 0)
            {
                Debug.LogError("[Individual Traffic Test] Assign the spawn selector, route planner, destination lanes and at least one UTS vehicle prefab.", this);
                spawnInProgress = false;
                yield break;
            }

            List<GameObject> validPrefabs = new List<GameObject>();
            foreach (GameObject prefab in vehiclePrefabs)
                if (prefab != null) validPrefabs.Add(prefab);

            if (validPrefabs.Count == 0)
            {
                Debug.LogError("[Individual Traffic Test] No valid vehicle prefabs are assigned.", this);
                spawnInProgress = false;
                yield break;
            }

            TrafficLaneSpline startLane = null;
            TrafficLaneSpline destination = null;
            float startT = 0f;
            Vector3 spawnPosition = Vector3.zero;
            Quaternion spawnRotation = Quaternion.identity;
            TrafficRoutePlanner.RoutePlan plan = null;
            List<Vector3> routePoints = null;
            string lastReason = "No valid start/destination combination was found.";
            bool routeFound = false;

            List<TrafficLaneSpline> destinations = new List<TrafficLaneSpline>();
            foreach (TrafficLaneSpline lane in destinationLanes)
                if (lane != null && lane.isActiveAndEnabled)
                    destinations.Add(lane);

            if (destinations.Count == 0)
            {
                Debug.LogWarning("[Individual Traffic Test] No active destination lanes are assigned.", this);
                spawnInProgress = false;
                yield break;
            }

            // A random destination may be unreachable from a particular point.
            // Try several destinations, then choose a new hidden start position.
            for (int positionAttempt = 0; positionAttempt < spawnPositionAttempts && !routeFound; positionAttempt++)
            {
                if (!spawnSelector.TryFindSpawnPosition(out startLane, out startT,
                    out spawnPosition, out spawnRotation))
                {
                    lastReason = "No hidden spawn position was found near the player.";
                    continue;
                }

                bool tooCloseToExistingVehicle = false;
                for (int vehicleIndex = 0; vehicleIndex < activeTestVehicles.Count; vehicleIndex++)
                {
                    GameObject existingVehicle = activeTestVehicles[vehicleIndex];
                    if (existingVehicle != null &&
                        Vector3.Distance(existingVehicle.transform.position, spawnPosition) < minimumVehicleSpawnSpacing)
                    {
                        tooCloseToExistingVehicle = true;
                        break;
                    }
                }

                if (tooCloseToExistingVehicle)
                {
                    lastReason = "Candidate spawn position was too close to existing traffic.";
                    continue;
                }

                // Test each possible destination at most once for this start lane.
                // Randomise the order, but never repeatedly pick the same unreachable
                // opposite-direction lane pair.
                List<TrafficLaneSpline> shuffledDestinations =
                    new List<TrafficLaneSpline>();

                foreach (TrafficLaneSpline lane in destinations)
                    if (lane != startLane)
                        shuffledDestinations.Add(lane);

                for (int i = shuffledDestinations.Count - 1; i > 0; i--)
                {
                    int swapIndex = Random.Range(0, i + 1);
                    TrafficLaneSpline temporary = shuffledDestinations[i];
                    shuffledDestinations[i] = shuffledDestinations[swapIndex];
                    shuffledDestinations[swapIndex] = temporary;
                }

                int attempts = Mathf.Min(destinationAttemptsPerPosition, shuffledDestinations.Count);
                for (int destinationAttempt = 0;
                    destinationAttempt < attempts && !routeFound;
                    destinationAttempt++)
                {
                    TrafficLaneSpline candidate = shuffledDestinations[destinationAttempt];

                    if (!routePlanner.TryBuildRoute(startLane, startT, candidate,
                        out TrafficRoutePlanner.RoutePlan candidatePlan, out string routeReason))
                    {
                        // This lane is not reachable from this start position.
                        // Silently skip it and test the next unique destination.
                        lastReason = routeReason;
                        continue;
                    }

                    if (!routePlanner.TryBuildRoutePoints(candidatePlan,
                        out List<Vector3> candidatePoints, out string geometryReason) ||
                        candidatePoints == null || candidatePoints.Count < 3)
                    {
                        lastReason = "Route geometry invalid: " + geometryReason;
                        continue;
                    }

                    destination = candidate;
                    plan = candidatePlan;
                    routePoints = candidatePoints;
                    routeFound = true;
                }
            }

            if (!routeFound)
            {
                Debug.LogWarning("[Individual Traffic Test] Could not find a reachable route after " +
                    spawnPositionAttempts + " spawn positions. Last reason: " + lastReason, this);
                spawnInProgress = false;
                yield break;
            }

            GameObject routeObject = new GameObject("Runtime UTS Route - " + startLane.LaneDisplayName + " to " + destination.LaneDisplayName);
            routeObject.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            routeObject.SetActive(false);

            TrafficGeneratedWalkPath path = routeObject.AddComponent<TrafficGeneratedWalkPath>();
            path.ConfigureRoute(routePlanner, startLane, startT, destination);
            routeObject.SetActive(true);

            // WalkPath.Awake creates its sampled point array and UTS SpawnPoint.
            yield return null;

            if (path == null || !path.HasValidPath || path.SpawnPoints == null || path.SpawnPoints.Length == 0)
            {
                Debug.LogError("[Individual Traffic Test] The private UTS path failed to initialise. No vehicle was spawned.", this);
                if (routeObject != null) Destroy(routeObject);
                spawnInProgress = false;
                yield break;
            }

            int lastPoint = path.getPointsTotal(0) - 2;
            if (lastPoint < 2)
            {
                Debug.LogError("[Individual Traffic Test] The generated path is too short for a vehicle.", path);
                DestroyRouteAndSpawnPoints(path, routeObject);
                spawnInProgress = false;
                yield break;
            }

            Vector3 start = path.getNextPoint(0, 1);
            Vector3 next = path.getNextPoint(0, 2);
            if (Vector3.Distance(start, next) < 0.01f)
            {
                Debug.LogError("[Individual Traffic Test] The first route segment has no usable direction.", path);
                DestroyRouteAndSpawnPoints(path, routeObject);
                spawnInProgress = false;
                yield break;
            }

            GameObject selectedPrefab = validPrefabs[Random.Range(0, validPrefabs.Count)];
            GameObject vehicle = Instantiate(selectedPrefab, start, Quaternion.LookRotation(next - start, Vector3.up));
            vehicle.name = selectedPrefab.name + " - Individual Route Test";

            MovePath movePath = vehicle.AddComponent<MovePath>();
            movePath.walkPath = path;
            movePath._walkPointThreshold = waypointThreshold;
            movePath.InitStartPosition(0, 1, false, true);
            movePath.nextFinishPos = lastPoint >= 3 ? path.getNextPoint(0, 3) : Vector3.zero;

            CarAIController ai = vehicle.AddComponent<CarAIController>();
            ai.MOVE_SPEED = moveSpeed;
            ai.INCREASE = acceleration;
            ai.DECREASE = deceleration;
            ai.TO_CAR = followingDistance;
            ai.TO_SEMAPHORE = followingDistance;
            ai.MaxAngle = 8f;

            WheelCollider[] wheels = vehicle.GetComponentsInChildren<WheelCollider>(true);
            BoxCollider[] boxes = vehicle.GetComponentsInChildren<BoxCollider>(true);
            int expectedBoxIndex = wheels.Length > 2 ? 0 : 1;

            if (boxes.Length <= expectedBoxIndex)
            {
                Debug.LogError("[Individual Traffic Test] This prefab does not have the BoxCollider layout expected by UTS CarAIController.", vehicle);
                Destroy(vehicle);
                DestroyRouteAndSpawnPoints(path, routeObject);
                spawnInProgress = false;
                yield break;
            }

            ai.bc = boxes[expectedBoxIndex];
            ai.bcSize = ai.bc.bounds.size;
            movePath.SetLookPosition();

            path.SpawnRequested += (wayIndex, forward) => HandleRouteFinished(path, routeObject, wayIndex, forward);
            activeTestVehicles.Add(vehicle);

            Debug.Log("[Individual Traffic Test] Spawned '" + vehicle.name +
                "' | Start: " + startLane.LaneDisplayName + " at T=" + startT.ToString("F3") +
                " | Unique destination: " + destination.LaneDisplayName +
                " | Route distance: " + plan.TotalDistance.ToString("F1") +
                "m | Waypoints: " + routePoints.Count +
                ". This vehicle owns this private route.", vehicle);

            spawnInProgress = false;
        }

        private void HandleRouteFinished(
            TrafficGeneratedWalkPath path, GameObject routeObject, int wayIndex, bool forward)
        {
            if (wayIndex != 0 || !forward)
                return;

            StartCoroutine(CleanupCompletedRoute(path, routeObject));
        }

        private IEnumerator CleanupCompletedRoute(
            TrafficGeneratedWalkPath path, GameObject routeObject)
        {
            // UTS destroys the vehicle after requesting the route-end spawn.
            yield return new WaitForSeconds(1f);
            DestroyRouteAndSpawnPoints(path, routeObject);
            RemoveDestroyedVehicles();
        }

        private void DestroyRouteAndSpawnPoints(TrafficGeneratedWalkPath path, GameObject routeObject)
        {
            if (path != null && path.SpawnPoints != null)
            {
                foreach (SpawnPoint spawnPoint in path.SpawnPoints)
                    if (spawnPoint != null) Destroy(spawnPoint.gameObject);
            }

            if (routeObject != null)
                Destroy(routeObject);
        }
    }
}