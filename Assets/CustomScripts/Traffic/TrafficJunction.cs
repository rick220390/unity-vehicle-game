using UnityEngine;

namespace CustomTraffic
{
    /// <summary>
    /// Logical identity for a physical road junction.
    /// Directional road splines remain continuous; marker components
    /// identify where each connected lane reaches this junction.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficJunction : MonoBehaviour
    {
        public enum JunctionType
        {
            T_Junction,
            Crossroads,
            Other
        }

        [Header("Junction Identity")]
        [SerializeField] private string junctionId = "junction_01";
        [SerializeField] private string junctionDisplayName = "New Junction";
        [SerializeField] private JunctionType junctionType = JunctionType.T_Junction;

        public string JunctionId => junctionId;
        public string JunctionDisplayName => junctionDisplayName;
        public JunctionType Type => junctionType;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 1f);
            Gizmos.DrawWireSphere(transform.position, 1f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);
        }
    }
}
