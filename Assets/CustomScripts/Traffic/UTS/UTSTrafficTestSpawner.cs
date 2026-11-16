using UnityEngine;

namespace CustomTraffic.UTS
{
    /// <summary>
    /// Spawns one existing UTS car prefab at the beginning of a custom traffic
    /// lane and gives it the sampled WalkPath adapter.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrafficSplineWalkPath))]
    public class UTSTrafficTestSpawner : MonoBehaviour
    {
        [Header("Test Vehicle")]
        [SerializeField] private GameObject vehiclePrefab;
        [SerializeField, Min(0.1f)] private float moveSpeed = 8f;
        [SerializeField, Min(0.1f)] private float acceleration = 2f;
        [SerializeField, Min(0.1f)] private float deceleration = 2f;
        [SerializeField, Min(1f)] private float followingDistance = 8f;
        [SerializeField, Min(0.5f)] private float waypointThreshold = 2f;
        [SerializeField] private bool spawnOnStart = true;

        private void Start()
        {
            if (spawnOnStart)
                SpawnOneTestVehicle();
        }

        [ContextMenu("Spawn One UTS Test Vehicle")]
        public void SpawnOneTestVehicle()
        {
            if (vehiclePrefab == null)
            {
                Debug.LogError("[UTS Traffic Test] Assign an existing UTS car prefab.", this);
                return;
            }

            TrafficSplineWalkPath path = GetComponent<TrafficSplineWalkPath>();
            Vector3 start = path.getNextPoint(0, 1);
            Vector3 next = path.getNextPoint(0, 2);

            if (Vector3.Distance(start, next) < 0.01f)
            {
                Debug.LogError("[UTS Traffic Test] The lane adapter has no usable sampled points.", this);
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(next - start, Vector3.up);
            GameObject vehicle = Instantiate(vehiclePrefab, start, rotation);

            // MovePath must exist and be assigned before CarAIController.Awake runs.
            MovePath movePath = vehicle.AddComponent<MovePath>();
            movePath.walkPath = path;
            movePath._walkPointThreshold = waypointThreshold;
            movePath.InitStartPosition(0, 1, true, true);
            movePath.nextFinishPos = path.getNextPoint(0, 3);

            CarAIController ai = vehicle.AddComponent<CarAIController>();
            ai.GetBoxSize();
            ai.MOVE_SPEED = moveSpeed;
            ai.INCREASE = acceleration;
            ai.DECREASE = deceleration;
            ai.TO_CAR = followingDistance;
            ai.TO_SEMAPHORE = followingDistance;
            ai.MaxAngle = 8f;

            movePath.SetLookPosition();

            Debug.Log("[UTS Traffic Test] Spawned one UTS vehicle on lane '" +
                      GetComponent<TrafficLaneSpline>().LaneDisplayName +
                      "'. It will loop at the end of the spline; stop Play Mode after it passes the test section.", vehicle);
        }
    }
}