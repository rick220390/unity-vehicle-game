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
    [Tooltip("Drag the trailer's kingpinPivot empty Transform here.")]
    [SerializeField] private Transform kingpinPivot;
    [Tooltip("Maximum horizontal distance between the fifth wheel and kingpin.")]
    [SerializeField, Min(0.05f)] private float couplingDistance = 0.2f;
    [Tooltip("Maximum vertical height difference between the fifth wheel and kingpin.")]
    [SerializeField, Min(0.02f)] private float maximumHeightDifference = 0.1f;
    [SerializeField, Range(1f, 20f)] private float maximumAlignmentAngle = 8f;

    [Header("Landing legs")]
    [Tooltip("Assign only the colliders that physically support the trailer when uncoupled.")]
    [SerializeField] private Collider[] landingLegSupportColliders;

    private Rigidbody trailerBody;
    private ConfigurableJoint couplingJoint;
    private Rigidbody connectedTractor;
    private bool isCoupled;
    private WarehouseTrailerDockingBay dockingBay;

    public bool IsCoupled => isCoupled;
    public Rigidbody ConnectedTractor => connectedTractor;
    public Transform KingpinPivot => kingpinPivot;
    public float CouplingDistance => couplingDistance;
    public float MaximumHeightDifference => maximumHeightDifference;
    public bool IsDocking => dockingBay != null && dockingBay.IsDocking;
    public bool IsDocked => dockingBay != null && dockingBay.IsDocked;

    public void SetDockingBay(WarehouseTrailerDockingBay bay)
    {
        dockingBay = bay;
    }

    public void ClearDockingBay(WarehouseTrailerDockingBay bay)
    {
        if (dockingBay == bay)
            dockingBay = null;
    }

    private void Awake()
    {
        trailerBody = GetComponent<Rigidbody>();
        // A standalone trailer starts supported by its landing legs.
        SetLandingLegSupport(true);

        if (kingpinPivot == null)
            Debug.LogError("WarehousePlayerTrailerCoupling: Assign the trailer kingpinPivot Transform in the Inspector.", this);
    }

    public bool CanCouple(Rigidbody tractorBody, Transform fifthWheelPivot, out string reason)
    {
        reason = string.Empty;

        if (isCoupled)
        {
            reason = "Trailer is already coupled.";
            return false;
        }

        if (dockingBay != null && !dockingBay.IsDocked)
        {
            reason = "Wait for the trailer to finish positioning in the loading bay before coupling.";
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

        float relativeSpeed = (trailerBody.velocity - tractorBody.velocity).magnitude;
        if (relativeSpeed > 0.5f)
        {
            reason = "Stop the tractor and trailer before coupling (relative speed must be below 0.5 m/s).";
            return false;
        }

        Vector3 pivotOffset = fifthWheelPivot.position - kingpinPivot.position;
        float horizontalSeparation = Vector2.Distance(
            new Vector2(fifthWheelPivot.position.x, fifthWheelPivot.position.z),
            new Vector2(kingpinPivot.position.x, kingpinPivot.position.z));
        float heightSeparation = Mathf.Abs(pivotOffset.y);

        // Use the approach tolerances exposed in the Inspector. The old code
        // ignored these fields and imposed an unrelated 3cm full-3D gate,
        // which made normal player coupling fail even inside the configured zone.
        if (horizontalSeparation > couplingDistance ||
            heightSeparation > maximumHeightDifference)
        {
            reason = "Kingpin alignment is outside the coupling zone. Horizontal: " +
                     horizontalSeparation.ToString("0.000") + "m / " +
                     couplingDistance.ToString("0.000") + "m; height: " +
                     heightSeparation.ToString("0.000") + "m / " +
                     maximumHeightDifference.ToString("0.000") + "m.";
            Debug.LogWarning(
                "WarehousePlayerTrailerCoupling: " + reason +
                " Fifth-wheel world=" + fifthWheelPivot.position.ToString("F3") +
                ", kingpin world=" + kingpinPivot.position.ToString("F3") +
                ", delta=" + pivotOffset.ToString("F3") +
                ". Fifth-wheel path=" + GetHierarchyPath(fifthWheelPivot) +
                ", kingpin path=" + GetHierarchyPath(kingpinPivot) +
                ", tractor Rigidbody=" + tractorBody.position.ToString("F3") +
                ", trailer Rigidbody=" + trailerBody.position.ToString("F3") + ".",
                this);
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
        // Synchronise physics BEFORE validation. Validation and joint creation
        // must use the same marker poses; previously the sync happened between them.
        Physics.SyncTransforms();

        if (!CanCouple(tractorBody, fifthWheelPivot, out reason))
            return false;

        if (!WarehouseTruckTrailerCoupler.Connect(
            tractorBody, fifthWheelPivot, trailerBody, kingpinPivot,
            couplingDistance, maximumHeightDifference, out couplingJoint))
        {
            reason = "The fifth-wheel joint could not be created. Check the Console.";
            return false;
        }

        // Only release the loading-bay hold after the fifth-wheel joint has
        // been created successfully. A failed coupling attempt leaves the
        // trailer safely docked and stationary.
        if (dockingBay != null)
            dockingBay.ReleaseForCoupling(this);

        connectedTractor = tractorBody;
        isCoupled = true;

        // The tractor now supports the front of the trailer.
        SetLandingLegSupport(false);

        // Cargo securing is temporarily disabled during player coupling tests.
        // First verify that the tractor/trailer fifth-wheel joint is stable on its own.
        // Re-enable shared cargo securing after the coupling physics is confirmed.

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

    private static string GetHierarchyPath(Transform item)
    {
        if (item == null) return "<null>";
        string path = item.name;
        Transform parent = item.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
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

    private void OnDrawGizmosSelected()
    {
        Transform marker = kingpinPivot;
        if (marker == null) return;

        Gizmos.color = isCoupled ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(marker.position, couplingDistance);
        Gizmos.DrawLine(marker.position, marker.position + marker.forward);
    }
}
