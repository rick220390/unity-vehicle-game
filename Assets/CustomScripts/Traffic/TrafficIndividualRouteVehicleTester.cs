using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CustomTraffic.UTS;

namespace CustomTraffic
{
    /// <summary>
    /// Spawns test vehicles, giving every vehicle its own independently
    /// generated UTS route and destination. This is deliberately a one-at-a-time
    /// integration tester; the production traffic manager will later control density.
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

        [Header("Test Behaviour")]
        [SerializeField] private bool allowOnlyOneActiveTestVehicle = true;

        private readonly List<GameObject> activeTestVehicles = new List<GameObject>();
        private readonly List<GameObject> activeRouteObjects = new List<GameObject>();
        private bool spawnInProgress;

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

            if (allowOnlyOneActiveTestVehicle)
            {
                for (int i = activeTestVehicles.Count - 1; i >= 0; i--)
                {
                    if (activeTestVehicles[i] == null)
                        activeTestVehicles.RemoveAt(i);
                }

                if (activeTestVehicles.Count > 0)
                {
                    Debug.LogWarning("[Individual Traffic Test] The previous test vehicle is still active. Let it finish before spawning another.", this);
                    return;
                }
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

            if (!spawnSelector.TryFindSpawnPosition(
                out TrafficLaneSpline startLane,
                out float startT,
                out Vector3 spawnPosition,
                out Quaternion spawnRotation))
            {
                Debug.LogWarning("[Individual Traffic Test] No hidden spawn position was found. No vehicle was created.", this);
                spawnInProgress = false;
                yield break;
            }

            List<TrafficLaneSpline> destinations = new List<TrafficLaneSpline>();
            foreach (TrafficLaneSpline lane in destinationLanes)
            {
                if (lane != null && lane != startLane && lane.isActiveAndEnabled)
                    destinations.Add(lane);
            }

            if (destinations.Count == 0)
            {
                Debug.LogWarning("[Individual Traffic Test] No active destination lane different from the selected start lane is assigned.", this);
                spawnInProgress = false;
                yield break;
            }

            TrafficLaneSpline destination = destinations[Random.Range(0, destinations.Count)];

            if (!routePlanner.TryBuildRoute(startLane, startT, destination,
                out TrafficRoutePlanner.RoutePlan plan, out string routeReason))
            {
                Debug.LogWarning("[Individual Traffic Test] Could not build this vehicle's route: " + routeReason, this);
                spawnInProgress = false;
                yield break;
            }

            if (!routePlanner.TryBuildRoutePoints(plan, out List<Vector3> routePoints, out string geometryReason) ||
                routePoints == null || routePoints.Count < 3)
            {
                Debug.LogWarning("[Individual Traffic Test] Route geometry is invalid: " + geometryReason, this);
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

            GameObject prefab = validPrefabs[Random.Range(0, validPrefabs.Count)];
            GameObject vehicle = Instantiate(prefab, start, Quaternion.LookRotation(next - start, Vector3.up));
            vehicle.name = prefab.name + " - Individual Route Test";

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

            path.SpawnRequested += HandleRouteFinished;
            activeTestVehicles.Add(vehicle);
            activeRouteObjects.Add(routeObject);

            Debug.Log("[Individual Traffic Test] Spawned '" + vehicle.name +
                "' | Start: " + startLane.LaneDisplayName + " at T=" + startT.ToString("F3") +
                " | Unique destination: " + destination.LaneDisplayName +
                " | Route distance: " + plan.TotalDistance.ToString("F1") +
                "m | Waypoints: " + routePoints.Count +
                ". This vehicle owns this private route.", vehicle);

            spawnInProgress = false;
        }

        private void HandleRouteFinished(int wayIndex, bool forward)
        {
            if (wayIndex != 0 || !forward)
                return;

            // UTS destroys the vehicle immediately after requesting the route-end spawn.
            // We intentionally do not respawn it at the same point.
            StartCoroutine(CleanupCompletedRoutes());
        }

        private IEnumerator CleanupCompletedRoutes()
        {
            yield return new WaitForSeconds(1f);

            for (int i = activeRouteObjects.Count - 1; i >= 0; i--)
            {
                GameObject routeObject = activeRouteObjects[i];
                if (routeObject == null)
                {
                    activeRouteObjects.RemoveAt(i);
                    continue;
                }

                TrafficGeneratedWalkPath path = routeObject.GetComponent<TrafficGeneratedWalkPath>();
                DestroyRouteAndSpawnPoints(path, routeObject);
                activeRouteObjects.RemoveAt(i);
            }

            for (int i = activeTestVehicles.Count - 1; i >= 0; i--)
                if (activeTestVehicles[i] == null) activeTestVehicles.RemoveAt(i);
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