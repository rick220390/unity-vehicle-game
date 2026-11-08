using UnityEngine;

/// <summary>
/// Loading-bay docking controller for an independent trailer.
/// Add this to the loading-bay trigger and assign a Dock Point whose pose
/// represents the required trailer-root pose. The trailer is only captured
/// when it is uncoupled and already reasonably aligned with the bay.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class WarehouseTrailerDockingBay : MonoBehaviour
{
    [Header("Dock setup")]
    [SerializeField] private BoxCollider triggerZone;
    [Tooltip("Place this transform where the TRAILER ROOT should finish, with the required trailer heading.")]
    [SerializeField] private Transform dockPoint;

    [Header("Approach tolerances")]
    [Tooltip("Maximum horizontal distance from the trailer root to the dock point before capture is allowed.")]
    [SerializeField, Min(0.1f)] private float captureDistance = 3f;
    [Tooltip("Maximum height difference allowed before capture.")]
    [SerializeField, Min(0.05f)] private float captureHeightDifference = 0.75f;
    [Tooltip("Maximum yaw difference allowed before capture.")]
    [SerializeField, Range(1f, 90f)] private float captureAngle = 20f;

    [Header("Dock movement")]
    [SerializeField, Min(0.1f)] private float dockingSpeed = 2f;
    [SerializeField, Min(1f)] private float dockingTurnSpeed = 25f;
    [SerializeField, Min(0.001f)] private float positionSnapTolerance = 0.025f;
    [SerializeField, Min(0.1f)] private float rotationSnapTolerance = 1f;

    private WarehousePlayerTrailerCoupling candidate;
    private Rigidbody candidateBody;
    private bool isDocking;
    private bool isDocked;
    private int ignoreUntilExitInstanceId;

    private bool originalIsKinematic;
    private bool originalUseGravity;
    private RigidbodyInterpolation originalInterpolation;
    private CollisionDetectionMode originalCollisionMode;

    public bool IsOccupied => candidate != null;
    public bool IsDocking => isDocking;
    public bool IsDocked => isDocked;
    public WarehousePlayerTrailerCoupling DockedTrailer => isDocked ? candidate : null;

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
            Debug.LogError("WarehouseTrailerDockingBay: Assign a BoxCollider with Is Trigger enabled.", this);
            enabled = false;
            return;
        }

        if (dockPoint == null)
        {
            Debug.LogError("WarehouseTrailerDockingBay: Assign the trailer-root Dock Point.", this);
            enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCapture(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (candidate == null)
            TryCapture(other);
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body != null && body.GetInstanceID() == ignoreUntilExitInstanceId)
            ignoreUntilExitInstanceId = 0;
    }

    private void FixedUpdate()
    {
        if (!isDocking || candidateBody == null || dockPoint == null)
            return;

        Vector3 nextPosition = Vector3.MoveTowards(
            candidateBody.position, dockPoint.position, dockingSpeed * Time.fixedDeltaTime);
        Quaternion nextRotation = Quaternion.RotateTowards(
            candidateBody.rotation, dockPoint.rotation, dockingTurnSpeed * Time.fixedDeltaTime);

        candidateBody.MovePosition(nextPosition);
        candidateBody.MoveRotation(nextRotation);

        bool positionReached = Vector3.Distance(nextPosition, dockPoint.position) <= positionSnapTolerance;
        bool rotationReached = Quaternion.Angle(nextRotation, dockPoint.rotation) <= rotationSnapTolerance;

        if (!positionReached || !rotationReached)
            return;

        candidateBody.position = dockPoint.position;
        candidateBody.rotation = dockPoint.rotation;
        candidateBody.velocity = Vector3.zero;
        candidateBody.angularVelocity = Vector3.zero;
        isDocking = false;
        isDocked = true;

        Debug.Log("WarehouseTrailerDockingBay: Trailer '" + candidate.name + "' is secured in loading bay '" + name + "'.", candidate);
    }

    /// <summary>
    /// Public entry point for trailer-spawn systems. The spawned trailer must
    /// already be positioned within the configured capture tolerances.
    /// </summary>
    public bool RequestDock(WarehousePlayerTrailerCoupling trailer)
    {
        if (trailer == null || dockPoint == null || candidate != null)
            return false;

        if (!CanCapture(trailer))
            return false;

        BeginDocking(trailer);
        return true;
    }

    private void TryCapture(Collider other)
    {
        if (candidate != null || other == null)
            return;

        Rigidbody body = other.attachedRigidbody;
        if (body != null && body.GetInstanceID() == ignoreUntilExitInstanceId)
            return;

        WarehousePlayerTrailerCoupling trailer =
            other.GetComponentInParent<WarehousePlayerTrailerCoupling>();

        if (trailer != null && CanCapture(trailer))
            BeginDocking(trailer);
    }

    private bool CanCapture(WarehousePlayerTrailerCoupling trailer)
    {
        if (trailer == null || trailer.IsCoupled || trailer.IsDocked || trailer.IsDocking)
            return false;

        Rigidbody body = trailer.GetComponent<Rigidbody>();
        if (body == null || body.isKinematic)
            return false;

        float horizontalDistance = Vector2.Distance(
            new Vector2(body.position.x, body.position.z),
            new Vector2(dockPoint.position.x, dockPoint.position.z));

        float heightDifference = Mathf.Abs(body.position.y - dockPoint.position.y);
        float yawDifference = Vector3.Angle(
            Vector3.ProjectOnPlane(body.transform.forward, Vector3.up),
            Vector3.ProjectOnPlane(dockPoint.forward, Vector3.up));

        return horizontalDistance <= captureDistance &&
               heightDifference <= captureHeightDifference &&
               yawDifference <= captureAngle;
    }

    private void BeginDocking(WarehousePlayerTrailerCoupling trailer)
    {
        candidate = trailer;
        candidateBody = trailer.GetComponent<Rigidbody>();

        originalIsKinematic = candidateBody.isKinematic;
        originalUseGravity = candidateBody.useGravity;
        originalInterpolation = candidateBody.interpolation;
        originalCollisionMode = candidateBody.collisionDetectionMode;

        candidateBody.velocity = Vector3.zero;
        candidateBody.angularVelocity = Vector3.zero;
        candidateBody.useGravity = false;
        candidateBody.isKinematic = true;
        candidateBody.interpolation = RigidbodyInterpolation.Interpolate;

        trailer.SetDockingBay(this);
        isDocking = true;
        isDocked = false;

        Debug.Log("WarehouseTrailerDockingBay: Capturing trailer '" + trailer.name + "' for loading bay '" + name + "'.", trailer);
    }

    /// <summary>
    /// Called by the trailer coupling component only after the fifth-wheel
    /// joint has been created successfully. The trailer is then free to move
    /// with the tractor and this bay ignores it until it exits the trigger.
    /// </summary>
    public void ReleaseForCoupling(WarehousePlayerTrailerCoupling trailer)
    {
        if (trailer == null || candidate != trailer || candidateBody == null)
            return;

        candidateBody.velocity = Vector3.zero;
        candidateBody.angularVelocity = Vector3.zero;
        candidateBody.isKinematic = originalIsKinematic;
        candidateBody.useGravity = originalUseGravity;
        candidateBody.interpolation = originalInterpolation;
        candidateBody.collisionDetectionMode = originalCollisionMode;
        candidateBody.WakeUp();

        ignoreUntilExitInstanceId = candidateBody.GetInstanceID();
        trailer.ClearDockingBay(this);

        Debug.Log("WarehouseTrailerDockingBay: Released trailer '" + trailer.name + "' for tractor coupling.", trailer);

        candidate = null;
        candidateBody = null;
        isDocking = false;
        isDocked = false;
    }

    /// <summary>
    /// Allows a scene manager to cancel a trailer's docking attempt safely.
    /// </summary>
    public void CancelDocking()
    {
        if (candidate == null || candidateBody == null)
            return;

        candidateBody.isKinematic = originalIsKinematic;
        candidateBody.useGravity = originalUseGravity;
        candidateBody.interpolation = originalInterpolation;
        candidateBody.collisionDetectionMode = originalCollisionMode;
        candidateBody.WakeUp();
        candidate.ClearDockingBay(this);

        candidate = null;
        candidateBody = null;
        isDocking = false;
        isDocked = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (dockPoint != null)
        {
            Gizmos.color = isDocked ? Color.green : (isDocking ? Color.yellow : Color.cyan);
            Gizmos.DrawWireCube(dockPoint.position, new Vector3(2.5f, 0.25f, 6f));
            Gizmos.DrawLine(dockPoint.position, dockPoint.position + dockPoint.forward * 2f);
        }

        if (triggerZone != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(triggerZone.center, triggerZone.size);
        }
    }
}
