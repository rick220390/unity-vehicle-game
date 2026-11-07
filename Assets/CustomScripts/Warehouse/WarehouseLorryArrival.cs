using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns an independent tractor and one random independent trailer prefab,
/// couples them at FithWheelPivot/kingpinPivot, and drives the tractor using
/// WheelColliders along the existing warehouse route.
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseLorryArrival : MonoBehaviour
{
    [System.Serializable]
    public class RoutePoint
    {
        [Tooltip("Scene waypoint the lorry should reach.")]
        public Transform target;
        [Tooltip("Enable for a reversing leg.")]
        public bool reverse;
    }

    [Header("Separate vehicle prefabs")]
    [SerializeField] private GameObject tractorPrefab;
    [Tooltip("Each entry is a separate trailer prefab. The trailer must have a Rigidbody, WheelColliders and a child named kingpinPivot.")]
    [SerializeField] private GameObject[] trailerPrefabs;
    [SerializeField] private Transform spawnPoint;

    [Header("Attachment points")]
    [Tooltip("Exact child name on the tractor prefab.")]
    [SerializeField] private string fifthWheelPivotName = "FithWheelPivot";
    [Tooltip("Exact child name on each trailer prefab.")]
    [SerializeField] private string kingpinPivotName = "kingpinPivot";

    [Header("Arrival route")]
    [SerializeField] private List<RoutePoint> route = new List<RoutePoint>();
    [SerializeField, Min(0.1f)] private float drivingSpeedKph = 12f;
    [SerializeField, Min(0.01f)] private float arrivalDistance = 0.8f;
    [SerializeField, Min(0f)] private float pauseAtEachPoint = 0.15f;
    [SerializeField, Min(0.1f)] private float stopSpeedKph = 1.5f;
    [SerializeField, Min(0.1f)] private float waypointTimeout = 120f;

    [Header("Runtime")]
    [SerializeField] private bool logArrival = true;

    private GameObject spawnedVehicleRoot;
    private GameObject spawnedTractor;
    private GameObject spawnedTrailer;
    private WarehouseTruckPhysicsController truckController;
    private Coroutine arrivalRoutine;
    private bool hasArrived;

    public GameObject SpawnedLorry => spawnedVehicleRoot;
    public GameObject SpawnedTractor => spawnedTractor;
    public GameObject SpawnedTrailer => spawnedTrailer;
    public bool HasArrived => hasArrived;

    public void StartArrival()
    {
        if (arrivalRoutine != null)
            StopCoroutine(arrivalRoutine);

        hasArrived = false;

        if (tractorPrefab == null)
        {
            Debug.LogError("WarehouseLorryArrival: Assign the separate Tractor Prefab.", this);
            return;
        }
        if (trailerPrefabs == null || trailerPrefabs.Length == 0)
        {
            Debug.LogError("WarehouseLorryArrival: Add at least one separate trailer prefab.", this);
            return;
        }
        if (spawnPoint == null || route == null || route.Count == 0)
        {
            Debug.LogError("WarehouseLorryArrival: Assign a Spawn Point and at least one route point.", this);
            return;
        }

        List<GameObject> availableTrailers = new List<GameObject>();
        foreach (GameObject prefab in trailerPrefabs)
            if (prefab != null) availableTrailers.Add(prefab);

        if (availableTrailers.Count == 0)
        {
            Debug.LogError("WarehouseLorryArrival: No trailer prefabs are assigned.", this);
            return;
        }

        GameObject selectedTrailer = availableTrailers[Random.Range(0, availableTrailers.Count)];
        RemoveLorry();

        spawnedVehicleRoot = new GameObject("WarehouseArticulatedLorry_Runtime");
        spawnedVehicleRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        spawnedTractor = Instantiate(tractorPrefab, spawnPoint.position, spawnPoint.rotation);
        spawnedTractor.name = tractorPrefab.name + "_Tractor";
        spawnedTractor.transform.SetParent(spawnedVehicleRoot.transform, true);

        spawnedTrailer = Instantiate(selectedTrailer, spawnPoint.position, spawnPoint.rotation);
        spawnedTrailer.name = selectedTrailer.name + "_Trailer";
        spawnedTrailer.transform.SetParent(spawnedVehicleRoot.transform, true);

        // The tractor's main Rigidbody must live on the tractor prefab root so
        // the AI controller, wheel colliders and fifth-wheel joint share one body.
        Rigidbody tractorBody = spawnedTractor.GetComponent<Rigidbody>();
        Rigidbody trailerBody = spawnedTrailer.GetComponent<Rigidbody>();
        if (trailerBody == null)
        {
            Debug.LogError("WarehouseLorryArrival: The trailer prefab needs its own Rigidbody on its root object.", spawnedTrailer);
            RemoveLorry();
            return;
        }

        Transform fifthWheelPivot = FindChild(spawnedTractor.transform, fifthWheelPivotName);
        Transform kingpinPivot = FindChild(spawnedTrailer.transform, kingpinPivotName);

        if (tractorBody == null || fifthWheelPivot == null || kingpinPivot == null)
        {
            Debug.LogError(
                "WarehouseLorryArrival: Could not prepare coupling. Tractor Rigidbody=" + (tractorBody != null) +
                ". Tractor root Rigidbody=" + (tractorBody != null) +
                ", " + fifthWheelPivotName + "=" + (fifthWheelPivot != null) +
                ", trailer Rigidbody=" + (trailerBody != null) +
                ", " + kingpinPivotName + "=" + (kingpinPivot != null) + ".", this);
            RemoveLorry();
            return;
        }

        // Position the separate trailer so the two coupling markers coincide.
        spawnedTrailer.transform.rotation = spawnedTractor.transform.rotation;
        Physics.SyncTransforms();
        spawnedTrailer.transform.position += fifthWheelPivot.position - kingpinPivot.position;
        Physics.SyncTransforms();

        tractorBody.isKinematic = false;
        tractorBody.useGravity = true;
        tractorBody.interpolation = RigidbodyInterpolation.Interpolate;
        tractorBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        truckController = spawnedTractor.GetComponent<WarehouseTruckPhysicsController>();
        if (truckController == null)
            truckController = spawnedTractor.AddComponent<WarehouseTruckPhysicsController>();

        ConfigurableJoint joint;
        if (!WarehouseTruckTrailerCoupler.Connect(tractorBody, fifthWheelPivot, trailerBody, kingpinPivot, out joint))
        {
            RemoveLorry();
            return;
        }

        // Secure every cargo Rigidbody immediately. Cargo must remain fixed to
        // the trailer throughout the journey; the forklift unloader releases
        // one pallet root at a time after the lorry has parked.
        LockCargoForUnloading();

        if (logArrival)
            Debug.Log("WarehouseLorryArrival: Spawned tractor '" + tractorPrefab.name +
                      "' with trailer '" + selectedTrailer.name + "', connected the fifth wheel and secured trailer cargo.", this);

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
                StopVehicle();
                arrivalRoutine = null;
                yield break;
            }

            truckController.DriveTo(point.target, point.reverse, drivingSpeedKph);
            float elapsed = 0f;

            while (spawnedTractor != null &&
                   HorizontalDistance(spawnedTractor.transform.position, point.target.position) > arrivalDistance)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= waypointTimeout)
                {
                    Debug.LogError("WarehouseLorryArrival: Timed out driving to route point " + i + ".", this);
                    StopVehicle();
                    arrivalRoutine = null;
                    yield break;
                }
                yield return null;
            }

            if (spawnedTractor == null)
            {
                arrivalRoutine = null;
                yield break;
            }

            truckController.StopDriving();
            float settleTimer = 0f;
            while (truckController != null && truckController.SpeedKph > stopSpeedKph && settleTimer < 8f)
            {
                settleTimer += Time.deltaTime;
                yield return null;
            }

            if (pauseAtEachPoint > 0f)
                yield return new WaitForSeconds(pauseAtEachPoint);
        }

        StopVehicle();
        hasArrived = true;
        LockCargoForUnloading();
        arrivalRoutine = null;

        if (logArrival)
            Debug.Log("WarehouseLorryArrival: Articulated lorry has reached the loading bay and is ready for unloading.", this);
    }

    private void StopVehicle()
    {
        if (truckController != null)
            truckController.StopDriving();
    }

    private void LockCargoForUnloading()
    {
        if (spawnedTrailer == null)
            return;

        // Only lock pallet bodies inside the dedicated Load container.
        // Never touch trailer wheels, suspension, chassis or coupling bodies.
        Transform load = FindChild(spawnedTrailer.transform, "Load");
        if (load == null)
        {
            Debug.LogWarning("WarehouseLorryArrival: No child named 'Load' found on the spawned trailer. Cargo was not locked.", spawnedTrailer);
            return;
        }

        Rigidbody[] loadBodies = load.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in loadBodies)
        {
            if (body == null || body.transform == load)
                continue;

            // Only process the outermost Rigidbody in each cargo branch
            // (normally the pallet root); ignore nested box/prop Rigidbodies.
            Transform parent = body.transform.parent;
            bool hasCargoRigidbodyParent = false;
            while (parent != null && parent != load)
            {
                if (parent.GetComponent<Rigidbody>() != null)
                {
                    hasCargoRigidbodyParent = true;
                    break;
                }
                parent = parent.parent;
            }

            if (hasCargoRigidbodyParent)
                continue;

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }
    }

    private static Transform FindChild(Transform root, string exactName)
    {
        if (root == null) return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == exactName) return child;
        return null;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public void RemoveLorry()
    {
        if (arrivalRoutine != null)
        {
            StopCoroutine(arrivalRoutine);
            arrivalRoutine = null;
        }

        StopVehicle();

        if (spawnedVehicleRoot != null)
            Destroy(spawnedVehicleRoot);
        else
        {
            if (spawnedTractor != null) Destroy(spawnedTractor);
            if (spawnedTrailer != null) Destroy(spawnedTrailer);
        }

        spawnedVehicleRoot = null;
        spawnedTractor = null;
        spawnedTrailer = null;
        truckController = null;
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

        if (route == null) return;
        Vector3 previous = spawnPoint != null ? spawnPoint.position : transform.position;
        foreach (RoutePoint point in route)
        {
            if (point == null || point.target == null) continue;
            Gizmos.color = point.reverse ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(point.target.position, 0.35f);
            Gizmos.DrawLine(previous, point.target.position);
            previous = point.target.position;
        }
    }
}