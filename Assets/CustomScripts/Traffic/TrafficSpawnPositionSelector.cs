using UnityEngine;
using UnityEngine.Splines;

namespace CustomTraffic
{
    /// <summary>
    /// Finds a random position on a directional road lane near the player.
    /// This component only selects positions; it does not create vehicles.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficSpawnPositionSelector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform player;
        [SerializeField] private Camera playerCamera;

        [Header("Player Area")]
        [SerializeField, Min(1f)] private float activeRadius = 100f;
        [SerializeField, Min(0f)] private float minimumPlayerDistance = 20f;

        [Header("Lane Position")]
        [Tooltip("Avoids selecting the very beginning or end of a lane spline.")]
        [SerializeField, Range(0f, 0.45f)] private float laneEndMargin = 0.1f;

        [Header("Visibility")]
        [SerializeField] private bool rejectVisiblePositions = true;
        [Tooltip("Only colliders on these layers can hide a spawn position from the camera.")]
        [SerializeField] private LayerMask obstructionMask = ~0;
        [SerializeField, Min(0f)] private float visibilityTargetHeight = 1f;

        [Header("Search")]
        [SerializeField, Min(1)] private int searchAttempts = 80;

        [Header("Debug")]
        [SerializeField] private bool drawLastFoundPosition = true;
        [SerializeField, Min(0.1f)] private float gizmoSize = 1f;

        private TrafficLaneSpline[] cachedLanes;
        private bool hasLastFoundPosition;
        private Vector3 lastFoundPosition;
        private Vector3 lastFoundDirection;

        /// <summary>
        /// Attempts to find a random valid position on a road lane near the player.
        /// The returned T value is the normalized position along the lane spline.
        /// </summary>
        public bool TryFindSpawnPosition(
            out TrafficLaneSpline selectedLane,
            out float selectedT,
            out Vector3 spawnPosition,
            out Quaternion spawnRotation)
        {
            selectedLane = null;
            selectedT = 0f;
            spawnPosition = Vector3.zero;
            spawnRotation = Quaternion.identity;
            hasLastFoundPosition = false;

            if (player == null)
            {
                Debug.LogWarning("[Traffic Spawn Selector] Player reference is missing.", this);
                return false;
            }

            if (playerCamera == null)
                playerCamera = Camera.main;

            if (playerCamera == null)
            {
                Debug.LogWarning("[Traffic Spawn Selector] Player camera is missing.", this);
                return false;
            }

            if (cachedLanes == null || cachedLanes.Length == 0)
                RefreshLaneCache();

            if (cachedLanes == null || cachedLanes.Length == 0)
            {
                Debug.LogWarning("[Traffic Spawn Selector] No active TrafficLaneSpline components were found.", this);
                return false;
            }

            for (int attempt = 0; attempt < searchAttempts; attempt++)
            {
                TrafficLaneSpline lane = cachedLanes[Random.Range(0, cachedLanes.Length)];

                if (!IsUsableLane(lane))
                    continue;

                float t = Random.Range(laneEndMargin, 1f - laneEndMargin);

                Vector3 position = EvaluateLanePosition(lane, t);
                Vector3 direction = EvaluateLaneDirection(lane, t);

                if (direction.sqrMagnitude < 0.001f)
                    continue;

                float distanceFromPlayer = Vector3.Distance(player.position, position);

                if (distanceFromPlayer < minimumPlayerDistance ||
                    distanceFromPlayer > activeRadius)
                    continue;

                if (rejectVisiblePositions && IsVisibleFromPlayerCamera(position))
                    continue;

                selectedLane = lane;
                selectedT = t;
                spawnPosition = position;
                spawnRotation = Quaternion.LookRotation(direction, Vector3.up);

                lastFoundPosition = position;
                lastFoundDirection = direction;
                hasLastFoundPosition = true;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Rebuilds the lane cache. Call this if road lanes are created at runtime.
        /// </summary>
        [ContextMenu("Refresh Lane Cache")]
        public void RefreshLaneCache()
        {
            TrafficLaneSpline[] foundLanes = FindObjectsOfType<TrafficLaneSpline>(true);

            int activeCount = 0;

            for (int i = 0; i < foundLanes.Length; i++)
            {
                if (IsUsableLane(foundLanes[i]))
                    activeCount++;
            }

            cachedLanes = new TrafficLaneSpline[activeCount];
            int index = 0;

            for (int i = 0; i < foundLanes.Length; i++)
            {
                if (IsUsableLane(foundLanes[i]))
                    cachedLanes[index++] = foundLanes[i];
            }

            Debug.Log("[Traffic Spawn Selector] Cached " + cachedLanes.Length + " active road lanes.", this);
        }

        private bool IsUsableLane(TrafficLaneSpline lane)
        {
            return lane != null &&
                lane.isActiveAndEnabled &&
                lane.SplineContainer != null &&
                lane.SplineContainer.Spline != null &&
                lane.SplineContainer.Spline.Count >= 2;
        }

        private Vector3 EvaluateLanePosition(TrafficLaneSpline lane, float t)
        {
            Unity.Mathematics.float3 point =
                SplineUtility.EvaluatePosition(lane.SplineContainer.Spline, t);

            Vector3 localPoint = new Vector3(point.x, point.y, point.z);
            return lane.SplineContainer.transform.TransformPoint(localPoint);
        }

        private Vector3 EvaluateLaneDirection(TrafficLaneSpline lane, float t)
        {
            Unity.Mathematics.float3 tangent =
                SplineUtility.EvaluateTangent(lane.SplineContainer.Spline, t);

            Vector3 localTangent = new Vector3(tangent.x, tangent.y, tangent.z);
            return lane.SplineContainer.transform.TransformDirection(localTangent).normalized;
        }

        private bool IsVisibleFromPlayerCamera(Vector3 roadPosition)
        {
            Vector3 targetPosition = roadPosition + Vector3.up * visibilityTargetHeight;
            Vector3 viewportPosition = playerCamera.WorldToViewportPoint(targetPosition);

            bool insideCameraView =
                viewportPosition.z > 0f &&
                viewportPosition.x >= 0f &&
                viewportPosition.x <= 1f &&
                viewportPosition.y >= 0f &&
                viewportPosition.y <= 1f;

            // Outside the camera's view frustum: it cannot currently be seen.
            if (!insideCameraView)
                return false;

            // Inside the view: accept it only if something on the obstruction
            // layers blocks the camera's line of sight to the candidate.
            Vector3 origin = playerCamera.transform.position;
            Vector3 toTarget = targetPosition - origin;
            float distance = toTarget.magnitude;

            if (distance <= 0.01f)
                return true;

            bool blocked = Physics.Raycast(
                origin,
                toTarget / distance,
                distance,
                obstructionMask,
                QueryTriggerInteraction.Ignore);

            return !blocked;
        }

        [ContextMenu("Test Find Spawn Position")]
        private void TestFindSpawnPosition()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Traffic Spawn Selector] Enter Play mode before running this test.", this);
                return;
            }

            if (TryFindSpawnPosition(
                out TrafficLaneSpline lane,
                out float t,
                out Vector3 position,
                out Quaternion rotation))
            {
                Debug.Log(
                    "[Traffic Spawn Selector] Found position on lane '" +
                    lane.LaneDisplayName + "' at T=" + t.ToString("F3") +
                    ". Position: " + position.ToString("F2") +
                    ". Facing: " + rotation.eulerAngles.ToString("F1"),
                    this);
            }
            else
            {
                Debug.LogWarning(
                    "[Traffic Spawn Selector] No suitable hidden road position found. " +
                    "Try increasing Search Attempts or Active Radius, or check the camera obstruction layers.",
                    this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawLastFoundPosition || !hasLastFoundPosition)
                return;

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(lastFoundPosition, gizmoSize * 0.35f);
            Gizmos.DrawLine(
                lastFoundPosition,
                lastFoundPosition + lastFoundDirection * gizmoSize * 2f);
        }
    }
}
