using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Door interaction point and animation.
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

    public enum DoorMotionType
    {
        Hinged,
        Scissor
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

    [Header("Seated Character Alignment")]
    [Tooltip("Fine adjustment from the seat marker, in the marker's local axes. Negative Y lowers the character; negative Z moves them backwards.")]
    public Vector3 seatedLocalPositionOffset;
    [Tooltip("Fine rotation adjustment from the seat marker, in local degrees. Use Y to correct the character's facing direction.")]
    public Vector3 seatedLocalRotationOffset;

    [Header("Door Role")]
    public DoorRole role = DoorRole.Driver;

    [Header("Door Motion")]
    public DoorMotionType motionType = DoorMotionType.Hinged;

    [Tooltip("Local axis to rotate around. For scissor doors, use the axis that lifts the door upwards.")]
    public Vector3 localOpenAxis = Vector3.up;

    [Tooltip("Degrees to rotate from the closed position. Use a negative value if the door swings the wrong way.")]
    public float openAngle = 65f;

    [Tooltip("For scissor doors, the small initial movement away from the body before lifting. Set the direction and distance in this hinge's parent-local space.")]
    public Vector3 scissorPopOutLocalOffset = new Vector3(0.08f, 0f, 0f);

    [Min(0.05f)]
    [Tooltip("Duration of the initial outward movement on a scissor door.")]
    public float scissorPopOutDuration = 0.18f;

    [Min(0.05f)]
    [Tooltip("Duration of the upward rotation on a scissor door.")]
    public float scissorLiftDuration = 0.65f;

    [Min(0.05f)]
    [Tooltip("Duration of a conventional hinged door rotation.")]
    public float animationDuration = 0.45f;

    [Tooltip("Optional keyboard test while playing. Press K by default to open/close this door.")]
    public bool enableKeyboardTest = true;
    public KeyCode testToggleKey = KeyCode.K;

    private Vector3 closedLocalPosition;
    private Quaternion closedLocalRotation;
    private Quaternion openLocalRotation;
    private Coroutine animationRoutine;
    private bool isOpen;

    public Transform InteractionTransform =>
        interactionPoint != null ? interactionPoint : transform;

    public bool IsOpen => isOpen;

    public bool CanEnterAsDriver =>
        isActiveAndEnabled &&
        vehicle != null &&
        vehicle.isActiveAndEnabled &&
        !vehicle.HasDriver &&
        role == DoorRole.Driver;

    private void Awake()
    {
        CacheClosedPose();
    }

    private void Reset()
    {
        FindVehicleIfMissing();
        CacheClosedPose();
    }

    private void OnValidate()
    {
        FindVehicleIfMissing();
        if (localOpenAxis.sqrMagnitude < 0.001f)
            localOpenAxis = Vector3.up;

        animationDuration = Mathf.Max(0.05f, animationDuration);
        scissorPopOutDuration = Mathf.Max(0.05f, scissorPopOutDuration);
        scissorLiftDuration = Mathf.Max(0.05f, scissorLiftDuration);
    }

    private void OnEnable()
    {
        FindVehicleIfMissing();
        CacheClosedPose();

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

    private void CacheClosedPose()
    {
        closedLocalPosition = transform.localPosition;
        closedLocalRotation = transform.localRotation;
        RecalculateOpenRotation();
    }

    private void RecalculateOpenRotation()
    {
        Vector3 axis = localOpenAxis.sqrMagnitude > 0.001f
            ? localOpenAxis.normalized
            : Vector3.up;

        openLocalRotation = closedLocalRotation * Quaternion.AngleAxis(openAngle, axis);
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

        RecalculateOpenRotation();
        animationRoutine = StartCoroutine(AnimateDoorSequence(open));
    }

    private IEnumerator AnimateDoorSequence(bool opening)
    {
        Vector3 poppedOutPosition = closedLocalPosition + scissorPopOutLocalOffset;

        if (motionType == DoorMotionType.Scissor)
        {
            if (opening)
            {
                // Stage 1: move the door slightly away from the body.
                yield return AnimatePosition(poppedOutPosition, scissorPopOutDuration);

                // Stage 2: lift the door around its hinge.
                yield return AnimateRotation(openLocalRotation, scissorLiftDuration);
            }
            else
            {
                // Close in reverse order: lower first, then tuck back against the body.
                yield return AnimateRotation(closedLocalRotation, scissorLiftDuration);
                yield return AnimatePosition(closedLocalPosition, scissorPopOutDuration);
            }
        }
        else
        {
            yield return AnimateRotation(
                opening ? openLocalRotation : closedLocalRotation,
                animationDuration
            );
        }

        isOpen = opening;
        animationRoutine = null;
    }

    private IEnumerator AnimatePosition(Vector3 targetPosition, float duration)
    {
        Vector3 startPosition = transform.localPosition;
        float elapsed = 0f;
        duration = Mathf.Max(0.05f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = t * t * (3f - 2f * t);
            transform.localPosition = Vector3.Lerp(startPosition, targetPosition, smoothT);
            yield return null;
        }

        transform.localPosition = targetPosition;
    }

    private IEnumerator AnimateRotation(Quaternion targetRotation, float duration)
    {
        Quaternion startRotation = transform.localRotation;
        float elapsed = 0f;
        duration = Mathf.Max(0.05f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = t * t * (3f - 2f * t);
            transform.localRotation = Quaternion.Slerp(startRotation, targetRotation, smoothT);
            yield return null;
        }

        transform.localRotation = targetRotation;
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