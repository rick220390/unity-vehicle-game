using UnityEngine;
using WSMGameStudio.HeavyMachinery;

/// <summary>
/// A reusable warehouse rack position for one complete pallet.
/// Add this to a shelf-slot object with a trigger collider, then assign the
/// pallet placement point and forklift controller in the Inspector.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class WarehousePalletStorageSlot : MonoBehaviour
{
    [Header("Slot setup")]
    [SerializeField] private BoxCollider triggerZone;
    [SerializeField] private Transform placementPoint;

    [Header("Forklift clearance")]
    [SerializeField] private ForkliftController forklift;
    [Tooltip("Extra space around the pallet bounds that the fork transforms must clear before the pallet is stored.")]
    [SerializeField, Min(0f)] private float forkClearancePadding = 0.08f;

    [Header("Storage stability")]
    [SerializeField, Min(0.05f)] private float stableSpeedThreshold = 0.08f;
    [SerializeField, Min(0.05f)] private float stableAngularSpeedThreshold = 0.15f;
    [SerializeField, Min(0f)] private float stableTimeRequired = 0.4f;

    private Rigidbody candidate;
    private float stableTimer;
    private bool occupied;

    public bool IsOccupied => occupied;
    public Rigidbody StoredPallet => occupied ? candidate : null;

    private void Reset()
    {
        triggerZone = GetComponent<BoxCollider>();
        if (triggerZone != null)
            triggerZone.isTrigger = true;
    }

    private void Awake()
    {
        if (triggerZone == null)
            triggerZone = GetComponent<BoxCollider>();

        if (triggerZone == null || !triggerZone.isTrigger)
        {
            Debug.LogError("WarehousePalletStorageSlot: Assign a BoxCollider configured as Is Trigger.", this);
            enabled = false;
            return;
        }

        if (placementPoint == null)
        {
            Debug.LogError("WarehousePalletStorageSlot: Assign the pallet placement point.", this);
            enabled = false;
            return;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TrySelectCandidate(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!occupied)
            TrySelectCandidate(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (occupied || candidate == null)
            return;

        Rigidbody body = other.attachedRigidbody;
        if (body == candidate)
        {
            candidate = null;
            stableTimer = 0f;
        }
    }

    private void Update()
    {
        if (occupied || candidate == null)
            return;

        if (candidate == null)
        {
            ClearCandidate();
            return;
        }

        if (!IsForkliftClearOf(candidate))
        {
            stableTimer = 0f;
            return;
        }

        if (candidate.velocity.sqrMagnitude > stableSpeedThreshold * stableSpeedThreshold ||
            candidate.angularVelocity.sqrMagnitude > stableAngularSpeedThreshold * stableAngularSpeedThreshold)
        {
            stableTimer = 0f;
            return;
        }

        stableTimer += Time.deltaTime;
        if (stableTimer >= stableTimeRequired)
            StoreCandidate();
    }

    private void TrySelectCandidate(Collider other)
    {
        if (occupied || candidate != null)
            return;

        Rigidbody body = other.attachedRigidbody;
        if (body == null || body.isKinematic)
            return;

        // The complete pallet is expected to have its own Rigidbody.
        // Child box colliders resolve to that same attached Rigidbody.
        candidate = body;
        stableTimer = 0f;
        Debug.Log("WarehousePalletStorageSlot: Pallet candidate entered slot '" + name + "'.", candidate);
    }

    private bool IsForkliftClearOf(Rigidbody pallet)
    {
        if (forklift == null)
            return true;

        if (forklift.fork_L == null || forklift.fork_R == null)
            return true;

        Bounds bounds = GetPalletBounds(pallet);
        bounds.Expand(forkClearancePadding * 2f);

        return !bounds.Contains(forklift.fork_L.transform.position) &&
               !bounds.Contains(forklift.fork_R.transform.position);
    }

    private static Bounds GetPalletBounds(Rigidbody pallet)
    {
        Collider[] colliders = pallet.GetComponentsInChildren<Collider>();
        Bounds bounds = new Bounds(pallet.worldCenterOfMass, Vector3.zero);
        bool hasBounds = false;

        foreach (Collider item in colliders)
        {
            if (item == null || item.isTrigger)
                continue;

            if (!hasBounds)
            {
                bounds = item.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(item.bounds);
            }
        }

        return bounds;
    }

    private void StoreCandidate()
    {
        if (candidate == null || placementPoint == null)
        {
            ClearCandidate();
            return;
        }

        candidate.velocity = Vector3.zero;
        candidate.angularVelocity = Vector3.zero;
        candidate.transform.SetPositionAndRotation(placementPoint.position, placementPoint.rotation);
        candidate.useGravity = false;
        candidate.isKinematic = true;
        candidate.interpolation = RigidbodyInterpolation.None;
        candidate.transform.SetParent(transform, true);

        occupied = true;
        stableTimer = 0f;

        Debug.Log("WarehousePalletStorageSlot: Stored pallet '" + candidate.name + "' in slot '" + name + "'.", candidate);
    }

    private void ClearCandidate()
    {
        candidate = null;
        stableTimer = 0f;
    }

    /// <summary>
    /// Clears this slot's occupancy state. Retrieval behaviour will call this
    /// when the stored pallet is released by the forklift.
    /// </summary>
    public void ClearStoredPallet(Rigidbody pallet)
    {
        if (!occupied || candidate != pallet)
            return;

        occupied = false;
        ClearCandidate();
    }

    private void OnDrawGizmosSelected()
    {
        if (placementPoint != null)
        {
            Gizmos.color = occupied ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(placementPoint.position, 0.12f);
            Gizmos.DrawLine(placementPoint.position, placementPoint.position + placementPoint.forward * 0.5f);
        }
    }
}
