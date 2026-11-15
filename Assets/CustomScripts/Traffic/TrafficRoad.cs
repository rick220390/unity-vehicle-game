using UnityEngine;

namespace CustomTraffic
{
    /// <summary>
    /// Identifies one physical road and the two directional lane splines
    /// that carry traffic along it.
    ///
    /// Each directional spline can run continuously along the road and pass
    /// junctions. Junctions will be represented separately by markers.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficRoad : MonoBehaviour
    {
        [Header("Road Identity")]
        [SerializeField] private string roadId = "road_01";
        [SerializeField] private string roadDisplayName = "New Road";

        [Header("Direction A")]
        [SerializeField] private string directionAName = "Direction A";
        [SerializeField] private TrafficLaneSpline directionALane;

        [Header("Direction B")]
        [SerializeField] private string directionBName = "Direction B";
        [SerializeField] private TrafficLaneSpline directionBLane;

        public string RoadId => roadId;
        public string RoadDisplayName => roadDisplayName;

        public string DirectionAName => directionAName;
        public TrafficLaneSpline DirectionALane => directionALane;

        public string DirectionBName => directionBName;
        public TrafficLaneSpline DirectionBLane => directionBLane;

        /// <summary>
        /// True when both directions have lane splines assigned
        /// and they are not accidentally using the same spline.
        /// </summary>
        public bool HasBothDirectionalLanes =>
            directionALane != null &&
            directionBLane != null &&
            directionALane != directionBLane;

        /// <summary>
        /// Checks the road's basic setup without changing any scene data.
        /// </summary>
        public bool IsConfigured(out string reason)
        {
            if (string.IsNullOrWhiteSpace(roadId))
            {
                reason = "Road ID is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(roadDisplayName))
            {
                reason = "Road display name is empty.";
                return false;
            }

            if (directionALane == null)
            {
                reason = "Direction A lane spline has not been assigned.";
                return false;
            }

            if (directionBLane == null)
            {
                reason = "Direction B lane spline has not been assigned.";
                return false;
            }

            if (directionALane == directionBLane)
            {
                reason = "Both directions reference the same lane spline.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
