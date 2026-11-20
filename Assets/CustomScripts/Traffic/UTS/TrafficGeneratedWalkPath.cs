using System;
using System.Collections.Generic;
using UnityEngine;

namespace CustomTraffic.UTS
{
    /// <summary>
    /// Builds a UTS WalkPath from a route calculated by TrafficRoutePlanner.
    /// This adapter owns only its generated waypoint data; it does not alter
    /// the source road lanes, junctions, or their splines.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrafficRoutePlanner))]
    public class TrafficGeneratedWalkPath : WalkPath
    {
        [Header("Calculated Route")]
        [SerializeField] private TrafficRoutePlanner routePlanner;
        [SerializeField] private TrafficLaneSpline startLane;
        [SerializeField, Range(0f, 1f)] private float startNormalizedPosition;
        [SerializeField] private TrafficLaneSpline destinationLane;

        public event Action<int, bool> SpawnRequested;

        public bool HasValidPath =>
            points != null &&
            pointLength != null &&
            pointLength.Length > 0 &&
            pointLength[0] >= 4;

        /// <summary>
        /// Supplies this path's route before its GameObject is activated.
        /// Add/configure this component while its GameObject is inactive so
        /// WalkPath.Awake builds the path only after these values are assigned.
        /// </summary>
        public void ConfigureRoute(
            TrafficRoutePlanner planner,
            TrafficLaneSpline lane,
            float lanePosition,
            TrafficLaneSpline destination)
        {
            routePlanner = planner;
            startLane = lane;
            startNormalizedPosition = Mathf.Clamp01(lanePosition);
            destinationLane = destination;
        }

        private void Reset()
        {
            routePlanner = GetComponent<TrafficRoutePlanner>();
        }

        private void OnValidate()
        {
            if (routePlanner == null)
                routePlanner = GetComponent<TrafficRoutePlanner>();
        }

        /// <summary>
        /// WalkPath.Awake calls this virtual method before it creates spawn points.
        /// The planner therefore builds the route before UTS initialises its path.
        /// </summary>
        public override void DrawCurved(bool withDraw, EnumDir direct = EnumDir.Forward)
        {
            loopPath = false;
            numberOfWays = 1;
            lineSpacing = Mathf.Max(0.6f, lineSpacing);
            points = null;
            pointLength = new int[2];
            _forward = new bool[1] { true };
            pathPoint.Clear();
            pathPointTransform.Clear();

            if (routePlanner == null)
                routePlanner = GetComponent<TrafficRoutePlanner>();

            if (routePlanner == null || startLane == null || destinationLane == null)
            {
                if (withDraw)
                {
                    Debug.LogWarning(
                        "[UTS Generated Route] Assign the Route Planner, Start Lane and Destination Lane.",
                        this);
                }
                return;
            }

            if (!routePlanner.TryBuildRoute(
                startLane,
                startNormalizedPosition,
                destinationLane,
                out TrafficRoutePlanner.RoutePlan plan,
                out string routeReason))
            {
                if (withDraw)
                    Debug.LogWarning("[UTS Generated Route] Route calculation failed: " + routeReason, this);
                return;
            }

            if (!routePlanner.TryBuildRoutePoints(
                plan,
                out List<Vector3> routePoints,
                out string geometryReason))
            {
                if (withDraw)
                    Debug.LogWarning("[UTS Generated Route] Waypoint generation failed: " + geometryReason, this);
                return;
            }

            BuildUTSPointArray(routePoints);

            if (withDraw && HasValidPath)
            {
                for (int i = 1; i < routePoints.Count; i++)
                    Debug.DrawLine(routePoints[i - 1], routePoints[i], Color.cyan);

                Debug.Log(
                    "[UTS Generated Route] Built UTS path: " +
                    startLane.LaneDisplayName + " -> " +
                    destinationLane.LaneDisplayName +
                    " | Waypoints: " + routePoints.Count +
                    " | Distance: " + plan.TotalDistance.ToString("F1") + "m.",
                    this);
            }
        }

        private void BuildUTSPointArray(List<Vector3> routePoints)
        {
            if (routePoints == null || routePoints.Count < 2)
                return;

            pointLength = new int[2];
            pointLength[0] = routePoints.Count + 2;
            points = new Vector3[1, pointLength[0]];

            // UTS reserves index 0 and the final index for padding.
            // Actual route points occupy indices 1 through routePoints.Count.
            for (int i = 0; i < routePoints.Count; i++)
            {
                points[0, i + 1] = routePoints[i];
                pathPoint.Add(routePoints[i]);
            }

            points[0, 0] = points[0, 1];
            points[0, pointLength[0] - 1] = points[0, pointLength[0] - 2];
        }

        public override void CreateSpawnPoints()
        {
            if (!HasValidPath)
            {
                SpawnPoints = new SpawnPoint[0];
                return;
            }

            SpawnPoints = new SpawnPoint[1];

            SpawnPoints[0] = SpawnPoint.CarCreate(
                "UTS Generated Route SpawnPoint (" + name + ")",
                points[0, 0],
                points[0, 2],
                lineSpacing,
                0,
                true,
                this,
                3f,
                10f);
        }

        public override void SpawnOnePeople(int wayIndex, bool forward)
        {
            if (SpawnRequested != null)
            {
                SpawnRequested.Invoke(wayIndex, forward);
            }
            else
            {
                Debug.LogWarning(
                    "[UTS Generated Route] A vehicle reached the end of this route, but no spawner is subscribed to SpawnRequested.",
                    this);
            }
        }
    }
}