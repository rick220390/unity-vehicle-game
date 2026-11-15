using UnityEngine;
using UnityEngine.Splines;

namespace CustomTraffic
{
    /// <summary>
    /// Describes one traffic lane.
    ///
    /// The direction of travel is defined by the order of the spline:
    /// vehicles travel from the spline's first control point to its last.
    /// Scene-view arrows make that direction visible while editing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public class TrafficLaneSpline : MonoBehaviour
    {
        public enum AllowedLaneMovement
        {
            Any,
            LeftOnly,
            StraightOnly,
            RightOnly,
            LeftOrStraight,
            StraightOrRight
        }

        [Header("Lane Identity")]
        [SerializeField] private string laneId = "lane_01";
        [SerializeField] private string laneDisplayName = "Lane 1";
        [SerializeField] private AllowedLaneMovement allowedMovement = AllowedLaneMovement.Any;

        [Header("Road Rules")]
        [SerializeField, Min(1)] private int speedLimitKph = 30;

        [Header("Scene View Direction")]
        [SerializeField] private bool showDirectionArrows = true;
        [SerializeField, Range(1, 30)] private int arrowCount = 6;
        [SerializeField, Min(0.1f)] private float arrowSize = 1f;
        [SerializeField] private Color arrowColour = new Color(0.1f, 0.8f, 1f, 1f);

        private SplineContainer splineContainer;

        public string LaneId => laneId;
        public string LaneDisplayName => laneDisplayName;
        public AllowedLaneMovement AllowedMovement => allowedMovement;
        public int SpeedLimitKph => speedLimitKph;
        public SplineContainer SplineContainer => splineContainer;

        private void OnValidate()
        {
            if (splineContainer == null)
                splineContainer = GetComponent<SplineContainer>();

            arrowCount = Mathf.Clamp(arrowCount, 1, 30);
            arrowSize = Mathf.Max(0.1f, arrowSize);
            speedLimitKph = Mathf.Max(1, speedLimitKph);
        }

        private void OnDrawGizmos()
        {
            if (!showDirectionArrows)
                return;

            if (splineContainer == null)
                splineContainer = GetComponent<SplineContainer>();

            if (splineContainer == null || splineContainer.Spline == null)
                return;

            var spline = splineContainer.Spline;

            if (spline.Count < 2)
                return;

            Gizmos.color = arrowColour;

            // Evenly distribute arrows along the spline.
            // Every arrow points from the first spline point towards the last.
            for (int i = 0; i < arrowCount; i++)
            {
                float t = (i + 1f) / (arrowCount + 1f);

                Vector3 localPosition = ToVector3(SplineUtility.EvaluatePosition(spline, t));
                Vector3 localTangent = ToVector3(SplineUtility.EvaluateTangent(spline, t));

                Vector3 worldPosition = splineContainer.transform.TransformPoint(localPosition);
                Vector3 worldDirection = splineContainer.transform.TransformDirection(localTangent).normalized;

                if (worldDirection.sqrMagnitude < 0.001f)
                    continue;

                DrawDirectionArrow(worldPosition, worldDirection);
            }
        }

        private void DrawDirectionArrow(Vector3 position, Vector3 direction)
        {
            Vector3 arrowTip = position + direction * arrowSize;
            Vector3 sideDirection = Vector3.Cross(direction, Vector3.up).normalized;

            // Handle splines that point almost vertically.
            if (sideDirection.sqrMagnitude < 0.001f)
                sideDirection = Vector3.Cross(direction, Vector3.right).normalized;

            float headLength = arrowSize * 0.35f;
            float headWidth = arrowSize * 0.2f;

            Vector3 arrowBase = arrowTip - direction * headLength;

            Gizmos.DrawLine(position, arrowTip);
            Gizmos.DrawLine(arrowTip, arrowBase + sideDirection * headWidth);
            Gizmos.DrawLine(arrowTip, arrowBase - sideDirection * headWidth);
        }

        private static Vector3 ToVector3(Unity.Mathematics.float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }
    }
}