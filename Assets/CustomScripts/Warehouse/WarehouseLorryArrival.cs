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

    [Header("Random delivery lorries")]
    [Tooltip("Add each fully loaded delivery-truck prefab here. One valid entry is selected at random each time a warehouse job starts.")]
    [SerializeField] private GameObject[] lorryPrefabs;
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
    private float groundClearanceOffset;

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

        if (lorryPrefabs == null || lorryPrefabs.Length == 0)
        {
            Debug.LogError("WarehouseLorryArrival: Add at least one loaded delivery lorry prefab to the Random Delivery Lorries array.", this);
            return;
        }

        // Ignore empty Inspector slots so one unassigned entry cannot break delivery spawning.
        List<GameObject> availablePrefabs = new List<GameObject>();
        foreach (GameObject prefab in lorryPrefabs)
        {
            if (prefab != null)
                availablePrefabs.Add(prefab);
        }

        if (availablePrefabs.Count == 0)
        {
            Debug.LogError("WarehouseLorryArrival: The Random Delivery Lorries array contains no assigned prefabs.", this);
            return;
        }

        GameObject selectedLorryPrefab = availablePrefabs[Random.Range(0, availablePrefabs.Count)];
        if (logArrival)
            Debug.Log("WarehouseLorryArrival: Selected delivery load: " + selectedLorryPrefab.name, this);

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

        bool sourceIsSceneObject = selectedLorryPrefab.scene.IsValid();

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
            selectedLorryPrefab.SetActive(false);

        Vector3 spawnPosition = new Vector3(spawnPoint.position.x, groundY, spawnPoint.position.z);
        spawnedLorry = Instantiate(selectedLorryPrefab, spawnPosition, spawnPoint.rotation);
        spawnedLorry.name = selectedLorryPrefab.name + "_WarehouseDelivery";
        spawnedLorry.SetActive(true);

        // Ground the vehicle using its tyre meshes, not the bounds of the
        // complete loaded truck. Cargo, pallets and trailer contents can extend
        // below the tyre bounds and would incorrectly lift the whole vehicle.
        ResolveWheelMeshes();
        float lowestWheelY = float.PositiveInfinity;
        foreach (Transform wheel in wheelMeshes)
        {
            if (wheel == null)
                continue;

            Renderer[] wheelRenderers = wheel.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer wheelRenderer in wheelRenderers)
            {
                if (wheelRenderer != null)
                    lowestWheelY = Mathf.Min(lowestWheelY, wheelRenderer.bounds.min.y);
            }
        }

        if (float.IsPositiveInfinity(lowestWheelY))
        {
            Debug.LogWarning("WarehouseLorryArrival: Could not find renderers on the named wheel meshes. Falling back to the full vehicle bounds for grounding.", this);
            Renderer[] renderers = spawnedLorry.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    lowestWheelY = Mathf.Min(lowestWheelY, renderer.bounds.min.y);
            }
        }

        if (!float.IsPositiveInfinity(lowestWheelY))
        {
            Vector3 alignedPosition = spawnedLorry.transform.position;
            alignedPosition.y += groundY - lowestWheelY;
            spawnedLorry.transform.position = alignedPosition;
        }
        else
        {
            Debug.LogError("WarehouseLorryArrival: No renderers found to calculate the lorry ground height.", this);
        }

        lockedLorryY = spawnedLorry.transform.position.y;
        groundClearanceOffset = lockedLorryY - groundY;

        Rigidbody[] bodies = spawnedLorry.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in bodies)
        {
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

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
                FollowGround();

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
            FollowGround();

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

    // Re-sample the ground along the route. A single locked spawn height
    // causes the lorry to float or sink whenever the yard surface changes height.
    private void FollowGround()
    {
        if (spawnedLorry == null)
            return;

        Vector3 origin = spawnedLorry.transform.position + Vector3.up * 100f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 500f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.transform == spawnedLorry.transform ||
                hit.collider.transform.IsChildOf(spawnedLorry.transform))
                continue;

            Vector3 position = spawnedLorry.transform.position;
            position.y = hit.point.y + groundClearanceOffset;
            spawnedLorry.transform.position = position;
            lockedLorryY = position.y;
            return;
        }
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