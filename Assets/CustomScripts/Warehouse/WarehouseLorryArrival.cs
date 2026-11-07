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
    private float lockedLorryY;

    public GameObject SpawnedLorry => spawnedLorry;
    public bool HasArrived => hasArrived;

    private static readonly string[] WheelNames =
    {
        "WheelBL1", "WheelBL2", "WheelBL3",
        "WheelBR1", "WheelBR2", "WheelBR3",
        "WheelFL1", "WheelFL2", "WheelFR1", "WheelF2"
    };

    private void LateUpdate()
    {
        if (!hasArrived)
            EnforceLorryRigidbodies();
    }

    private void EnforceLorryRigidbodies()
    {
        if (spawnedLorry == null)
            return;

        Rigidbody[] bodies = spawnedLorry.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in bodies)
        {
            if (body == null)
                continue;

            body.useGravity = false;
            body.isKinematic = true;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
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

        bool sourceIsSceneObject = lorryPrefab.scene.IsValid();

        // Raycast the actual ground below the spawn marker. The spawn marker's
        // Y is not necessarily the vehicle's ground height, and the model pivot
        // may be well above or below the bottoms of its wheels.
        float groundY = spawnPoint.position.y;
        Ray groundRay = new Ray(spawnPoint.position + Vector3.up * 100f, Vector3.down);
        if (Physics.Raycast(groundRay, out RaycastHit groundHit, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            groundY = groundHit.point.y;
        }
        else
        {
            Debug.LogWarning("WarehouseLorryArrival: No ground was found below the Spawn Point. Check that the loading-yard ground has a collider; using the marker height as fallback.", this);
        }

        if (sourceIsSceneObject)
            lorryPrefab.SetActive(false);

        Vector3 spawnPosition = new Vector3(spawnPoint.position.x, groundY, spawnPoint.position.z);
        spawnedLorry = Instantiate(lorryPrefab, spawnPosition, spawnPoint.rotation);
        spawnedLorry.name = lorryPrefab.name + "_WarehouseDelivery";
        spawnedLorry.SetActive(true);

        // Align the lowest rendered part of the complete lorry/trailer (normally
        // the tyre bottoms) with the ground, compensating for the prefab pivot.
        Renderer[] renderers = spawnedLorry.GetComponentsInChildren<Renderer>(true);
        bool foundRenderer = false;
        Bounds vehicleBounds = new Bounds(spawnedLorry.transform.position, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!foundRenderer)
            {
                vehicleBounds = renderer.bounds;
                foundRenderer = true;
            }
            else
            {
                vehicleBounds.Encapsulate(renderer.bounds);
            }
        }

        if (foundRenderer)
        {
            Vector3 alignedPosition = spawnedLorry.transform.position;
            alignedPosition.y += groundY - vehicleBounds.min.y;
            spawnedLorry.transform.position = alignedPosition;
        }

        lockedLorryY = spawnedLorry.transform.position.y;

        Rigidbody[] bodies = spawnedLorry.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in bodies)
        {
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

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
                   HorizontalDistance(spawnedLorry.transform.position, point.target.position) > arrivalDistance)
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
                Vector3 movedPosition = truck.position + travelDirection * step;
                movedPosition.y = lockedLorryY;
                truck.position = movedPosition;

                if (animateWheels)
                    RotateWheels(step, point.reverse);

                yield return null;
            }

            if (spawnedLorry == null)
            {
                arrivalRoutine = null;
                yield break;
            }

            Vector3 finalPosition = point.target.position;
            finalPosition.y = lockedLorryY;
            spawnedLorry.transform.position = finalPosition;

            Quaternion finalRotation = Quaternion.Euler(0f, point.target.eulerAngles.y, 0f);
            while (spawnedLorry != null && Quaternion.Angle(spawnedLorry.transform.rotation, finalRotation) > 0.25f)
            {
                spawnedLorry.transform.rotation = Quaternion.RotateTowards(
                    spawnedLorry.transform.rotation, finalRotation, turningSpeed * Time.deltaTime);
                yield return null;
            }

            if (spawnedLorry != null)
                spawnedLorry.transform.rotation = finalRotation;

            if (pauseAtEachPoint > 0f)
                yield return new WaitForSeconds(pauseAtEachPoint);
        }

        hasArrived = true;
        arrivalRoutine = null;
        LockCargoForUnloading();

        if (logArrival)
            Debug.Log("WarehouseLorryArrival: Lorry has reached the loading bay and is ready for unloading.", this);
    }

    private void LockCargoForUnloading()
    {
        if (spawnedLorry == null)
            return;

        Transform trailer = FindChildByName(spawnedLorry.transform, "BigTruck0102Trailer");
        if (trailer == null)
        {
            Debug.LogError("WarehouseLorryArrival: Could not find BigTruck0102Trailer. Trailer cargo remains locked.", this);
            return;
        }

        Rigidbody[] bodies = trailer.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in bodies)
        {
            if (body == null || body.transform == trailer)
                continue;

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }
    }

    private static Transform FindChildByName(Transform root, string exactName)
    {
        if (root == null)
            return null;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == exactName)
                return child;
        }

        return null;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void ResolveWheelMeshes()
    {
        if (spawnedLorry == null)
            return;

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