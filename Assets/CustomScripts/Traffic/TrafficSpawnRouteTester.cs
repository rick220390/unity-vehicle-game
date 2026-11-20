using System.Collections.Generic;
using UnityEngine;
using CustomTraffic.UTS;

namespace CustomTraffic
{
    /// <summary>
    /// Test bridge between the hidden spawn-position selector and route planner.
    /// It builds a temporary, private UTS path from the selected spawn position.
    /// It does not spawn a vehicle or modify existing test paths.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficSpawnRouteTester : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TrafficSpawnPositionSelector spawnSelector;
        [SerializeField] private TrafficRoutePlanner routePlanner;

        [Header("Destination Options")]
        [Tooltip("The tester randomly chooses one of these lanes as the destination.")]
        [SerializeField] private TrafficLaneSpline[] destinationLanes;

        [Header("Test")]
        [SerializeField] private bool createPathObject = true;

        private GameObject testPathObject;

        [ContextMenu("Test Build Route From Hidden Spawn Position")]
        private void TestBuildRouteFromSpawnPosition()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Traffic Spawn Route Test] Enter Play mode before running this test.", this);
                return;
            }

            if (spawnSelector == null || routePlanner == null)
            {
                Debug.LogWarning("[Traffic Spawn Route Test] Assign the Spawn Selector and Route Planner.", this);
                return;
            }

            if (destinationLanes == null || destinationLanes.Length == 0)
            {
                Debug.LogWarning("[Traffic Spawn Route Test] Add at least one destination lane.", this);
                return;
            }

            if (!spawnSelector.TryFindSpawnPosition(
                out TrafficLaneSpline startLane,
                out float startT,
                out Vector3 spawnPosition,
                out Quaternion spawnRotation))
            {
                Debug.LogWarning("[Traffic Spawn Route Test] The selector could not find a suitable hidden spawn position.", this);
                return;
            }

            List<TrafficLaneSpline> validDestinations = new List<TrafficLaneSpline>();
            foreach (TrafficLaneSpline destination in destinationLanes)
            {
                if (destination != null && destination != startLane && destination.isActiveAndEnabled)
                    validDestinations.Add(destination);
            }

            if (validDestinations.Count == 0)
            {
                Debug.LogWarning(
                    "[Traffic Spawn Route Test] No valid destination lanes are assigned. " +
                    "Choose at least one active lane different from the selected start lane.",
                    this);
                return;
            }

            TrafficLaneSpline destinationLane =
                validDestinations[Random.Range(0, validDestinations.Count)];

            if (!routePlanner.TryBuildRoute(
                startLane,
                startT,
                destinationLane,
                out TrafficRoutePlanner.RoutePlan plan,
                out string reason))
            {
                Debug.LogWarning("[Traffic Spawn Route Test] Route calculation failed: " + reason, this);
                return;
            }

            if (!routePlanner.TryBuildRoutePoints(
                plan,
                out List<Vector3> waypoints,
                out string geometryReason))
            {
                Debug.LogWarning("[Traffic Spawn Route Test] Route geometry failed: " + geometryReason, this);
                return;
            }

            float startGap = Vector3.Distance(spawnPosition, waypoints[0]);
            Debug.Log(
                "[Traffic Spawn Route Test] Connected spawn selector to route planner. " +
                "Start lane: " + startLane.LaneDisplayName +
                " | T: " + startT.ToString("F3") +
                " | Destination: " + destinationLane.LaneDisplayName +
                " | Spawn-to-route start gap: " + startGap.ToString("F3") + "m" +
                " | First waypoint: " + waypoints[0].ToString("F2") +
                " | Waypoints: " + waypoints.Count,
                this);

            if (!createPathObject)
                return;

            // Remove only the previous temporary path created by this tester.
            if (testPathObject != null)
                Destroy(testPathObject);

            testPathObject = new GameObject("TEMP - Spawn To Destination UTS Route");
            testPathObject.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            testPathObject.SetActive(false);

            TrafficGeneratedWalkPath generatedPath =
                testPathObject.AddComponent<TrafficGeneratedWalkPath>();

            generatedPath.ConfigureRoute(routePlanner, startLane, startT, destinationLane);
            testPathObject.SetActive(true);

            if (!generatedPath.HasValidPath)
            {
                Debug.LogWarning(
                    "[Traffic Spawn Route Test] The generated UTS path did not initialise correctly. " +
                    "Check the route path component and Console for details.",
                    generatedPath);
                return;
            }

            Debug.Log(
                "[Traffic Spawn Route Test] Temporary UTS path created successfully. " +
                "The path starts at the selected spawn position. No vehicle has been spawned.",
                generatedPath);
        }
    }
}