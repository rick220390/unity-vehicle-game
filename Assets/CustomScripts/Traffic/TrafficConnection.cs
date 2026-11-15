using UnityEngine;

namespace CustomTraffic
{
    public enum TrafficConnectionType
    {
        // Keeps existing scene connections working until they are explicitly classified.
        Legacy,
        RoadLane,
        JunctionMovement
    }

    /// <summary>
    /// Represents one directed path segment in the road network.
    /// RoadLane connections join road ends/exits to road ends/entries.
    /// JunctionMovement connections join a junction entry to a junction exit.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficConnection : MonoBehaviour
    {
        [Header("Connection Identity")]
        [SerializeField] private string connectionId = "connection_01";
        [SerializeField] private string displayName = "Traffic Connection";
        [SerializeField] private TrafficConnectionType connectionType = TrafficConnectionType.Legacy;

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
        public TrafficConnectionType ConnectionType => connectionType;
        public TrafficLaneSpline Lane => lane;
        public TrafficNode StartNode => startNode;
        public TrafficNode EndNode => endNode;
        public bool IsConfigured => lane != null && startNode != null && endNode != null;

        public bool IsValidForNetwork(out string reason)
        {
            if (!IsConfigured)
            {
                reason = "The lane, start node, or end node reference is missing.";
                return false;
            }

            if (startNode == endNode)
            {
                reason = "The start and end nodes cannot be the same object.";
                return false;
            }

            if (connectionType == TrafficConnectionType.RoadLane)
            {
                bool validStart =
                    startNode.NodeType == TrafficNodeType.RoadEnd ||
                    startNode.NodeType == TrafficNodeType.JunctionExit;

                bool validEnd =
                    endNode.NodeType == TrafficNodeType.RoadEnd ||
                    endNode.NodeType == TrafficNodeType.JunctionEntry;

                if (!validStart || !validEnd)
                {
                    reason = "A RoadLane must start at a RoadEnd or JunctionExit and end at a RoadEnd or JunctionEntry.";
                    return false;
                }
            }
            else if (connectionType == TrafficConnectionType.JunctionMovement)
            {
                if (!startNode.IsJunctionEntry || !endNode.IsJunctionExit)
                {
                    reason = "A JunctionMovement must start at a JunctionEntry and end at a JunctionExit.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

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