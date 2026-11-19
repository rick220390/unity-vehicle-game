using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace CustomTraffic.UTS
{
    /// <summary>
    /// Presents either a single TrafficLaneSpline or one complete
    /// incoming-lane -> junction-turn -> outgoing-lane route to the existing
    /// UTS MovePath/CarAIController as sampled waypoints.
    ///
    /// This bridge does not modify any original UTS scripts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrafficLaneSpline))]
    public class TrafficSplineWalkPath : WalkPath
    {
        [Header("Spline Sampling")]
        [SerializeField, Min(8)] private int samplesPerSegment = 40;

        [Header("Optional Junction Turn Test")]
        [Tooltip("Leave empty for a normal continuous lane. Assign a configured junction movement to build a route that follows this lane to the junction, takes the turn, then follows the outgoing lane.")]
        [SerializeField] private TrafficJunctionMovement selectedMovement;

        [Tooltip("Warn if the turning spline endpoints are farther from their lane junction markers than this distance.")]
        [SerializeField, Min(0.1f)] private float endpointWarningDistance = 3f;

        public override void DrawCurved(bool withDraw, EnumDir direct = EnumDir.Forward)
        {
            TrafficLaneSpline lane = GetComponent<TrafficLaneSpline>();

            if (lane == null || lane.SplineContainer == null ||
                lane.SplineContainer.Spline == null || lane.SplineContainer.Spline.Count < 2)
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

            // UTS expects duplicate padding points at indices 0 and the final index.
            // Keep looping enabled for this isolated test so UTS does not request
            // an unconfigured SpawnPoint when it reaches the route's end.
            numberOfWays = 1;
            loopPath = true;
            _forward = new bool[1] { true };
            pointLength = new int[2];
            pointLength[0] = routePoints.Count + 2;
            points = new Vector3[1, routePoints.Count + 2];

            for (int i = 0; i < routePoints.Count; i++)
                points[0, i + 1] = routePoints[i];

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
                    "[UTS Traffic Bridge] Built turning test route: '" +
                    lane.LaneDisplayName + "' -> '" +
                    selectedMovement.DisplayName + "' -> '" +
                    selectedMovement.OutgoingLane.LaneDisplayName +
                    "'. Sampled waypoints: " + routePoints.Count + ".",
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

            Vector3 incomingMarkerPosition;
            Vector3 outgoingMarkerPosition;

            if (!incomingMarker.TryGetWorldPosition(out incomingMarkerPosition) ||
                !outgoingMarker.TryGetWorldPosition(out outgoingMarkerPosition))
            {
                Debug.LogError("[UTS Traffic Bridge] Could not evaluate one of the junction lane markers.", movement);
                return false;
            }

            Vector3 turnStart = EvaluateWorldPosition(turningSpline, 0f);
            Vector3 turnEnd = EvaluateWorldPosition(turningSpline, 1f);

            float startGap = Vector3.Distance(incomingMarkerPosition, turnStart);
            float endGap = Vector3.Distance(outgoingMarkerPosition, turnEnd);

            if (startGap > endpointWarningDistance || endGap > endpointWarningDistance)
            {
                Debug.LogWarning(
                    "[UTS Traffic Bridge] Turning spline endpoint alignment needs checking. " +
                    "Incoming gap: " + startGap.ToString("F2") + "m; outgoing gap: " +
                    endGap.ToString("F2") + "m. The route will still be generated.",
                    movement);
            }

            // Travel along the incoming road only as far as its junction marker.
            AddSplineSamples(
                routePoints,
                incomingSpline,
                0f,
                incomingMarker.NormalizedPosition,
                samplesPerSegment,
                false);

            // Follow the separate curved movement through the junction.
            AddSplineSamples(
                routePoints,
                turningSpline,
                0f,
                1f,
                samplesPerSegment,
                true);

            // Rejoin the outgoing directional lane at its marker and continue
            // in that lane's defined direction of travel.
            AddSplineSamples(
                routePoints,
                outgoingSpline,
                outgoingMarker.NormalizedPosition,
                1f,
                samplesPerSegment,
                true);

            return routePoints.Count >= 2;
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
                destination.Add(EvaluateWorldPosition(container, t));
            }
        }

        private Vector3 EvaluateWorldPosition(SplineContainer container, float t)
        {
            Unity.Mathematics.float3 evaluated =
                SplineUtility.EvaluatePosition(container.Spline, t);

            Vector3 local = new Vector3(evaluated.x, evaluated.y, evaluated.z);
            return container.transform.TransformPoint(local);
        }
    }
}