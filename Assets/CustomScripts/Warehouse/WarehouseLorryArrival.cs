using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns an atmospheric delivery lorry and drives it through a sequence of
/// scene waypoints. Mark the final loading-bay leg as reverse in the inspector.
/// This is a transform-driven AI vehicle; it does not require WheelColliders.
/// </summary>
[DisallowMultipleComponent]
public class WarehouseLorryArrival : MonoBehaviour
{
    [System.Serializable]
    public class RoutePoint
    {
        [Tooltip("Scene waypoint the lorry should reach.")]
        public Transform target;

        [Tooltip("Enable for a reversing leg. The lorry will face away from its direction of travel.")]
        public bool reverse;
    }

    [Header("Lorry prefab")]
    [SerializeField] private GameObject lorryPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private bool spawnOnStart = true;

    [Header("Arrival route")]
    [Tooltip("Add route points in travel order. Use forward points to approach the bay, then a reverse point for the final backing manoeuvre.")]
    [SerializeField] private List<RoutePoint> route = new List<RoutePoint>();
    [SerializeField, Min(0.1f)] private float drivingSpeed = 5f;
    [SerializeField, Min(0.1f)] private float turningSpeed = 120f;
    [SerializeField, Min(0.01f)] private float arrivalDistance = 0.15f;
    [SerializeField, Min(0f)] private float pauseAtEachPoint = 0.15f;

    [Header("Visible wheel animation")]
    [Tooltip("Leave empty to find the named wheel meshes automatically under the spawned lorry.")]
    [SerializeField] private List<Transform> wheelMeshes = new List<Transform>();
    [SerializeField, Min(0.05f)] private float wheelRadius = 0.5f;
    [Tooltip("Local axle direction of the wheel meshes. Usually X for this lorry model.")]
    [SerializeField] private Vector3 wheelAxleLocalAxis = Vector3.right;
    [SerializeField] private bool animateWheels = true;

    [Header("Runtime")]
    [SerializeField] private bool logArrival = true;

    private GameObject spawnedLorry;
    private Coroutine arrivalRoutine;
    private bool hasArrived;

    public GameObject SpawnedLorry => spawnedLorry;
    public bool HasArrived => hasArrived;

    private static readonly string[] WheelNames =
    {
        "WheelBL1", "WheelBL2", "WheelBL3",
        "WheelBR1", "WheelBR2", "WheelBR3",
        "WheelFL1", "WheelFL2", "WheelFR1", "WheelF2"
    };

    private void Start()
    {
        if (spawnOnStart)
            StartArrival();
    }

    public void StartArrival()
    {
        if (arrivalRoutine != null)
            StopCoroutine(arrivalRoutine);

        hasArrived = false;

        if (lorryPrefab == null)
        {
            Debug.LogError("WarehouseLorryArrival: Assign the BigTruck01_Open prefab.", this);
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError("WarehouseLorryArrival: Assign a Spawn Point transform.", this);
            return;
        }

        if (route == null || route.Count == 0)
        {
            Debug.LogError("WarehouseLorryArrival: Add at least one route point.", this);
            return;
        }

        if (spawnedLorry != null)
            Destroy(spawnedLorry);

        // Support either a Project prefab or a complete lorry already placed
        // in the scene. If the source is a scene object, hide it before cloning
        // so only the spawned delivery lorry is visible during play.
        bool sourceIsSceneObject = lorryPrefab.scene.IsValid();
        if (sourceIsSceneObject)
            lorryPrefab.SetActive(false);

        spawnedLorry = Instantiate(lorryPrefab, spawnPoint.position, spawnPoint.rotation);
        spawnedLorry.name = lorryPrefab.name + "_WarehouseDelivery";
        spawnedLorry.SetActive(true);
        ResolveWheelMeshes();

        arrivalRoutine = StartCoroutine(DriveRoute());
    }

    private IEnumerator DriveRoute()
    {
        for (int i = 0; i < route.Count; i++)
        {
            RoutePoint point = route[i];
            if (point == null || point.target == null)
            {
                Debug.LogError("WarehouseLorryArrival: Route point " + i + " has no target assigned.", this);
                arrivalRoutine = null;
                yield break;
            }

            while (spawnedLorry != null &&
                   Vector3.Distance(spawnedLorry.transform.position, point.target.position) > arrivalDistance)
            {
                Transform truck = spawnedLorry.transform;
                Vector3 toTarget = point.target.position - truck.position;
                toTarget.y = 0f;

                if (toTarget.sqrMagnitude < 0.0001f)
                    break;

                Vector3 travelDirection = toTarget.normalized;
                Vector3 facingDirection = point.reverse ? -travelDirection : travelDirection;
                Quaternion desiredRotation = Quaternion.LookRotation(facingDirection, Vector3.up);

                truck.rotation = Quaternion.RotateTowards(
                    truck.rotation, desiredRotation, turningSpeed * Time.deltaTime);

                float step = Mathf.Min(drivingSpeed * Time.deltaTime, toTarget.magnitude);
                truck.position += travelDirection * step;

                if (animateWheels)
                    RotateWheels(step, point.reverse);

                yield return null;
            }

            if (spawnedLorry == null)
            {
                arrivalRoutine = null;
                yield break;
            }

            // Snap only the final small positional remainder; preserve the lorry's
            // heading so the reverse manoeuvre finishes facing the correct way.
            Vector3 finalPosition = point.target.position;
            finalPosition.y = spawnedLorry.transform.position.y;
            spawnedLorry.transform.position = finalPosition;

            if (pauseAtEachPoint > 0f)
                yield return new WaitForSeconds(pauseAtEachPoint);
        }

        hasArrived = true;
        arrivalRoutine = null;

        if (logArrival)
            Debug.Log("WarehouseLorryArrival: Lorry has reached the loading bay and is ready for unloading.", this);
    }

    private void ResolveWheelMeshes()
    {
        if (spawnedLorry == null)
            return;

        // If wheel references were assigned in the scene inspector, they belong
        // to the scene and cannot reference the newly-instantiated prefab. Find
        // the corresponding child meshes by their exact model names instead.
        wheelMeshes.Clear();

        Transform[] children = spawnedLorry.GetComponentsInChildren<Transform>(true);
        foreach (string wheelName in WheelNames)
        {
            foreach (Transform child in children)
            {
                if (child.name == wheelName)
                {
                    wheelMeshes.Add(child);
                    break;
                }
            }
        }

        if (animateWheels && wheelMeshes.Count == 0)
            Debug.LogWarning("WarehouseLorryArrival: No wheel meshes found. Check the wheel child names.", this);
    }

    private void RotateWheels(float distance, bool reversing)
    {
        if (wheelMeshes.Count == 0 || wheelRadius <= 0f)
            return;

        float degrees = (distance / wheelRadius) * Mathf.Rad2Deg;
        if (reversing)
            degrees = -degrees;

        Vector3 axis = wheelAxleLocalAxis.sqrMagnitude > 0.0001f
            ? wheelAxleLocalAxis.normalized
            : Vector3.right;

        foreach (Transform wheel in wheelMeshes)
        {
            if (wheel != null)
                wheel.Rotate(axis, degrees, Space.Self);
        }
    }

    public void RemoveLorry()
    {
        if (arrivalRoutine != null)
        {
            StopCoroutine(arrivalRoutine);
            arrivalRoutine = null;
        }

        if (spawnedLorry != null)
            Destroy(spawnedLorry);

        spawnedLorry = null;
        hasArrived = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (spawnPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.5f);
            Gizmos.DrawLine(spawnPoint.position, spawnPoint.position + spawnPoint.forward * 2f);
        }

        if (route == null)
            return;

        Vector3 previous = spawnPoint != null ? spawnPoint.position : transform.position;
        for (int i = 0; i < route.Count; i++)
        {
            if (route[i] == null || route[i].target == null)
                continue;

            Gizmos.color = route[i].reverse ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(route[i].target.position, 0.35f);
            Gizmos.DrawLine(previous, route[i].target.position);
            previous = route[i].target.position;
        }
    }
}
