using UnityEngine;

namespace CustomTraffic
{
    /// <summary>
    /// Represents one directed lane segment in the road network.
    /// The lane spline runs from startNode to endNode.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficConnection : MonoBehaviour
    {
        [Header("Connection Identity")]
        [SerializeField] private string connectionId = "connection_01";
        [SerializeField] private string displayName = "Traffic Connection";

        [Header("Lane and Endpoints")]
        [SerializeField] private TrafficLaneSpline lane;
        [SerializeField] private TrafficNode startNode;
        [SerializeField] private TrafficNode endNode;

        [Header("Scene Visualisation")]
        [SerializeField] private bool showConnection = true;
        [SerializeField, Min(0.1f)] private float arrowSize = 1.5f;
        [SerializeField] private Color connectionColour = new Color(1f, 0.85f, 0.15f, 1f);

        public string ConnectionId => connectionId;
        public string DisplayName => displayName;
        public TrafficLaneSpline Lane => lane;
        public TrafficNode StartNode => startNode;
        public TrafficNode EndNode => endNode;
        public bool IsConfigured => lane != null && startNode != null && endNode != null;

        private void OnValidate()
        {
            arrowSize = Mathf.Max(0.1f, arrowSize);
        }

        private void OnDrawGizmos()
        {
            if (!showConnection || startNode == null || endNode == null)
                return;

            Vector3 start = startNode.transform.position;
            Vector3 end = endNode.transform.position;
            Vector3 direction = (end - start).normalized;

            if (direction.sqrMagnitude < 0.001f)
                return;

            Gizmos.color = connectionColour;
            Gizmos.DrawLine(start, end);

            Vector3 arrowTip = Vector3.Lerp(start, end, 0.65f);
            Vector3 arrowBase = arrowTip - direction * arrowSize * 0.5f;
            Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;

            if (side.sqrMagnitude < 0.001f)
                side = Vector3.Cross(direction, Vector3.right).normalized;

            Gizmos.DrawLine(arrowTip, arrowBase + side * arrowSize * 0.2f);
            Gizmos.DrawLine(arrowTip, arrowBase - side * arrowSize * 0.2f);
        }
    }
}