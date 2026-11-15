using UnityEngine;

namespace CustomTraffic
{
    public enum TrafficNodeType
    {
        RoadEnd,
        Junction,
        JunctionEntry,
        JunctionExit
    }

    public class TrafficNode : MonoBehaviour
    {
        [Header("Node Settings")]
        [SerializeField] private string nodeId = "node_01";
        [SerializeField] private string displayName = "Traffic Node";
        [SerializeField] private TrafficNodeType nodeType = TrafficNodeType.RoadEnd;

        [Header("Scene Visualisation")]
        [SerializeField] private float gizmoRadius = 0.6f;

        public string NodeId => nodeId;
        public string DisplayName => displayName;
        public TrafficNodeType NodeType => nodeType;
        public bool IsJunction => nodeType == TrafficNodeType.Junction;
        public bool IsJunctionEntry => nodeType == TrafficNodeType.JunctionEntry;
        public bool IsJunctionExit => nodeType == TrafficNodeType.JunctionExit;

        private void OnDrawGizmos()
        {
            Gizmos.color = GetNodeColour();

            Gizmos.DrawWireSphere(transform.position, gizmoRadius);
            Gizmos.DrawLine(
                transform.position - transform.up * gizmoRadius,
                transform.position + transform.up * gizmoRadius
            );
        }

        private Color GetNodeColour()
        {
            switch (nodeType)
            {
                case TrafficNodeType.Junction:
                    return new Color(1f, 0.65f, 0.1f, 1f);
                case TrafficNodeType.JunctionEntry:
                    return new Color(0.2f, 1f, 0.35f, 1f);
                case TrafficNodeType.JunctionExit:
                    return new Color(0.75f, 0.3f, 1f, 1f);
                default:
                    return new Color(0.2f, 0.85f, 1f, 1f);
            }
        }
    }
}