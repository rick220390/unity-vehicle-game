using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Door interaction point and hinged door animation.
/// Attach this component to the hinge pivot (the empty GameObject placed at the
/// physical hinge). Keep the visible door mesh as a child of that pivot.
/// </summary>
[DisallowMultipleComponent]
public class VehicleDoor : MonoBehaviour
{
    public enum DoorRole
    {
        Driver,
        FrontPassenger,
        RearLeftPassenger,
        RearRightPassenger,
        Other
    }

    public static readonly List<VehicleDoor> All = new List<VehicleDoor>();

    [Header("Door Connection")]
    [Tooltip("The root vehicle this door belongs to. Found from the parent hierarchy when possible.")]
    public Vehicle vehicle;

    [Tooltip("World-space point used to detect when the player is near this door. If empty, the hinge position is used.")]
    public Transform interactionPoint;

    [Tooltip("Optional seat position used when entering through this door. If empty, Vehicle.seatPoint is used.")]
    public Transform seatPointOverride;

    [Tooltip("Empty transform placed just outside this door at the character's standing position. Rotate it so its forward direction faces into the car.")]
    public Transform entryPoint;

    [Tooltip("Optional separate position where the character should finish after exiting through this door. Its forward direction controls the final facing direction.")]
    public Transform exitPoint;

    [Header("Door Role")]
    public DoorRole role = DoorRole.Driver;

    [Header("Hinge Animation")]
    [Tooltip("Local axis to rotate around. Most car doors use the local Y axis.")]
    public Vector3 localOpenAxis = Vector3.up;

    [Tooltip("Degrees to rotate from the closed position. Use a negative value if the door swings the wrong way.")]
    public float openAngle = 65f;

    [Min(0.05f)]
    public float animationDuration = 0.45f;

    [Tooltip("Optional keyboard test while playing. Press K by default to open/close this door.")]
    public bool enableKeyboardTest = true;
    public KeyCode testToggleKey = KeyCode.K;

    private Quaternion closedLocalRotation;
    private Quaternion openLocalRotation;
    private Coroutine animationRoutine;
    private bool isOpen;

    /// <summary>The configured interaction point, or this hinge transform.</summary>
    public Transform InteractionTransform =>
        interactionPoint != null ? interactionPoint : transform;

    public bool IsOpen => isOpen;

    /// <summary>True when this is an active driver entry point on an available vehicle.</summary>
    public bool CanEnterAsDriver =>
        isActiveAndEnabled &&
        vehicle != null &&
        vehicle.isActiveAndEnabled &&
        !vehicle.HasDriver &&
        role == DoorRole.Driver;

    private void Awake()
    {
        CacheHingeRotations();
    }

    private void Reset()
    {
        FindVehicleIfMissing();
        CacheHingeRotations();
    }

    private void OnValidate()
    {
        FindVehicleIfMissing();
        if (localOpenAxis.sqrMagnitude < 0.001f)
            localOpenAxis = Vector3.up;
        animationDuration = Mathf.Max(0.05f, animationDuration);
    }

    private void OnEnable()
    {
        FindVehicleIfMissing();
        CacheHingeRotations();

        if (!All.Contains(this))
            All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);

        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }
    }

    private void Update()
    {
        if (enableKeyboardTest && Input.GetKeyDown(testToggleKey))
            ToggleDoor();
    }

    private void CacheHingeRotations()
    {
        closedLocalRotation = transform.localRotation;
        openLocalRotation = closedLocalRotation *
            Quaternion.AngleAxis(openAngle, localOpenAxis.sqrMagnitude > 0.001f
                ? localOpenAxis.normalized
                : Vector3.up);
    }

    public void OpenDoor()
    {
        AnimateDoor(true);
    }

    public void CloseDoor()
    {
        AnimateDoor(false);
    }

    public void ToggleDoor()
    {
        AnimateDoor(!isOpen);
    }

    private void AnimateDoor(bool open)
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        // Recalculate in case the angle or axis was adjusted in the Inspector.
        openLocalRotation = closedLocalRotation *
            Quaternion.AngleAxis(openAngle, localOpenAxis.sqrMagnitude > 0.001f
                ? localOpenAxis.normalized
                : Vector3.up);

        animationRoutine = StartCoroutine(AnimateRotation(
            open ? openLocalRotation : closedLocalRotation,
            open
        ));
    }

    private IEnumerator AnimateRotation(Quaternion targetRotation, bool opening)
    {
        Quaternion startRotation = transform.localRotation;
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, animationDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Smooth ease-in/ease-out rather than a mechanical linear swing.
            float smoothT = t * t * (3f - 2f * t);
            transform.localRotation = Quaternion.Slerp(startRotation, targetRotation, smoothT);
            yield return null;
        }

        transform.localRotation = targetRotation;
        isOpen = opening;
        animationRoutine = null;
    }

    private void FindVehicleIfMissing()
    {
        if (vehicle == null)
            vehicle = GetComponentInParent<Vehicle>();
    }

    public float DistanceToPoint(Vector3 point)
    {
        return Vector3.Distance(InteractionTransform.position, point);
    }

    /// <summary>
    /// Legacy vehicles without door markers can still use body-based entry.
    /// Once a door marker exists, entry is limited to its interaction point.
    /// </summary>
    public static bool HasAnyDoorFor(Vehicle targetVehicle)
    {
        if (targetVehicle == null)
            return false;

        foreach (VehicleDoor door in All)
        {
            if (door != null &&
                door.isActiveAndEnabled &&
                door.vehicle == targetVehicle)
                return true;
        }

        return false;
    }
}
