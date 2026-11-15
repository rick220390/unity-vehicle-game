using UnityEngine;
using UnityEngine.Splines;

namespace CustomTraffic
{
    /// <summary>
    /// Marks the exact point where one directional road lane reaches a junction.
    /// The road spline remains continuous; this component stores a position on it.
    /// Create one marker for each directional lane that connects to the junction.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficJunctionMarker : MonoBehaviour
    {
        [Header("Connections")]
        [SerializeField] private TrafficJunction junction;
        [SerializeField] private TrafficRoad road;
        [SerializeField] private TrafficLaneSpline lane;

        [Header("Position Along Lane")]
        [SerializeField, Range(0f, 1f)]
        private float normalizedPosition = 0.5f;

        [Header("Scene View")]
        [SerializeField] private bool showMarker = true;
        [SerializeField, Min(0.1f)] private float markerSize = 0.6f;
        [SerializeField] private Color markerColour = new Color(1f, 0.2f, 0.8f, 1f);

        public TrafficJunction Junction => junction;
        public TrafficRoad Road => road;
        public TrafficLaneSpline Lane => lane;
        public float NormalizedPosition => normalizedPosition;

        /// <summary>
        /// Gets the marker's world position by evaluating its assigned lane spline.
        /// </summary>
        public bool TryGetWorldPosition(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            if (lane == null || lane.SplineContainer == null)
                return false;

            var container = lane.SplineContainer;
            if (container.Spline == null || container.Spline.Count < 2)
                return false;

            var localPosition = SplineUtility.EvaluatePosition(
                container.Spline, normalizedPosition);

            worldPosition = container.transform.TransformPoint(
                new Vector3(localPosition.x, localPosition.y, localPosition.z));

            return true;
        }

        /// <summary>
        /// Checks that this marker references a lane belonging to its assigned road.
        /// </summary>
        public bool IsConfigured(out string reason)
        {
            if (junction == null)
            {
                reason = "Junction has not been assigned.";
                return false;
            }

            if (road == null)
            {
                reason = "Road has not been assigned.";
                return false;
            }

            if (lane == null)
            {
                reason = "Directional lane spline has not been assigned.";
                return false;
            }

            if (lane != road.DirectionALane && lane != road.DirectionBLane)
            {
                reason = "The selected lane is not one of this road's directional splines.";
                return false;
            }

            if (!TryGetWorldPosition(out _))
            {
                reason = "The selected lane does not have a valid spline with at least two points.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            normalizedPosition = Mathf.Clamp01(normalizedPosition);
            markerSize = Mathf.Max(0.1f, markerSize);
        }

        private void OnDrawGizmos()
        {
            if (!showMarker || !TryGetWorldPosition(out Vector3 lanePosition))
                return;

            Gizmos.color = markerColour;
            Gizmos.DrawSphere(lanePosition, markerSize * 0.5f);
            Gizmos.DrawWireSphere(lanePosition, markerSize * 0.75f);

            if (junction != null)
                Gizmos.DrawLine(lanePosition, junction.transform.position);
        }
    }
}
