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
    [Tooltip("Maximum horizontal distance between the fifth wheel and kingpin.")]
    [SerializeField, Min(0.05f)] private float couplingDistance = 0.2f;
    [Tooltip("Maximum vertical height difference between the fifth wheel and kingpin.")]
    [SerializeField, Min(0.02f)] private float maximumHeightDifference = 0.1f;
    [SerializeField, Range(1f, 20f)] private float maximumAlignmentAngle = 8f;

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

        float relativeSpeed = (trailerBody.velocity - tractorBody.velocity).magnitude;
        if (relativeSpeed > 0.5f)
        {
            reason = "Stop the tractor and trailer before coupling (relative speed must be below 0.5 m/s).";
            return false;
        }

        Vector3 pivotOffset = fifthWheelPivot.position - kingpinPivot.position;
        Vector3 horizontalOffset = Vector3.ProjectOnPlane(pivotOffset, Vector3.up);
        float horizontalDistance = horizontalOffset.magnitude;
        float heightDifference = Mathf.Abs(pivotOffset.y);

        if (horizontalDistance > couplingDistance || heightDifference > maximumHeightDifference)
        {
            reason = "Coupling alignment: horizontal " + horizontalDistance.ToString("0.00") +
                     "m / " + couplingDistance.ToString("0.00") + "m, height difference " +
                     heightDifference.ToString("0.00") + "m / " +
                     maximumHeightDifference.ToString("0.00") + "m. Adjust tractor position/height.";
            Debug.LogWarning("WarehousePlayerTrailerCoupling: " + reason, this);
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

        // Apply current transform changes before creating a constrained joint.
        Physics.SyncTransforms();

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
