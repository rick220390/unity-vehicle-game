using UnityEngine;
using WSMGameStudio.HeavyMachinery;

/// <summary>
/// A reusable warehouse rack position for one complete pallet.
/// Stores a pallet after it is placed and the forklift forks are withdrawn.
/// Releases it again when both forks are inserted underneath and the operator lifts.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class WarehousePalletStorageSlot : MonoBehaviour
{
    [Header("Slot setup")]
    [SerializeField] private BoxCollider triggerZone;
    [SerializeField] private Transform placementPoint;

    [Header("Forklift reference")]
    [SerializeField] private ForkliftController forklift;

    [Header("Storage clearance")]
    [Tooltip("Extra space around the pallet bounds that the fork transforms must clear before the pallet is stored.")]
    [SerializeField, Min(0f)] private float forkClearancePadding = 0.08f;

    [Header("Retrieval detection")]
    [Tooltip("How far below the pallet bounds the fork transforms may be while inserted underneath it.")]
    [SerializeField, Min(0f)] private float forkInsertionBelowPallet = 0.35f;
    [Tooltip("How far above the pallet bounds the fork transforms may be while engaging it.")]
    [SerializeField, Min(0f)] private float forkInsertionAbovePallet = 0.08f;
    [Range(0.0001f, 0.01f)]
    [Tooltip("Fork lift input changes gradually each frame. This detects the upward movement.")]
    [SerializeField] private float liftInputThreshold = 0.0005f;

    [Header("Storage stability")]
    [SerializeField, Min(0.05f)] private float stableSpeedThreshold = 0.08f;
    [SerializeField, Min(0.05f)] private float stableAngularSpeedThreshold = 0.15f;
    [SerializeField, Min(0f)] private float stableTimeRequired = 0.4f;

    private Rigidbody candidate;
    private float stableTimer;
    private float previousLiftInput;
    private float previousLeftForkY;
    private float previousRightForkY;
    private bool occupied;
    private int recentlyReleasedPalletId;

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

        if (forklift != null)
        {
            previousLiftInput = forklift.ForksVertical;
            if (forklift.fork_L != null) previousLeftForkY = forklift.fork_L.transform.position.y;
            if (forklift.fork_R != null) previousRightForkY = forklift.fork_R.transform.position.y;
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
        Rigidbody body = other.attachedRigidbody;

        if (body != null && body.GetInstanceID() == recentlyReleasedPalletId)
            recentlyReleasedPalletId = 0;

        if (occupied || candidate == null || body != candidate)
            return;

        ClearCandidate();
    }

    private void Update()
    {
        if (candidate == null)
        {
            if (occupied)
            {
                occupied = false;
                ClearCandidate();
            }
            return;
        }

        if (occupied)
        {
            UpdateRetrieval();
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
        if (body == null || body.isKinematic || body.GetInstanceID() == recentlyReleasedPalletId)
            return;

        candidate = body;
        stableTimer = 0f;
        Debug.Log("WarehousePalletStorageSlot: Pallet candidate entered slot '" + name + "'.", candidate);
    }

    private bool IsForkliftClearOf(Rigidbody pallet)
    {
        if (forklift == null || forklift.fork_L == null || forklift.fork_R == null)
            return true;

        Bounds bounds = GetPalletBounds(pallet);
        bounds.Expand(forkClearancePadding * 2f);

        return !bounds.Contains(forklift.fork_L.transform.position) &&
               !bounds.Contains(forklift.fork_R.transform.position);
    }

    private void UpdateRetrieval()
    {
        if (forklift == null || forklift.fork_L == null || forklift.fork_R == null)
            return;

        ForkliftPlayerInput playerInput = forklift.GetComponent<ForkliftPlayerInput>();
        if (playerInput != null && !playerInput.enablePlayerInput)
        {
            previousLiftInput = forklift.ForksVertical;
            return;
        }

        float liftInput = forklift.ForksVertical;
        float leftForkY = forklift.fork_L.transform.position.y;
        float rightForkY = forklift.fork_R.transform.position.y;

        // Use actual fork movement as well as the controller input. This still
        // works if the input value is already at its upper limit.
        bool lifting = liftInput > previousLiftInput + liftInputThreshold ||
                       leftForkY > previousLeftForkY + 0.0001f ||
                       rightForkY > previousRightForkY + 0.0001f;

        previousLiftInput = liftInput;
        previousLeftForkY = leftForkY;
        previousRightForkY = rightForkY;

        if (!lifting)
            return;

        bool forksInserted = AreForksInsertedUnder(candidate);
        Debug.Log(
            "WarehousePalletStorageSlot: Upward fork movement at '" + name +
            "'. Forks inserted under stored pallet=" + forksInserted +
            ". Left fork Y=" + leftForkY.ToString("F3") +
            ", right fork Y=" + rightForkY.ToString("F3") + ".", this);

        if (forksInserted)
            ReleaseStoredPallet();
    }

    private bool AreForksInsertedUnder(Rigidbody pallet)
    {
        Bounds palletBounds = GetPalletBounds(pallet);

        // The fork component transforms are often located back at their mounting
        // points, not at the fork tips. Test the actual fork collider bounds instead.
        float minForkY = palletBounds.min.y - forkInsertionBelowPallet;
        float maxForkY = palletBounds.min.y + forkInsertionAbovePallet;
        Bounds insertionBand = new Bounds(
            new Vector3(palletBounds.center.x, (minForkY + maxForkY) * 0.5f, palletBounds.center.z),
            new Vector3(
                palletBounds.size.x + forkClearancePadding * 2f,
                maxForkY - minForkY,
                palletBounds.size.z + forkClearancePadding * 2f));

        return ForkColliderIntersectsInsertionBand(forklift.fork_L.transform, insertionBand) &&
               ForkColliderIntersectsInsertionBand(forklift.fork_R.transform, insertionBand);
    }

    private static bool ForkColliderIntersectsInsertionBand(Transform forkRoot, Bounds insertionBand)
    {
        Collider[] forkColliders = forkRoot.GetComponentsInChildren<Collider>();
        foreach (Collider forkCollider in forkColliders)
        {
            if (forkCollider == null || forkCollider.isTrigger)
                continue;

            if (forkCollider.bounds.Intersects(insertionBand))
                return true;
        }

        // Fallback for fork models that do not have colliders on the fork parts.
        return insertionBand.Contains(forkRoot.position);
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
        previousLiftInput = forklift != null ? forklift.ForksVertical : 0f;
        if (forklift != null && forklift.fork_L != null) previousLeftForkY = forklift.fork_L.transform.position.y;
        if (forklift != null && forklift.fork_R != null) previousRightForkY = forklift.fork_R.transform.position.y;

        Debug.Log("WarehousePalletStorageSlot: Stored pallet '" + candidate.name + "' in slot '" + name + "'.", candidate);
    }

    private void ReleaseStoredPallet()
    {
        Rigidbody pallet = candidate;
        if (pallet == null)
            return;

        pallet.transform.SetParent(null, true);
        pallet.velocity = Vector3.zero;
        pallet.angularVelocity = Vector3.zero;
        pallet.useGravity = true;
        pallet.isKinematic = false;
        pallet.interpolation = RigidbodyInterpolation.Interpolate;
        pallet.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        pallet.WakeUp();

        recentlyReleasedPalletId = pallet.GetInstanceID();
        occupied = false;
        ClearCandidate();
        previousLiftInput = forklift.ForksVertical;
        previousLeftForkY = forklift.fork_L.transform.position.y;
        previousRightForkY = forklift.fork_R.transform.position.y;

        Debug.Log("WarehousePalletStorageSlot: Released pallet '" + pallet.name + "' for forklift retrieval.", pallet);
    }

    private void ClearCandidate()
    {
        candidate = null;
        stableTimer = 0f;
    }

    /// <summary>Allows another system to clear this slot when it retrieves the pallet.</summary>
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