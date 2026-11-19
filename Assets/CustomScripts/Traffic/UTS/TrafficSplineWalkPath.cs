using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace CustomTraffic.UTS
{
    /// <summary>
    /// Converts a directional traffic lane, and optionally one junction movement,
    /// into the waypoint format expected by the existing UTS vehicle AI.
    ///
    /// The generated route is deliberately NON-LOOPING. UTS cars are respawned
    /// at the route start when they reach its end, rather than steering across
    /// the junction from the route end back to its beginning.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrafficLaneSpline))]
    public class TrafficSplineWalkPath : WalkPath
    {
        [Header("Spline Sampling")]
        [SerializeField, Min(8)] private int samplesPerSegment = 48;

        [Header("Optional Junction Turn Test")]
        [Tooltip("Leave empty to follow this lane only. Assign a configured movement to follow the incoming lane, the curved junction spline, and the outgoing lane.")]
        [SerializeField] private TrafficJunctionMovement selectedMovement;

        [Tooltip("Search distance either side of each lane marker when matching a turn endpoint to its road spline.")]
        [SerializeField, Range(0.01f, 0.4f)] private float endpointSearchWindow = 0.15f;

        [Tooltip("Stop route generation if a turn endpoint is this far or more from its connected road spline.")]
        [SerializeField, Min(0.1f)] private float endpointWarningDistance = 3f;

        public event Action<int, bool> SpawnRequested;

        public bool HasValidPath =>
            points != null &&
            pointLength != null &&
            pointLength.Length > 0 &&
            pointLength[0] >= 4;

        public override void DrawCurved(bool withDraw, EnumDir direct = EnumDir.Forward)
        {
            TrafficLaneSpline lane = GetComponent<TrafficLaneSpline>();

            // Never let UTS close an open road route with an imaginary line.
            // Cars will be respawned at the route start through SpawnPoint instead.
            loopPath = false;
            points = null;
            pointLength = new int[2];

            if (lane == null || lane.SplineContainer == null ||
                lane.SplineContainer.Spline == null ||
                lane.SplineContainer.Spline.Count < 2)
            {
                Debug.LogError("[UTS Traffic Bridge] A valid TrafficLaneSpline/SplineContainer is required.", this);
                return;
            }

            List<Vector3> routePoints = new List<Vector3>();

            if (selectedMovement == null)
            {
                AddSplineSamples(
                    routePoints,
                    lane.SplineContainer,
                    0f,
                    1f,
                    samplesPerSegment,
                    false);
            }
            else if (!BuildTurningRoute(lane, selectedMovement, routePoints))
            {
                return;
            }

            if (routePoints.Count < 2)
            {
                Debug.LogError("[UTS Traffic Bridge] The generated route has fewer than two usable points.", this);
                return;
            }

            numberOfWays = 1;
            loopPath = false;
            _forward = new bool[1] { true };
            pointLength = new int[2];
            pointLength[0] = routePoints.Count + 2;
            points = new Vector3[1, routePoints.Count + 2];

            for (int i = 0; i < routePoints.Count; i++)
                points[0, i + 1] = routePoints[i];

            // UTS uses duplicate padding points at the beginning and end.
            points[0, 0] = points[0, 1];
            points[0, routePoints.Count + 1] = points[0, routePoints.Count];

            if (withDraw)
            {
                for (int i = 1; i < routePoints.Count; i++)
                    Debug.DrawLine(points[0, i], points[0, i + 1], Color.cyan);
            }

            if (selectedMovement != null)
            {
                Debug.Log(
                    "[UTS Traffic Bridge] Built an open turning route: '" +
                    lane.LaneDisplayName + "' -> '" +
                    selectedMovement.DisplayName + "' -> '" +
                    selectedMovement.OutgoingLane.LaneDisplayName +
                    "'. Waypoints: " + routePoints.Count +
                    ". UTS will respawn vehicles at the route start instead of looping across the junction.",
                    this);
            }
        }

        private bool BuildTurningRoute(
            TrafficLaneSpline incomingLane,
            TrafficJunctionMovement movement,
            List<Vector3> routePoints)
        {
            if (!movement.IsConfigured(out string reason))
            {
                Debug.LogError(
                    "[UTS Traffic Bridge] Selected junction movement is not configured: " + reason,
                    movement);
                return false;
            }

            if (movement.IncomingLane != incomingLane)
            {
                Debug.LogError(
                    "[UTS Traffic Bridge] The selected movement's incoming lane is '" +
                    movement.IncomingLane.LaneDisplayName +
                    "', but this adapter is on '" + incomingLane.LaneDisplayName + "'.",
                    this);
                return false;
            }

            if (!TryGetJunctionMarker(
                    movement.Junction,
                    movement.IncomingLane,
                    out TrafficJunctionMarker incomingMarker))
            {
                Debug.LogError(
                    "[UTS Traffic Bridge] Could not find a valid incoming lane marker for the selected movement.",
                    movement);
                return false;
            }

            if (!TryGetJunctionMarker(
                    movement.Junction,
                    movement.OutgoingLane,
                    out TrafficJunctionMarker outgoingMarker))
            {
                Debug.LogError(
                    "[UTS Traffic Bridge] Could not find a valid outgoing lane marker for the selected movement.",
                    movement);
                return false;
            }

            SplineContainer incomingSpline = incomingLane.SplineContainer;
            SplineContainer turningSpline = movement.MovementSpline;
            SplineContainer outgoingSpline = movement.OutgoingLane.SplineContainer;

            if (outgoingSpline == null || outgoingSpline.Spline == null ||
                outgoingSpline.Spline.Count < 2)
            {
                Debug.LogError("[UTS Traffic Bridge] The outgoing lane has no valid spline.", movement);
                return false;
            }

            Vector3 turnStart = EvaluateWorldPosition(turningSpline, 0f);
            Vector3 turnEnd = EvaluateWorldPosition(turningSpline, 1f);

            // Use the marker as a search hint, but join the road at the point
            // actually nearest to the hand-drawn turn spline endpoint. This
            // prevents a misplaced marker from adding a straight zigzag leg.
            float incomingJoinT = FindClosestNormalizedPosition(
                incomingSpline,
                turnStart,
                incomingMarker.NormalizedPosition,
                out float startGap);

            float outgoingJoinT = FindClosestNormalizedPosition(
                outgoingSpline,
                turnEnd,
                outgoingMarker.NormalizedPosition,
                out float endGap);

            if (startGap > endpointWarningDistance || endGap > endpointWarningDistance)
            {
                Debug.LogError(
                    "[UTS Traffic Bridge] Route not built because a turn endpoint is too far from its road spline. " +
                    "Incoming endpoint gap: " + startGap.ToString("F2") +
                    "m; outgoing endpoint gap: " + endGap.ToString("F2") +
                    "m. Move the end of the hand-drawn turn spline closer to its connected road lane. " +
                    "No artificial connector line will be generated.",
                    movement);
                return false;
            }

            if (outgoingJoinT >= 0.995f)
            {
                Debug.LogError(
                    "[UTS Traffic Bridge] The turn reaches the very end of the outgoing lane spline, leaving no road section for the vehicle to follow. Move the outgoing junction marker/turn endpoint earlier along that lane.",
                    movement);
                return false;
            }

            // 1. Follow the incoming road up to the point nearest the turn start.
            AddSplineSamples(
                routePoints,
                incomingSpline,
                0f,
                incomingJoinT,
                samplesPerSegment,
                false);

            // 2. Follow the user's curved turn spline exactly.
            AddSplineSamples(
                routePoints,
                turningSpline,
                0f,
                1f,
                samplesPerSegment,
                true);

            // 3. Continue from the point nearest the turn end on the outgoing lane.
            AddSplineSamples(
                routePoints,
                outgoingSpline,
                outgoingJoinT,
                1f,
                samplesPerSegment,
                true);

            Debug.Log(
                "[UTS Traffic Bridge] Turn endpoints matched to road splines. " +
                "Incoming T: " + incomingJoinT.ToString("F3") +
                " (gap " + startGap.ToString("F2") + "m), outgoing T: " +
                outgoingJoinT.ToString("F3") + " (gap " + endGap.ToString("F2") + "m).",
                movement);

            return routePoints.Count >= 2;
        }

        private float FindClosestNormalizedPosition(
            SplineContainer container,
            Vector3 targetWorldPosition,
            float expectedT,
            out float closestGap)
        {
            float minT = Mathf.Max(0f, expectedT - endpointSearchWindow);
            float maxT = Mathf.Min(1f, expectedT + endpointSearchWindow);

            const int searchSteps = 80;
            float bestT = expectedT;
            float bestDistanceSquared = float.MaxValue;

            for (int i = 0; i <= searchSteps; i++)
            {
                float fraction = i / (float)searchSteps;
                float candidateT = Mathf.Lerp(minT, maxT, fraction);
                Vector3 candidatePosition = EvaluateWorldPosition(container, candidateT);
                float distanceSquared = (candidatePosition - targetWorldPosition).sqrMagnitude;

                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestT = candidateT;
                }
            }

            closestGap = Mathf.Sqrt(bestDistanceSquared);
            return bestT;
        }

        private bool TryGetJunctionMarker(
            TrafficJunction junction,
            TrafficLaneSpline lane,
            out TrafficJunctionMarker result)
        {
            result = null;

            if (junction == null || lane == null)
                return false;

            TrafficJunctionMarker[] markers = junction.GetMarkers();

            foreach (TrafficJunctionMarker marker in markers)
            {
                if (marker != null &&
                    marker.Lane == lane &&
                    marker.IsConfigured(out _))
                {
                    result = marker;
                    return true;
                }
            }

            return false;
        }

        private void AddSplineSamples(
            List<Vector3> destination,
            SplineContainer container,
            float startT,
            float endT,
            int sampleCount,
            bool skipFirst)
        {
            if (container == null || container.Spline == null ||
                container.Spline.Count < 2)
                return;

            int firstSample = skipFirst ? 1 : 0;

            for (int i = firstSample; i < sampleCount; i++)
            {
                float fraction = i / (float)(sampleCount - 1);
                float t = Mathf.Lerp(startT, endT, fraction);
                Vector3 sample = EvaluateWorldPosition(container, t);

                // Avoid duplicate points at spline joins without changing the curve.
                if (destination.Count == 0 ||
                    Vector3.SqrMagnitude(destination[destination.Count - 1] - sample) > 0.0001f)
                {
                    destination.Add(sample);
                }
            }
        }

        private Vector3 EvaluateWorldPosition(SplineContainer container, float t)
        {
            Unity.Mathematics.float3 evaluated =
                SplineUtility.EvaluatePosition(container.Spline, t);

            Vector3 local = new Vector3(evaluated.x, evaluated.y, evaluated.z);
            return container.transform.TransformPoint(local);
        }

        public override void CreateSpawnPoints()
        {
            if (!HasValidPath)
            {
                SpawnPoints = new SpawnPoint[0];
                return;
            }

            SpawnPoints = new SpawnPoint[1];

            Vector3 startPoint = points[0, 0];
            Vector3 nextPoint = points[0, 2];

            SpawnPoints[0] = SpawnPoint.CarCreate(
                "UTS Traffic SpawnPoint (" + name + ")",
                startPoint,
                nextPoint,
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
                    "[UTS Traffic Bridge] A vehicle reached the end of the route, but no test spawner is subscribed to the respawn event.",
                    this);
            }
        }
    }
}