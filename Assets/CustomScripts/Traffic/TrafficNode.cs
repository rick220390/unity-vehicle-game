using UnityEngine;

namespace CustomTraffic
{
    public enum TrafficNodeType
    {
        RoadEnd,
        Junction
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

        private void OnDrawGizmos()
        {
            Gizmos.color = nodeType == TrafficNodeType.Junction
                ? new Color(1f, 0.65f, 0.1f, 1f)
                : new Color(0.2f, 0.85f, 1f, 1f);

            Gizmos.DrawWireSphere(transform.position, gizmoRadius);
            Gizmos.DrawLine(
                transform.position - transform.up * gizmoRadius,
                transform.position + transform.up * gizmoRadius
            );
        }
    }
}