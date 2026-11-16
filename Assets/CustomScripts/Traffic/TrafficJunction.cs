using System.Collections.Generic;
using UnityEngine;

namespace CustomTraffic
{
    /// <summary>
    /// Logical identity and local validator for one physical road junction.
    /// Directional road splines remain continuous; markers identify where
    /// each connected lane reaches this junction.
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

        [Header("Scene View")]
        [SerializeField] private bool showJunctionGizmo = true;
        [SerializeField, Min(0.1f)] private float gizmoSize = 1f;

        public string JunctionId => junctionId;
        public string JunctionDisplayName => junctionDisplayName;
        public JunctionType Type => junctionType;

        /// <summary>Returns all lane markers belonging to this junction.</summary>
        public TrafficJunctionMarker[] GetMarkers()
        {
            TrafficJunctionMarker[] allMarkers =
                GetComponentsInChildren<TrafficJunctionMarker>(true);

            List<TrafficJunctionMarker> results = new List<TrafficJunctionMarker>();

            foreach (TrafficJunctionMarker marker in allMarkers)
            {
                if (marker != null && marker.Junction == this)
                    results.Add(marker);
            }

            return results.ToArray();
        }

        /// <summary>Returns all movement definitions belonging to this junction.</summary>
        public TrafficJunctionMovement[] GetMovements()
        {
            TrafficJunctionMovement[] allMovements =
                GetComponentsInChildren<TrafficJunctionMovement>(true);

            List<TrafficJunctionMovement> results =
                new List<TrafficJunctionMovement>();

            foreach (TrafficJunctionMovement movement in allMovements)
            {
                if (movement != null && movement.Junction == this)
                    results.Add(movement);
            }

            return results.ToArray();
        }

        /// <summary>
        /// Checks marker and movement references without changing scene data.
        /// Run from the component context menu in the Inspector.
        /// </summary>
        [ContextMenu("Validate Junction")]
        public void ValidateJunction()
        {
            TrafficJunctionMarker[] markers = GetMarkers();
            TrafficJunctionMovement[] movements = GetMovements();

            int validMarkers = 0;
            int invalidMarkers = 0;
            int validMovements = 0;
            int invalidMovements = 0;

            HashSet<TrafficLaneSpline> markedLanes = new HashSet<TrafficLaneSpline>();

            foreach (TrafficJunctionMarker marker in markers)
            {
                if (!marker.IsConfigured(out string reason))
                {
                    invalidMarkers++;
                    Debug.LogWarning(
                        $"[Traffic Junction] Marker '{marker.name}' is invalid: {reason}",
                        marker);
                    continue;
                }

                if (!markedLanes.Add(marker.Lane))
                {
                    invalidMarkers++;
                    Debug.LogWarning(
                        $"[Traffic Junction] Lane '{marker.Lane.LaneDisplayName}' has more than one marker on this junction.",
                        marker);
                    continue;
                }

                validMarkers++;
            }

            foreach (TrafficJunctionMovement movement in movements)
            {
                if (!movement.IsConfigured(out string reason))
                {
                    invalidMovements++;
                    Debug.LogWarning(
                        $"[Traffic Junction] Movement '{movement.name}' is invalid: {reason}",
                        movement);
                    continue;
                }

                bool hasIncomingMarker = HasValidMarkerForLane(markers, movement.IncomingLane);
                bool hasOutgoingMarker = HasValidMarkerForLane(markers, movement.OutgoingLane);

                if (!hasIncomingMarker || !hasOutgoingMarker)
                {
                    invalidMovements++;
                    string missing = !hasIncomingMarker && !hasOutgoingMarker
                        ? "incoming and outgoing"
                        : !hasIncomingMarker ? "incoming" : "outgoing";

                    Debug.LogWarning(
                        $"[Traffic Junction] Movement '{movement.DisplayName}' has no valid junction marker for its {missing} lane.",
                        movement);
                    continue;
                }

                validMovements++;
            }

            Debug.Log(
                $"[Traffic Junction] '{junctionDisplayName}' validation complete. " +
                $"Markers: {validMarkers} valid, {invalidMarkers} invalid. " +
                $"Movements: {validMovements} valid, {invalidMovements} invalid. " +
                "Straight-ahead travel along continuous road splines is not counted as a separate turning movement.",
                this);
        }

        private bool HasValidMarkerForLane(
            TrafficJunctionMarker[] markers,
            TrafficLaneSpline lane)
        {
            foreach (TrafficJunctionMarker marker in markers)
            {
                if (marker != null &&
                    marker.Junction == this &&
                    marker.Lane == lane &&
                    marker.IsConfigured(out _))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            gizmoSize = Mathf.Max(0.1f, gizmoSize);
        }

        private void OnDrawGizmos()
        {
            if (!showJunctionGizmo)
                return;

            Gizmos.color = new Color(1f, 0.65f, 0.1f, 1f);
            Gizmos.DrawWireSphere(transform.position, gizmoSize);
            Gizmos.DrawLine(
                transform.position,
                transform.position + Vector3.up * gizmoSize * 1.5f);
        }
    }
}