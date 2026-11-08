using UnityEngine;
using WSMGameStudio.HeavyMachinery;

/// <summary>
/// One trailer pallet position. Add this to each empty pallet-position marker
/// beneath the trailer's Load object. Its trigger detects a loose pallet;
/// after the forklift withdraws and the pallet settles near the marker, it
/// snaps the pallet into place and makes it stationary for loading transport.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class WarehouseTrailerPalletSlot : MonoBehaviour
{
    [Header("Slot capture")]
    [SerializeField, Min(0.05f)] private float captureDistance = 0.75f;
    [SerializeField, Range(1f, 45f)] private float rotationTolerance = 25f;
    [SerializeField, Min(0.05f)] private float stableSpeedThreshold = 0.12f;
    [SerializeField, Min(0.05f)] private float stableAngularSpeedThreshold = 0.2f;
    [SerializeField, Min(0f)] private float stableTimeRequired = 0.35f;

    [Header("Forklift")]
    [Tooltip("Optional. If empty, the active forklift is found automatically.")]
    [SerializeField] private ForkliftController forklift;
    [SerializeField, Min(0f)] private float forkClearancePadding = 0.1f;

    private BoxCollider triggerZone;
    private Rigidbody candidate;
    private Rigidbody storedPallet;
    private float stableTimer;
    private Transform loadRoot;
    private Rigidbody trailerBody;

    public bool IsOccupied => storedPallet != null;

    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void Awake()
    {
        triggerZone = GetComponent<BoxCollider>();
        if (triggerZone == null)
        {
            Debug.LogError("WarehouseTrailerPalletSlot: Missing BoxCollider.", this);
            enabled = false;
            return;
        }

        // This component owns its trigger; the marker's BoxCollider is not a
        // physical pallet collider.
        triggerZone.isTrigger = true;
        loadRoot = FindLoadRoot(transform);
        if (loadRoot == null)
        {
            Debug.LogError("WarehouseTrailerPalletSlot: This marker must be beneath the trailer child named 'Load'.", this);
            enabled = false;
            return;
        }

        trailerBody = loadRoot.GetComponentInParent<Rigidbody>();
        if (forklift == null)
            forklift = FindObjectOfType<ForkliftController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        TrySelectPallet(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (storedPallet == null && candidate == null)
            TrySelectPallet(other);
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody hitBody = GetTopmostCargoBody(other.transform);
        if (candidate != null && hitBody == candidate)
        {
            candidate = null;
            stableTimer = 0f;
        }
    }

    private void Update()
    {
        if (storedPallet != null)
        {
            // The trailer unloader unparents a pallet when it is picked up.
            if (loadRoot == null || !storedPallet.transform.IsChildOf(loadRoot))
                storedPallet = null;
            return;
        }

        if (candidate == null)
            return;

        if (!candidate.gameObject.activeInHierarchy)
        {
            ClearCandidate();
            return;
        }

        float distance = Vector3.Distance(candidate.position, transform.position);
        float angle = Quaternion.Angle(candidate.rotation, transform.rotation);
        if (distance > captureDistance || angle > rotationTolerance)
        {
            stableTimer = 0f;
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

    private void TrySelectPallet(Collider other)
    {
        if (storedPallet != null || candidate != null || other == null)
            return;

        Rigidbody body = GetTopmostCargoBody(other.transform);
        if (body == null || body.isKinematic)
            return;

        // Ignore the trailer itself and any rigidbody that belongs to the
        // forklift. Only loose cargo can be captured by a pallet slot.
        if (body == trailerBody || body.GetComponentInParent<ForkliftController>() != null)
            return;

        candidate = body;
        stableTimer = 0f;
    }

    private Rigidbody GetTopmostCargoBody(Transform item)
    {
        Rigidbody selected = null;
        Transform current = item;

        while (current != null)
        {
            if (current == loadRoot)
                break;

            Rigidbody body = current.GetComponent<Rigidbody>();
            if (body != null)
                selected = body;

            current = current.parent;
        }

        return selected;
    }

    private bool IsForkliftClearOf(Rigidbody pallet)
    {
        if (forklift == null)
            forklift = FindObjectOfType<ForkliftController>();

        // Do not snap cargo unless we can verify that both forks have cleared it.
        if (forklift == null || forklift.fork_L == null || forklift.fork_R == null)
            return false;

        Bounds bounds = GetPalletBounds(pallet);
        bounds.Expand(forkClearancePadding * 2f);

        return !bounds.Contains(forklift.fork_L.transform.position) &&
               !bounds.Contains(forklift.fork_R.transform.position);
    }

    private void StoreCandidate()
    {
        if (candidate == null || loadRoot == null)
            return;

        Rigidbody pallet = candidate;
        pallet.velocity = Vector3.zero;
        pallet.angularVelocity = Vector3.zero;
        pallet.transform.SetParent(loadRoot, true);
        pallet.position = transform.position;
        pallet.rotation = transform.rotation;
        pallet.useGravity = false;
        pallet.isKinematic = true;
        pallet.interpolation = RigidbodyInterpolation.None;

        storedPallet = pallet;
        candidate = null;
        stableTimer = 0f;

        Debug.Log("WarehouseTrailerPalletSlot: Snapped pallet '" + pallet.name + "' into trailer slot '" + name + "'.", pallet);
    }

    private void ClearCandidate()
    {
        candidate = null;
        stableTimer = 0f;
    }

    private static Bounds GetPalletBounds(Rigidbody pallet)
    {
        Collider[] colliders = pallet.GetComponentsInChildren<Collider>();
        Bounds bounds = new Bounds(pallet.worldCenterOfMass, Vector3.zero);
        bool found = false;

        foreach (Collider item in colliders)
        {
            if (item == null || item.isTrigger)
                continue;

            if (!found)
            {
                bounds = item.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(item.bounds);
            }
        }

        return bounds;
    }

    private static Transform FindLoadRoot(Transform item)
    {
        for (Transform current = item; current != null; current = current.parent)
            if (current.name == "Load")
                return current;

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsOccupied ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.12f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);
    }
}
