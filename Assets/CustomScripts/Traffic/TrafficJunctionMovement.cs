using UnityEngine;
using UnityEngine.Splines;

namespace CustomTraffic
{
    /// <summary>
    /// Defines one permitted vehicle movement through a junction.
    /// The incoming and outgoing references are the continuous road lanes;
    /// movementSpline is a separate curved path through the junction.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficJunctionMovement : MonoBehaviour
    {
        [Header("Movement Identity")]
        [SerializeField] private string movementId = "movement_01";
        [SerializeField] private string displayName = "New Junction Movement";

        [Header("Junction and Road Lanes")]
        [SerializeField] private TrafficJunction junction;
        [SerializeField] private TrafficLaneSpline incomingLane;
        [SerializeField] private TrafficLaneSpline outgoingLane;

        [Header("Turning Path")]
        [SerializeField] private SplineContainer movementSpline;

        [Header("Scene View")]
        [SerializeField] private bool showMovement = true;
        [SerializeField, Min(0.1f)] private float markerSize = 0.5f;
        [SerializeField] private Color movementColour = new Color(1f, 0.85f, 0.15f, 1f);

        public string MovementId => movementId;
        public string DisplayName => displayName;
        public TrafficJunction Junction => junction;
        public TrafficLaneSpline IncomingLane => incomingLane;
        public TrafficLaneSpline OutgoingLane => outgoingLane;
        public SplineContainer MovementSpline => movementSpline;

        public bool IsConfigured(out string reason)
        {
            if (junction == null)
            {
                reason = "Junction has not been assigned.";
                return false;
            }

            if (incomingLane == null)
            {
                reason = "Incoming lane has not been assigned.";
                return false;
            }

            if (outgoingLane == null)
            {
                reason = "Outgoing lane has not been assigned.";
                return false;
            }

            if (incomingLane == outgoingLane)
            {
                reason = "Incoming and outgoing lanes cannot be the same.";
                return false;
            }

            if (movementSpline == null || movementSpline.Spline == null ||
                movementSpline.Spline.Count < 2)
            {
                reason = "Turning path needs a SplineContainer with at least two control points.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            markerSize = Mathf.Max(0.1f, markerSize);
        }

        private void OnDrawGizmos()
        {
            if (!showMovement || movementSpline == null ||
                movementSpline.Spline == null || movementSpline.Spline.Count < 2)
                return;

            var spline = movementSpline.Spline;
            Gizmos.color = movementColour;

            const int segments = 24;
            Vector3 previous = GetWorldPosition(spline, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 current = GetWorldPosition(spline, t);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }

            Vector3 arrowPosition = GetWorldPosition(spline, 0.7f);
            Vector3 tangent = GetWorldTangent(spline, 0.7f);
            if (tangent.sqrMagnitude > 0.001f)
            {
                Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.001f)
                    side = Vector3.Cross(tangent, Vector3.right).normalized;

                Vector3 tip = arrowPosition + tangent * markerSize;
                Vector3 basePoint = tip - tangent * markerSize * 0.4f;
                Gizmos.DrawLine(arrowPosition, tip);
                Gizmos.DrawLine(tip, basePoint + side * markerSize * 0.2f);
                Gizmos.DrawLine(tip, basePoint - side * markerSize * 0.2f);
            }
        }

        private Vector3 GetWorldPosition(Spline spline, float t)
        {
            var p = SplineUtility.EvaluatePosition(spline, t);
            return movementSpline.transform.TransformPoint(new Vector3(p.x, p.y, p.z));
        }

        private Vector3 GetWorldTangent(Spline spline, float t)
        {
            var tangent = SplineUtility.EvaluateTangent(spline, t);
            return movementSpline.transform.TransformDirection(
                new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
        }
    }
}