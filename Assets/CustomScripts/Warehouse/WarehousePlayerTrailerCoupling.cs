using UnityEngine;

/// <summary>
/// Player-operable fifth-wheel receiver for a trailer.
/// Put this on the trailer root (the same object as its Rigidbody).
/// Assign the kingpin marker and the landing-leg support colliders.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class WarehousePlayerTrailerCoupling : MonoBehaviour
{
    [Header("Coupling")]
    [SerializeField] private string kingpinPivotName = "kingpinPivot";
    [SerializeField, Min(0.1f)] private float couplingDistance = 0.65f;
    [SerializeField, Range(1f, 45f)] private float maximumAlignmentAngle = 12f;

    [Header("Landing legs")]
    [Tooltip("Assign only the colliders that physically support the trailer when uncoupled.")]
    [SerializeField] private Collider[] landingLegSupportColliders;

    private Rigidbody trailerBody;
    private Transform kingpinPivot;
    private ConfigurableJoint couplingJoint;
    private Rigidbody connectedTractor;
    private bool isCoupled;

    public bool IsCoupled => isCoupled;
    public Rigidbody ConnectedTractor => connectedTractor;
    public Transform KingpinPivot => kingpinPivot;

    private void Awake()
    {
        trailerBody = GetComponent<Rigidbody>();
        kingpinPivot = FindChild(transform, kingpinPivotName);

        // A standalone trailer starts supported by its landing legs.
        SetLandingLegSupport(true);

        if (kingpinPivot == null)
            Debug.LogError("WarehousePlayerTrailerCoupling: Could not find child '" + kingpinPivotName + "'.", this);
    }

    public bool CanCouple(Rigidbody tractorBody, Transform fifthWheelPivot, out string reason)
    {
        reason = string.Empty;

        if (isCoupled)
        {
            reason = "Trailer is already coupled.";
            return false;
        }

        if (trailerBody == null || tractorBody == null || fifthWheelPivot == null || kingpinPivot == null)
        {
            reason = "Missing tractor, fifth-wheel or kingpin reference.";
            return false;
        }

        if (tractorBody == trailerBody)
        {
            reason = "Cannot couple a trailer to itself.";
            return false;
        }

        float distance = Vector3.Distance(fifthWheelPivot.position, kingpinPivot.position);
        if (distance > couplingDistance)
        {
            reason = "Reverse closer to the trailer.";
            return false;
        }

        float angle = Vector3.Angle(
            Vector3.ProjectOnPlane(fifthWheelPivot.forward, Vector3.up),
            Vector3.ProjectOnPlane(kingpinPivot.forward, Vector3.up));

        if (angle > maximumAlignmentAngle)
        {
            reason = "Fifth wheel is not aligned with the kingpin.";
            return false;
        }

        return true;
    }

    public bool TryCouple(Rigidbody tractorBody, Transform fifthWheelPivot, out string reason)
    {
        if (!CanCouple(tractorBody, fifthWheelPivot, out reason))
            return false;

        if (!WarehouseTruckTrailerCoupler.Connect(
            tractorBody, fifthWheelPivot, trailerBody, kingpinPivot, out couplingJoint))
        {
            reason = "The fifth-wheel joint could not be created. Check the Console.";
            return false;
        }

        connectedTractor = tractorBody;
        isCoupled = true;

        // The tractor now supports the front of the trailer.
        SetLandingLegSupport(false);

        reason = "Trailer coupled.";
        Debug.Log("WarehousePlayerTrailerCoupling: Trailer coupled to " + tractorBody.name + ".", this);
        return true;
    }

    public bool TryUncouple(out string reason)
    {
        reason = string.Empty;

        if (!isCoupled || couplingJoint == null)
        {
            reason = "Trailer is not coupled.";
            return false;
        }

        // Engage the support legs before releasing the joint so the trailer
        // cannot drop onto its nose when the tractor pulls away.
        SetLandingLegSupport(true);
        Physics.SyncTransforms();

        couplingJoint.connectedBody = null;
        Destroy(couplingJoint);
        couplingJoint = null;
        connectedTractor = null;
        isCoupled = false;

        reason = "Trailer uncoupled.";
        Debug.Log("WarehousePlayerTrailerCoupling: Trailer uncoupled; landing-leg supports enabled.", this);
        return true;
    }

    private void SetLandingLegSupport(bool supportsTrailer)
    {
        if (landingLegSupportColliders == null)
            return;

        foreach (Collider support in landingLegSupportColliders)
        {
            if (support != null)
                support.enabled = supportsTrailer;
        }
    }

    private static Transform FindChild(Transform root, string exactName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == exactName)
                return child;

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Transform marker = kingpinPivot != null ? kingpinPivot : FindChild(transform, kingpinPivotName);
        if (marker == null) return;

        Gizmos.color = isCoupled ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(marker.position, couplingDistance);
        Gizmos.DrawLine(marker.position, marker.position + marker.forward);
    }
}
