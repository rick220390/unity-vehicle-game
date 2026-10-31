using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on the root of your Invector player (the object with the Rigidbody).
/// Handles entering vehicles, driving camera control, and exiting vehicles.
/// </summary>
public class PlayerVehicleInteractor : MonoBehaviour
{
[Header("Input")]
public KeyCode interactKey = KeyCode.F;
public KeyCode handbrakeKey = KeyCode.Space;
public KeyCode cameraToggleKey = KeyCode.V;

[Header("Enter / Exit")]
public float enterRange = 3f;
public float maxExitSpeed = 8f;
[Tooltip("Extra space kept between the car body and an automatically chosen fallback exit position.")]
[Min(0.1f)] public float fallbackExitClearance = 0.8f;

[Tooltip("Hide the player renderers while driving. Leave disabled to keep the third-person character visible inside the vehicle.")]
public bool hidePlayerMeshWhileDriving = false;

[Header("Vehicle Animations")]
[Tooltip("Animator on the Invector ThirdPerson root. If empty, this component searches the same GameObject.")]
public Animator vehicleAnimator;
public string vehicleLayerName = "Vehicle";
public string enterTriggerName = "EnterVehicle";
public string exitTriggerName = "ExitVehicle";
public string enteringStateName = "EnteringCar";
public string exitingStateName = "ExitingCar";
[Min(0.1f)] public float entryAnimationFallback = 2.5f;
[Min(0.1f)] public float exitAnimationFallback = 2.5f;
[Min(1f)] public float animationStateTimeout = 10f;

[Header("Disable while driving (drag in!)")]
[Tooltip("Drag in vThirdPersonInput, vThirdPersonController, and vThirdPersonCamera.")]
public MonoBehaviour[] disableWhileDriving;

[Header("Driving Camera")]
public Camera cam;
public float camDistance = 6f;
public float camHeight = 2.5f;
public float camLookHeight = 1.2f;
public float camPositionSmooth = 6f;
public float camRotationSmooth = 5f;

[Header("Camera Collision")]
[Tooltip("Radius used to keep the driving camera away from walls and other obstacles.")]
[Min(0.05f)] public float cameraCollisionRadius = 0.2f;
[Tooltip("Extra space maintained between the camera and an obstacle.")]
[Min(0f)] public float cameraCollisionPadding = 0.12f;
[Tooltip("Layers that can obstruct the driving camera.")]
public LayerMask cameraCollisionMask = ~0;

[Header("Mouse Orbit")]
public float mouseSensitivity = 3f;
public float minCameraPitch = -10f;
public float maxCameraPitch = 65f;

[Header("Cockpit Camera")]
[Tooltip("Yaw adjustment for the cockpit view. Positive values rotate the view to the right.")]
public float cockpitYawOffset = 12f;
[Tooltip("Mouse sensitivity when looking around inside the cockpit.")]
public float cockpitMouseSensitivity = 2.5f;
[Tooltip("Maximum horizontal mouse-look angle to either side of the forward view.")]
[Range(30f, 120f)] public float cockpitLookYawLimit = 85f;
[Tooltip("Maximum vertical mouse-look angle up or down.")]
[Range(15f, 80f)] public float cockpitLookPitchLimit = 45f;
[Tooltip("How quickly the cockpit view settles after head movement.")]
[Min(0.1f)] public float cockpitHeadSmooth = 5f;
[Tooltip("Strength of subtle head movement caused by acceleration, braking and cornering.")]
[Range(0f, 2f)] public float cockpitHeadMovement = 1f;

[Header("Driving HUD")]
[Tooltip("Show speed, RPM, current gear and fuel level only while seated in a vehicle.")]
public bool showDrivingTelemetry = true;
[Tooltip("Display the current vehicle's fuel level when it has a VehicleFuel component.")]
public bool showFuelGauge = true;

private float cameraYaw;
private float cameraPitch = 15f;
private float cockpitLookYaw;
private float cockpitLookPitch;
private Vector3 previousCockpitLocalVelocity;
private bool cockpitVelocityInitialized;
private Vector3 cockpitHeadOffset;
private float cockpitHeadPitch;
private float cockpitHeadRoll;

private Vehicle current;
private bool cockpitView;
private vThirdPersonCamera invectorCam;
private Transform originalParent;
private Vector3 originalLocalScale;
private Rigidbody rb;

private struct BehaviourEnabledState
{
    public MonoBehaviour behaviour;
    public bool wasEnabled;

    public BehaviourEnabledState(MonoBehaviour behaviour, bool wasEnabled)
    {
        this.behaviour = behaviour;
        this.wasEnabled = wasEnabled;
    }
}

private readonly List<BehaviourEnabledState> disabledBehaviourStates =
    new List<BehaviourEnabledState>();
private bool invectorCameraWasEnabled;
private bool playerRigidbodyWasKinematic;
private int vehicleAnimatorLayer = -1;
private bool animatorRootMotionWasEnabled;
private bool handIkActive;
private float handIkCurrentWeight;

private readonly List<Collider> hiddenColliders = new List<Collider>();
private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

private Vehicle nearby;
private VehicleDoor nearbyDoor;
private string message;
private float messageTimer;
private readonly RaycastHit[] cameraCastHits = new RaycastHit[32];

void Awake()
{
    rb = GetComponent<Rigidbody>();

    if (vehicleAnimator == null)
        vehicleAnimator = GetComponent<Animator>();

    if (vehicleAnimator != null)
    {
        vehicleAnimatorLayer = vehicleAnimator.GetLayerIndex(vehicleLayerName);
        animatorRootMotionWasEnabled = vehicleAnimator.applyRootMotion;

        if (vehicleAnimatorLayer >= 0)
            vehicleAnimator.SetLayerWeight(vehicleAnimatorLayer, 0f);
        else
            Debug.LogWarning($"PlayerVehicleInteractor: Animator layer '{vehicleLayerName}' was not found.", this);
    }
    else
    {
        Debug.LogWarning("PlayerVehicleInteractor: No Animator found on the player root. Vehicle animations will use timed fallbacks.", this);
    }

#if UNITY_2023_1_OR_NEWER
invectorCam = FindFirstObjectByType<vThirdPersonCamera>();
#else
invectorCam = FindObjectOfType<vThirdPersonCamera>();
#endif

    if (invectorCam != null)
    {
        Camera invectorCamera = invectorCam.GetComponent<Camera>();

        if (invectorCamera != null)
            cam = invectorCamera;
    }

    if (cam == null)
        cam = Camera.main;
}

void OnAnimatorIK(int layerIndex)
{
    // Unity calls this only for Animator layers with IK Pass enabled.
    if (vehicleAnimator == null || layerIndex != vehicleAnimatorLayer)
        return;

    float targetWeight = current != null && handIkActive
        ? current.driverHandIkWeight
        : 0f;
    handIkCurrentWeight = Mathf.MoveTowards(
        handIkCurrentWeight,
        targetWeight,
        8f * Time.deltaTime
    );

    ApplyHandIk(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow,
        current != null ? current.driverLeftHandTarget : null,
        current != null ? current.driverLeftElbowHint : null);
    ApplyHandIk(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow,
        current != null ? current.driverRightHandTarget : null,
        current != null ? current.driverRightElbowHint : null);
}

void ApplyHandIk(AvatarIKGoal hand, AvatarIKHint elbow, Transform handTarget, Transform elbowHint)
{
    float weight = handTarget != null ? handIkCurrentWeight : 0f;
    vehicleAnimator.SetIKPositionWeight(hand, weight);
    vehicleAnimator.SetIKRotationWeight(hand, weight);

    if (handTarget != null)
    {
        vehicleAnimator.SetIKPosition(hand, handTarget.position);
        vehicleAnimator.SetIKRotation(hand, handTarget.rotation);
    }

    float hintWeight = elbowHint != null ? weight : 0f;
    vehicleAnimator.SetIKHintPositionWeight(elbow, hintWeight);
    if (elbowHint != null)
        vehicleAnimator.SetIKHintPosition(elbow, elbowHint.position);
}

void Update()
{
    if (current == null)
    {
        // Door markers take priority. Vehicles with configured doors are not
        // enterable from their body; vehicles without door markers retain the
        // current proximity-based interaction until they are configured later.
        nearbyDoor = FindNearbyDoor();
        nearby = nearbyDoor != null
            ? nearbyDoor.vehicle
            : FindNearbyVehicle();

        if (nearby != null && Input.GetKeyDown(interactKey))
        {
            if (nearbyDoor != null)
                StartCoroutine(EnterThroughDoor(nearbyDoor));
            else
                Enter(nearby, null);
        }
    }
    else
    {
        nearby = null;
        nearbyDoor = null;

        // Switch between the existing chase camera and the vehicle's cockpit point.
        if (Input.GetKeyDown(cameraToggleKey))
            cockpitView = !cockpitView;

        // Vehicle driving controls.
        // Use raw vertical input for driving so releasing throttle and pressing
        // brake/reverse is not delayed by Unity's axis smoothing.
        float verticalInput = Input.GetAxisRaw("Vertical");
        float serviceBrakeInput = verticalInput < 0f && current.ForwardSpeed > 0.5f
            ? Mathf.Clamp01(-verticalInput)
            : 0f;

        current.SetInput(
            verticalInput,
            Input.GetAxis("Horizontal"),
            Input.GetKey(handbrakeKey),
            serviceBrakeInput
        );

        if (Input.GetKeyDown(interactKey))
            StartCoroutine(TryExitThroughDoor());
    }

    if (messageTimer > 0f)
        messageTimer -= Time.deltaTime;
}

// ---------------- Driving Camera ----------------

void LateUpdate()
{
    if (current == null || cam == null)
        return;

    // Cockpit view follows the vehicle's assigned camera point. Configure
    // Vehicle.cameraPoint on each car prefab for the correct driver eye position.
    if (cockpitView)
    {
        Transform cockpit = current.cameraPoint != null
            ? current.cameraPoint
            : current.seatPoint;

        // Independent mouse-look for the cockpit. The chase camera keeps its
        // existing orbit controls when cockpit view is switched off.
        cockpitLookYaw += Input.GetAxis("Mouse X") * cockpitMouseSensitivity;
        cockpitLookPitch -= Input.GetAxis("Mouse Y") * cockpitMouseSensitivity;
        cockpitLookYaw = Mathf.Clamp(cockpitLookYaw, -cockpitLookYawLimit, cockpitLookYawLimit);
        cockpitLookPitch = Mathf.Clamp(cockpitLookPitch, -cockpitLookPitchLimit, cockpitLookPitchLimit);

        UpdateCockpitHeadMovement();

        if (cockpit != null)
        {
            Quaternion baseRotation = cockpit.rotation * Quaternion.Euler(0f, cockpitYawOffset, 0f);
            Vector3 headPosition = cockpit.position + cockpit.rotation * cockpitHeadOffset;
            Quaternion lookRotation = baseRotation
                * Quaternion.Euler(cockpitHeadPitch + cockpitLookPitch, cockpitLookYaw, cockpitHeadRoll);

            cam.transform.SetPositionAndRotation(headPosition, lookRotation);
        }
        else
        {
            Quaternion baseRotation = current.transform.rotation * Quaternion.Euler(0f, cockpitYawOffset, 0f);
            cam.transform.SetPositionAndRotation(
                current.transform.position + Vector3.up,
                baseRotation * Quaternion.Euler(cockpitHeadPitch + cockpitLookPitch, cockpitLookYaw, cockpitHeadRoll)
            );
        }

        return;
    }

    // Rotate the camera with the mouse.
    cameraYaw += Input.GetAxis("Mouse X") * mouseSensitivity;
    cameraPitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;

    cameraPitch = Mathf.Clamp(
        cameraPitch,
        minCameraPitch,
        maxCameraPitch
    );

    // Keep the camera's position relative to the vehicle root so an offset
    // cameraPoint cannot pull the whole driving view away from the car.
    // The configured cameraPoint is used only as the look target.
    Vector3 orbitPivot = current.transform.position
                       + Vector3.up * camLookHeight;

    Vector3 lookTarget = current.cameraPoint != null
        ? current.cameraPoint.position
        : orbitPivot;

    // Avoid aiming steeply down at a low cameraPoint. Keep the target a little
    // above the vehicle's normal orbit pivot so more of the road ahead is visible.
    lookTarget.y = Mathf.Max(lookTarget.y, orbitPivot.y + 0.6f);

    // Calculate the camera's orbit around the vehicle using the existing distance
    // and height settings, preserving the framing that worked before cameraPoint
    // was connected.
    Quaternion orbitRotation = Quaternion.Euler(
        cameraPitch,
        current.transform.eulerAngles.y + cameraYaw,
        0f
    );

    // Keep the camera closer than the original default, but give enough
    // distance to see the road ahead and avoid an overly steep viewing angle.
    // The multiplier also applies to existing scenes with serialized camDistance = 6.
    float effectiveCamDistance = Mathf.Max(1f, camDistance * 0.85f);

    Vector3 desiredPos = orbitPivot
                       - orbitRotation * Vector3.forward * effectiveCamDistance
                       + Vector3.up * (camHeight - camLookHeight);

    // Prevent the camera from passing through walls, buildings, or nearby
    // props. Ignore colliders belonging to this car and the player itself.
    Vector3 cameraTargetPosition = desiredPos;
    Vector3 castVector = desiredPos - orbitPivot;
    float castDistance = castVector.magnitude;
    bool cameraBlocked = false;

    if (castDistance > 0.001f)
    {
        Vector3 castDirection = castVector / castDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            orbitPivot,
            Mathf.Max(0.05f, cameraCollisionRadius),
            castDirection,
            cameraCastHits,
            castDistance,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        float nearestHitDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = cameraCastHits[i].collider;
            if (hitCollider == null)
                continue;

            // The cast begins at the car, so its own body and wheel colliders
            // must not pull the camera into the vehicle.
            if (hitCollider.GetComponentInParent<Vehicle>() == current)
                continue;

            if (hitCollider.transform == transform ||
                hitCollider.transform.IsChildOf(transform))
                continue;

            if (cameraCastHits[i].distance < nearestHitDistance)
                nearestHitDistance = cameraCastHits[i].distance;
        }

        if (!float.IsInfinity(nearestHitDistance))
        {
            float safeDistance = Mathf.Max(
                0f,
                nearestHitDistance - Mathf.Max(0f, cameraCollisionPadding)
            );

            cameraTargetPosition = orbitPivot + castDirection * safeDistance;
            cameraBlocked = true;
        }
    }

    // Move inward immediately when an obstacle is detected so the camera cannot
    // linger inside geometry. Smooth the return to the normal orbit after it clears.
    if (cameraBlocked)
    {
        cam.transform.position = cameraTargetPosition;
    }
    else
    {
        float positionBlend = 1f - Mathf.Exp(
            -camPositionSmooth * Time.deltaTime
        );

        cam.transform.position = Vector3.Lerp(
            cam.transform.position,
            cameraTargetPosition,
            positionBlend
        );
    }

    float rotationBlend = 1f - Mathf.Exp(
        -camRotationSmooth * Time.deltaTime
    );

    // Aim at the configured camera point without changing the camera's orbit distance.
    Vector3 lookDirection = lookTarget - cam.transform.position;

    if (lookDirection.sqrMagnitude > 0.001f)
    {
        Quaternion desiredRotation = Quaternion.LookRotation(
            lookDirection,
            Vector3.up
        );

        cam.transform.rotation = Quaternion.Slerp(
            cam.transform.rotation,
            desiredRotation,
            rotationBlend
        );
    }
}

// ---------------- Cockpit Head Movement ----------------

void UpdateCockpitHeadMovement()
{
    float dt = Mathf.Max(Time.deltaTime, 0.0001f);
    Vector3 targetOffset = Vector3.zero;
    float targetPitch = 0f;
    float targetRoll = 0f;

    Rigidbody vehicleBody = current != null ? current.GetComponent<Rigidbody>() : null;
    if (vehicleBody != null && cockpitHeadMovement > 0f)
    {
#if UNITY_6000_0_OR_NEWER
        Vector3 worldVelocity = vehicleBody.linearVelocity;
#else
        Vector3 worldVelocity = vehicleBody.velocity;
#endif
        Vector3 localVelocity = current.transform.InverseTransformDirection(worldVelocity);

        if (cockpitVelocityInitialized)
        {
            Vector3 localAcceleration = (localVelocity - previousCockpitLocalVelocity) / dt;

            // Inertia: the driver's head subtly shifts opposite the car's
            // acceleration, with a small pitch response under throttle/braking.
            targetOffset.x = Mathf.Clamp(-localAcceleration.x * 0.0025f, -0.055f, 0.055f);
            targetOffset.z = Mathf.Clamp(-localAcceleration.z * 0.0018f, -0.045f, 0.045f);
            targetPitch = Mathf.Clamp(-localAcceleration.z * 0.45f, -3.5f, 3.5f);
            targetRoll = Mathf.Clamp(-localAcceleration.x * 0.7f, -4f, 4f);

            // A restrained additional lean while cornering, using the vehicle's
            // yaw rate. Keep it small so the view remains comfortable to drive.
            targetRoll += Mathf.Clamp(-vehicleBody.angularVelocity.y * 1.5f, -3f, 3f);
            targetRoll = Mathf.Clamp(targetRoll, -5f, 5f);
        }

        previousCockpitLocalVelocity = localVelocity;
        cockpitVelocityInitialized = true;
    }
    else
    {
        cockpitVelocityInitialized = false;
    }

    float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, cockpitHeadSmooth) * dt);
    cockpitHeadOffset = Vector3.Lerp(cockpitHeadOffset, targetOffset * cockpitHeadMovement, blend);
    cockpitHeadPitch = Mathf.Lerp(cockpitHeadPitch, targetPitch * cockpitHeadMovement, blend);
    cockpitHeadRoll = Mathf.Lerp(cockpitHeadRoll, targetRoll * cockpitHeadMovement, blend);
}

// ---------------- Finding a Vehicle ----------------

VehicleDoor FindNearbyDoor()
{
    VehicleDoor best = null;
    float bestDist = enterRange;

    foreach (VehicleDoor door in VehicleDoor.All)
    {
        if (door == null || !door.CanEnterAsDriver)
            continue;

        float distance = door.DistanceToPoint(transform.position);
        if (distance < bestDist)
        {
            best = door;
            bestDist = distance;
        }
    }

    return best;
}

Vehicle FindNearbyVehicle()
{
    Vehicle best = null;
    float bestDist = enterRange;

    foreach (Vehicle v in Vehicle.All)
    {
        if (v == null || v.HasDriver || VehicleDoor.HasAnyDoorFor(v))
            continue;

        // Measure from the nearest physical collider, not just the root pivot.
        // This also supports imported vehicles whose colliders are on child objects.
        float d = v.DistanceToPoint(transform.position);

        if (d < bestDist)
        {
            best = v;
            bestDist = d;
        }
    }

    return best;
}

// ---------------- Enter Vehicle ----------------

void Enter(Vehicle v, Transform seatPointOverride)
{
    if (v == null || !v.TryReserve())
        return;

    EnterReserved(v, seatPointOverride);
}

IEnumerator EnterThroughDoor(VehicleDoor door)
{
    if (door == null || !door.CanEnterAsDriver || door.vehicle == null)
        yield break;

    Vehicle targetVehicle = door.vehicle;
    if (!targetVehicle.TryReserve())
        yield break;

    // Prepare the player and reserve the vehicle before either animation starts.
    handIkActive = false;
    EnterReserved(targetVehicle, door.seatPointOverride);
    handIkActive = false;

    // Start the character at a dedicated marker beside the driver's door.
    // The marker's forward direction should face the car/seat.
    Transform entryPoint = door.entryPoint;
    if (entryPoint != null)
    {
        transform.SetParent(originalParent, true);
        transform.SetPositionAndRotation(entryPoint.position, entryPoint.rotation);
        transform.localScale = originalLocalScale;
    }
    else
    {
        Vector3 side = Vector3.ProjectOnPlane(targetVehicle.transform.right, Vector3.up).normalized;
        if (side.sqrMagnitude < 0.001f)
            side = Vector3.right;

        Vector3 startPosition = door.InteractionTransform.position + side * 0.8f;
        Quaternion faceCar = Quaternion.LookRotation(-side, Vector3.up);
        transform.SetParent(originalParent, true);
        transform.SetPositionAndRotation(startPosition, faceCar);
        transform.localScale = originalLocalScale;
        Debug.LogWarning("VehicleDoor has no Entry Point assigned. Using an approximate position beside the vehicle.", door);
    }

    // Re-enable Invector's root-motion receiver for the transition. Input
    // remains locked, so this restores the animation's authored turning without
    // allowing the player to walk around with WASD.
    SetInvectorRootMotionBehavioursEnabled(true);

    // Keep the physical door movement synchronized with the authored
    // character animation. Start the character animation immediately, then
    // open the door at 1.20s and begin closing it at 3.26s.
    Coroutine entryAnimation = StartCoroutine(PlayVehicleAnimation(
        enterTriggerName,
        enteringStateName,
        entryAnimationFallback,
        4f
    ));

    const float doorOpenAt = 1.20f;
    const float doorCloseAt = 3.26f;
    yield return new WaitForSeconds(doorOpenAt);
    door.OpenDoor();

    yield return new WaitForSeconds(Mathf.Max(0f, doorCloseAt - doorOpenAt));
    door.CloseDoor();

    // The entry animation's four-second handover remains authoritative. The
    // door has already started closing and should be shut before seat handover.
    yield return entryAnimation;

    SetInvectorRootMotionBehavioursEnabled(false);

    // Root motion is useful during the short transition, but should not move
    // the seated player around while the vehicle is being driven.
    if (vehicleAnimator != null)
        vehicleAnimator.applyRootMotion = false;

    AttachPlayerToSeat(targetVehicle, door.seatPointOverride, door);

    // Let the character settle into the seat before closing the physical door.
    yield return new WaitForSeconds(0.15f);
    door.CloseDoor();
}

void EnterReserved(Vehicle v, Transform seatPointOverride)
{
    current = v;

    // Start in chase view whenever a vehicle is entered.
    cockpitView = false;
    cameraYaw = 0f;
    cameraPitch = 15f;
    cockpitLookYaw = 0f;
    cockpitLookPitch = 0f;
    cockpitHeadOffset = Vector3.zero;
    cockpitHeadPitch = 0f;
    cockpitHeadRoll = 0f;
    cockpitVelocityInitialized = false;

    originalParent = transform.parent;
    originalLocalScale = transform.localScale;
    playerRigidbodyWasKinematic = rb != null && rb.isKinematic;

    // Remember each component's original state. Some components may have been
    // intentionally disabled before entering, so we must not blindly enable all
    // of them again when the player exits.
    disabledBehaviourStates.Clear();
    if (disableWhileDriving != null)
    {
        foreach (MonoBehaviour m in disableWhileDriving)
            TrackAndDisableMovementBehaviour(m);
    }

    // Do not rely only on the Inspector array: missing one Invector input or
    // controller component allows WASD to keep moving the character during
    // the enter animation and while seated. Automatically lock the standard
    // Invector movement components anywhere under this player hierarchy.
    MonoBehaviour[] playerBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
    foreach (MonoBehaviour behaviour in playerBehaviours)
    {
        if (behaviour == null || behaviour == vehicleAnimator)
            continue;

        string typeName = behaviour.GetType().Name;
        bool isInvectorMovement =
            typeName == "vThirdPersonInput" ||
            typeName == "vThirdPersonController" ||
            typeName == "vThirdPersonMotor";

        if (isInvectorMovement)
            TrackAndDisableMovementBehaviour(behaviour);
    }

    invectorCameraWasEnabled = invectorCam != null && invectorCam.enabled;
    if (invectorCam != null)
        invectorCam.enabled = false;

    SetPlayerPhysics(false);
    if (hidePlayerMeshWhileDriving)
        SetPlayerVisible(false);

    AttachPlayerToSeat(v, seatPointOverride);
}

void TrackAndDisableMovementBehaviour(MonoBehaviour behaviour)
{
    if (behaviour == null || behaviour == vehicleAnimator)
        return;

    // Keep Invector's controller and motor available for animation root motion.
    // Remember their original states, but do not disable them here. They are
    // explicitly enabled for entry/exit and disabled only while seated.
    string typeName = behaviour.GetType().Name;
    if (typeName == "vThirdPersonController" ||
        typeName == "vThirdPersonMotor")
    {
        TrackInvectorTransitionBehaviour(behaviour);
        return;
    }

    foreach (BehaviourEnabledState state in disabledBehaviourStates)
    {
        if (state.behaviour == behaviour)
        {
            behaviour.enabled = false;
            return;
        }
    }

    disabledBehaviourStates.Add(new BehaviourEnabledState(behaviour, behaviour.enabled));
    behaviour.enabled = false;
}

void TrackInvectorTransitionBehaviour(MonoBehaviour behaviour)
{
    if (behaviour == null)
        return;

    foreach (BehaviourEnabledState state in disabledBehaviourStates)
    {
        if (state.behaviour == behaviour)
            return;
    }

    // Store the Inspector state so it can be restored when the player exits.
    disabledBehaviourStates.Add(new BehaviourEnabledState(behaviour, behaviour.enabled));
}

void SetInvectorRootMotionBehavioursEnabled(bool enabled)
{
    // Invector's controller/motor can receive Animator root motion through
    // OnAnimatorMove. Force both on for the authored doorway animation, even if
    // an earlier experiment left one disabled in the Inspector. During driving
    // they are turned off; RestoreDrivingComponents returns their original states
    // when the player exits. vThirdPersonInput remains locked throughout.
    foreach (BehaviourEnabledState state in disabledBehaviourStates)
    {
        if (state.behaviour == null)
            continue;

        string typeName = state.behaviour.GetType().Name;
        if (typeName == "vThirdPersonController" ||
            typeName == "vThirdPersonMotor")
            state.behaviour.enabled = enabled;
    }
}

void AttachPlayerToSeat(Vehicle v, Transform seatPointOverride, VehicleDoor alignmentDoor = null)
{
    if (v == null)
        return;

    // A door's seat override takes priority over the vehicle's general seat point.
    // Keep the character transform relative to this marker so each vehicle can
    // be aligned precisely in the Inspector without changing the Mixamo clips.
    Transform seat = seatPointOverride != null
        ? seatPointOverride
        : (v.seatPoint != null ? v.seatPoint : v.transform);

    Vector3 positionOffset = alignmentDoor != null
        ? alignmentDoor.seatedLocalPositionOffset
        : Vector3.zero;
    Vector3 rotationOffset = alignmentDoor != null
        ? alignmentDoor.seatedLocalRotationOffset
        : Vector3.zero;

    transform.SetParent(seat, false);
    transform.localPosition = positionOffset;
    transform.localRotation = Quaternion.Euler(rotationOffset);
    handIkActive = true;
}

bool HasAnimatorTrigger(string triggerName)
{
    if (vehicleAnimator == null || string.IsNullOrEmpty(triggerName))
        return false;

    foreach (AnimatorControllerParameter parameter in vehicleAnimator.parameters)
    {
        if (parameter.name == triggerName &&
            parameter.type == AnimatorControllerParameterType.Trigger)
            return true;
    }

    return false;
}

IEnumerator PlayVehicleAnimation(string triggerName, string stateName, float fallbackDuration, float handoverDuration = -1f)
{
    if (vehicleAnimator == null ||
        !vehicleAnimator.isActiveAndEnabled ||
        vehicleAnimatorLayer < 0 ||
        !HasAnimatorTrigger(triggerName))
    {
        float waitDuration = handoverDuration > 0f ? handoverDuration : fallbackDuration;
        yield return new WaitForSeconds(Mathf.Max(0.1f, waitDuration));
        yield break;
    }

    // Allow the small authored root translation in the Mixamo enter/exit clips.
    // The player is snapped to the exact seat/doorway marker at the end.
    vehicleAnimator.applyRootMotion = true;
    vehicleAnimator.SetLayerWeight(vehicleAnimatorLayer, 1f);
    vehicleAnimator.ResetTrigger(exitTriggerName);
    vehicleAnimator.ResetTrigger(enterTriggerName);
    vehicleAnimator.SetTrigger(triggerName);

    // Entry/exit handovers use the measured clip durations rather than waiting
    // for state-transition polling to finish a few frames late. This keeps the
    // player/vehicle attachment change synchronized with the visible animation.
    if (handoverDuration > 0f)
    {
        yield return new WaitForSeconds(handoverDuration);
        yield break;
    }

    // Wait for the requested state to become active. A timeout prevents a
    // missing/misnamed Animator state from trapping the player indefinitely.
    float elapsed = 0f;
    bool stateStarted = false;

    while (elapsed < animationStateTimeout)
    {
        if (vehicleAnimator == null || !vehicleAnimator.isActiveAndEnabled)
            break;

        AnimatorStateInfo state = vehicleAnimator.GetCurrentAnimatorStateInfo(vehicleAnimatorLayer);
        if (state.IsName(stateName))
        {
            stateStarted = true;
            break;
        }

        if (vehicleAnimator.IsInTransition(vehicleAnimatorLayer))
        {
            AnimatorStateInfo next = vehicleAnimator.GetNextAnimatorStateInfo(vehicleAnimatorLayer);
            if (next.IsName(stateName))
            {
                stateStarted = true;
                break;
            }
        }

        elapsed += Time.deltaTime;
        yield return null;
    }

    if (!stateStarted)
    {
        Debug.LogWarning($"Vehicle animation state '{stateName}' did not start. Using the configured fallback duration.", this);
        yield return new WaitForSeconds(Mathf.Max(0.1f, fallbackDuration));
        yield break;
    }

    // Wait until the clip has played almost to completion. Entry then transitions
    // naturally into the Driving state; exit remains in its final pose until restored.
    elapsed = 0f;
    while (elapsed < animationStateTimeout)
    {
        if (vehicleAnimator == null || !vehicleAnimator.isActiveAndEnabled)
            yield break;

        AnimatorStateInfo state = vehicleAnimator.GetCurrentAnimatorStateInfo(vehicleAnimatorLayer);
        if (state.IsName(stateName) && state.normalizedTime >= 0.95f)
            yield break;

        // The entry transition is configured to move directly into Driving.
        if (stateName == enteringStateName && state.IsName("Driving"))
            yield break;

        elapsed += Time.deltaTime;
        yield return null;
    }

    Debug.LogWarning($"Vehicle animation state '{stateName}' did not finish before timeout.", this);
}

// ---------------- Exit Vehicle ----------------

IEnumerator TryExitThroughDoor()
{
    if (current == null)
        yield break;

    if (current.Speed > maxExitSpeed)
    {
        Say("Slow down to get out");
        yield break;
    }

    Vehicle exitingVehicle = current;
    VehicleDoor driverDoor = FindDriverDoor(exitingVehicle);

    // Prefer the driver's dedicated ExitPoint. If it is missing or obstructed,
    // retain compatibility with the EntryPoint and the vehicle's generic exits.
    Transform configuredExit = driverDoor != null && driverDoor.exitPoint != null
        ? driverDoor.exitPoint
        : (driverDoor != null ? driverDoor.entryPoint : null);

    Vector3 exitPosition;
    Quaternion exitRotation;
    bool hasConfiguredExit = configuredExit != null &&
        IsSpotFree(configuredExit.position, exitingVehicle);

    if (hasConfiguredExit)
    {
        exitPosition = configuredExit.position;
        exitRotation = configuredExit.rotation;
    }
    else if (!TryFindExitPosition(exitingVehicle, out exitPosition))
    {
        Say("No safe place to exit here");
        yield break;
    }
    else
    {
        Vector3 flatFwd = Vector3.ProjectOnPlane(exitingVehicle.transform.forward, Vector3.up);
        exitRotation = flatFwd.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(flatFwd, Vector3.up)
            : Quaternion.identity;
    }

    // Release the wheel before the exit animation starts.
    handIkActive = false;

    // Detach immediately when F is pressed, preserving the player's current
    // world pose. The exit animation must no longer inherit the vehicle's motion.
    if (current != exitingVehicle)
        yield break;

    transform.SetParent(originalParent, true);

    // Restore only the Invector root-motion receiver for the exit clip.
    // Input remains locked until the character reaches the ExitPoint.
    SetInvectorRootMotionBehavioursEnabled(true);

    if (driverDoor != null)
        driverDoor.OpenDoor();

    // The exit clip takes approximately two seconds. At that point place the
    // player exactly at the configured ExitPoint and restore normal control.
    yield return PlayVehicleAnimation(
        exitTriggerName,
        exitingStateName,
        exitAnimationFallback,
        2f
    );

    SetInvectorRootMotionBehavioursEnabled(false);

    transform.SetPositionAndRotation(exitPosition, exitRotation);
    transform.localScale = originalLocalScale;

    // The character has now completed the exit animation. Restore normal control.
    if (vehicleAnimator != null && vehicleAnimatorLayer >= 0)
        vehicleAnimator.SetLayerWeight(vehicleAnimatorLayer, 0f);

    if (vehicleAnimator != null)
        vehicleAnimator.applyRootMotion = animatorRootMotionWasEnabled;

    SetPlayerVisible(true);
    SetPlayerPhysics(true);
    RestoreDrivingComponents();

    if (invectorCam != null)
        invectorCam.enabled = invectorCameraWasEnabled;

    current = null;
    exitingVehicle.Release();
    cockpitView = false;

    if (driverDoor != null)
    {
        yield return new WaitForSeconds(0.15f);
        driverDoor.CloseDoor();
    }
}

VehicleDoor FindDriverDoor(Vehicle targetVehicle)
{
    if (targetVehicle == null)
        return null;

    foreach (VehicleDoor door in VehicleDoor.All)
    {
        if (door != null &&
            door.isActiveAndEnabled &&
            door.vehicle == targetVehicle &&
            door.role == VehicleDoor.DoorRole.Driver)
            return door;
    }

    return null;
}

void RestoreDrivingComponents()
{
    foreach (BehaviourEnabledState state in disabledBehaviourStates)
    {
        if (state.behaviour != null)
            state.behaviour.enabled = state.wasEnabled;
    }

    disabledBehaviourStates.Clear();
}

bool TryFindExitPosition(Vehicle v, out Vector3 position)
{
    position = Vector3.zero;

    // Prefer the exit points already configured on this vehicle.
    // These remain the authoritative positions when they are clear.
    if (v.exitPoints != null)
    {
        foreach (Transform exitPoint in v.exitPoints)
        {
            if (exitPoint == null)
                continue;

            if (IsSpotFree(exitPoint.position, v))
            {
                position = exitPoint.position;
                return true;
            }
        }
    }

    // If configured exits are blocked, try positions around the vehicle's
    // physical footprint rather than placing the player above the roof.
    float halfWidth = GetVehicleHalfExtent(v, v.transform.right);
    float halfLength = GetVehicleHalfExtent(v, v.transform.forward);
    float clearance = Mathf.Max(0.1f, fallbackExitClearance);

    // Use the height of the configured exit points when available. This keeps
    // fallback placements aligned with the intended standing level.
    float exitHeight = v.transform.position.y;
    if (v.exitPoints != null)
    {
        foreach (Transform exitPoint in v.exitPoints)
        {
            if (exitPoint != null)
            {
                exitHeight = exitPoint.position.y;
                break;
            }
        }
    }

    Vector3 right = Vector3.ProjectOnPlane(v.transform.right, Vector3.up).normalized;
    Vector3 forward = Vector3.ProjectOnPlane(v.transform.forward, Vector3.up).normalized;

    if (right.sqrMagnitude < 0.001f)
        right = Vector3.right;
    if (forward.sqrMagnitude < 0.001f)
        forward = Vector3.forward;

    Vector3 origin = v.transform.position;
    origin.y = exitHeight;

    Vector3[] candidates =
    {
        origin + right * (halfWidth + clearance),
        origin - right * (halfWidth + clearance),
        origin + forward * (halfLength + clearance),
        origin - forward * (halfLength + clearance)
    };

    foreach (Vector3 candidate in candidates)
    {
        if (!IsSpotFree(candidate, v))
            continue;

        position = candidate;
        return true;
    }

    // Never force an exit into an unchecked roof position or an occupied space.
    return false;
}

float GetVehicleHalfExtent(Vehicle v, Vector3 axis)
{
    Collider[] colliders = v.GetComponentsInChildren<Collider>(true);
    Vector3 origin = v.transform.position;
    axis = Vector3.ProjectOnPlane(axis, Vector3.up).normalized;

    if (axis.sqrMagnitude < 0.001f)
        return 1.5f;

    float maximum = 0f;

    foreach (Collider vehicleCollider in colliders)
    {
        if (vehicleCollider == null ||
            !vehicleCollider.enabled ||
            !vehicleCollider.gameObject.activeInHierarchy ||
            vehicleCollider.isTrigger ||
            vehicleCollider is WheelCollider)
            continue;

        Bounds bounds = vehicleCollider.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        // Project all corners of the collider's world-space bounds onto the
        // car's local forward/right direction. This works with rotated cars too.
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z)
                    );

                    float projectedDistance = Mathf.Abs(
                        Vector3.Dot(corner - origin, axis)
                    );

                    maximum = Mathf.Max(maximum, projectedDistance);
                }
            }
        }
    }

    // A sensible fallback for prototype vehicles without physical colliders.
    return maximum > 0.01f ? maximum : 1.5f;
}

bool IsSpotFree(Vector3 p, Vehicle v)
{
    // This runs only when the player requests to exit, so an allocating query
    // is fine and avoids silently truncating results around complex vehicles.
    Collider[] nearbyColliders = Physics.OverlapCapsule(
        p + Vector3.up * 0.5f,
        p + Vector3.up * 1.5f,
        0.3f,
        ~0,
        QueryTriggerInteraction.Ignore
    );

    foreach (Collider c in nearbyColliders)
    {
        if (c == null)
            continue;

        if (c.GetComponentInParent<Vehicle>() == v)
            continue;

        if (c.transform == transform || c.transform.IsChildOf(transform))
            continue;

        return false;
    }

    return true;
}

// ---------------- Player Physics ----------------

void SetPlayerPhysics(bool active)
{
    if (!active)
    {
        hiddenColliders.Clear();

        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            if (c.enabled)
            {
                hiddenColliders.Add(c);
                c.enabled = false;
            }
        }

        if (rb != null)
        {
            ZeroVelocity();
            rb.isKinematic = true;
        }
    }
    else
    {
        foreach (Collider c in hiddenColliders)
        {
            if (c != null)
                c.enabled = true;
        }

        hiddenColliders.Clear();

        if (rb != null)
        {
            rb.isKinematic = playerRigidbodyWasKinematic;
            ZeroVelocity();
        }
    }
}

// ---------------- Player Visibility ----------------

void SetPlayerVisible(bool visible)
{
    if (!visible)
    {
        hiddenRenderers.Clear();

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r.enabled)
            {
                hiddenRenderers.Add(r);
                r.enabled = false;
            }
        }
    }
    else
    {
        foreach (Renderer r in hiddenRenderers)
        {
            if (r != null)
                r.enabled = true;
        }

        hiddenRenderers.Clear();
    }
}

void ZeroVelocity()
{
    if (rb == null)
        return;

#if UNITY_6000_0_OR_NEWER
rb.linearVelocity = Vector3.zero;
#else
rb.velocity = Vector3.zero;
#endif

    rb.angularVelocity = Vector3.zero;
}

// ---------------- Prompt ----------------

void Say(string text)
{
    message = text;
    messageTimer = 2f;
}

void OnGUI()
{
    // Driving telemetry is only drawn while this player is actually inside a vehicle.
    if (current != null && showDrivingTelemetry)
        DrawDrivingTelemetry();

    string text = null;

    if (messageTimer > 0f)
        text = message;
    else if (current != null)
        text = $"[{interactKey}] Exit    [Space] Handbrake    [{cameraToggleKey}] {(cockpitView ? "External view" : "Cockpit view")}";
    else if (nearbyDoor != null)
        text = $"[{interactKey}] Enter Driver Door";
    else if (nearby != null)
        text = $"[{interactKey}] Enter";

    if (text == null)
        return;

    GUIStyle style = new GUIStyle(GUI.skin.label)
    {
        alignment = TextAnchor.MiddleCenter,
        fontSize = 22,
        fontStyle = FontStyle.Bold
    };

    style.normal.textColor = Color.white;

    GUI.Label(
        new Rect(0, Screen.height - 120, Screen.width, 40),
        text,
        style
    );
}

void DrawDrivingTelemetry()
{
    if (current == null)
        return;

    VehicleFuel fuel = current.GetComponent<VehicleFuel>();
    bool drawFuel = showFuelGauge && fuel != null;
    const float panelWidth = 250f;
    float panelHeight = drawFuel ? 166f : 132f;
    float margin = 22f;
    Rect panel = new Rect(
        Mathf.Max(8f, Screen.width - panelWidth - margin),
        Mathf.Max(8f, Screen.height - panelHeight - margin),
        panelWidth,
        panelHeight
    );

    Color previousColor = GUI.color;
    GUI.color = new Color(0.035f, 0.045f, 0.06f, 0.88f);
    GUI.Box(panel, GUIContent.none);
    GUI.color = previousColor;

    GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
    {
        alignment = TextAnchor.MiddleLeft,
        fontSize = 11,
        fontStyle = FontStyle.Bold
    };
    headerStyle.normal.textColor = new Color(0.72f, 0.78f, 0.84f);

    GUIStyle speedStyle = new GUIStyle(GUI.skin.label)
    {
        alignment = TextAnchor.MiddleLeft,
        fontSize = 42,
        fontStyle = FontStyle.Bold
    };
    speedStyle.normal.textColor = Color.white;

    GUIStyle unitStyle = new GUIStyle(GUI.skin.label)
    {
        alignment = TextAnchor.MiddleLeft,
        fontSize = 12,
        fontStyle = FontStyle.Bold
    };
    unitStyle.normal.textColor = new Color(0.72f, 0.78f, 0.84f);

    GUIStyle gearStyle = new GUIStyle(GUI.skin.label)
    {
        alignment = TextAnchor.MiddleCenter,
        fontSize = 24,
        fontStyle = FontStyle.Bold
    };
    gearStyle.normal.textColor = new Color(0.55f, 0.9f, 1f);

    GUI.Label(new Rect(panel.x + 14f, panel.y + 8f, 100f, 18f), "SPEED", headerStyle);
    float speedKph = current.Speed * 3.6f;
    GUI.Label(
        new Rect(panel.x + 12f, panel.y + 23f, 145f, 53f),
        Mathf.RoundToInt(speedKph).ToString(),
        speedStyle
    );
    GUI.Label(new Rect(panel.x + 16f, panel.y + 74f, 55f, 18f), "KM/H", unitStyle);

    GUI.Label(
        new Rect(panel.x + panelWidth - 76f, panel.y + 31f, 58f, 38f),
        "G" + Mathf.Max(1, current.CurrentGear),
        gearStyle
    );

    float redline = Mathf.Max(current.idleRpm + 1f, current.redlineRpm);
    float rpm = Mathf.Clamp(current.EngineRpm, 0f, redline);
    float rpmFraction = Mathf.Clamp01(rpm / redline);

    GUI.Label(
        new Rect(panel.x + 14f, panel.y + 91f, 140f, 18f),
        "RPM  " + Mathf.RoundToInt(rpm).ToString("N0"),
        headerStyle
    );

    Rect rpmBarBackground = new Rect(panel.x + 14f, panel.y + 112f, panelWidth - 28f, 8f);
    GUI.color = new Color(0.18f, 0.21f, 0.25f, 1f);
    GUI.DrawTexture(rpmBarBackground, Texture2D.whiteTexture);
    GUI.color = rpmFraction >= 0.9f
        ? new Color(1f, 0.25f, 0.2f, 1f)
        : rpmFraction >= 0.72f
            ? new Color(1f, 0.7f, 0.2f, 1f)
            : new Color(0.25f, 0.78f, 1f, 1f);
    GUI.DrawTexture(
        new Rect(rpmBarBackground.x, rpmBarBackground.y, rpmBarBackground.width * rpmFraction, rpmBarBackground.height),
        Texture2D.whiteTexture
    );

    if (drawFuel)
    {
        float capacity = Mathf.Max(1f, fuel.FuelCapacityLitres);
        float litres = Mathf.Clamp(fuel.FuelRemainingLitres, 0f, capacity);
        float fuelFraction = Mathf.Clamp01(litres / capacity);
        bool lowFuel = fuelFraction <= 0.15f;

        GUI.color = previousColor;
        GUI.Label(
            new Rect(panel.x + 14f, panel.y + 125f, panelWidth - 28f, 18f),
            "FUEL   " + litres.ToString("0.0") + " / " + capacity.ToString("0") + " L" +
                "    " + Mathf.RoundToInt(fuelFraction * 100f) + "%",
            headerStyle
        );

        Rect fuelBarBackground = new Rect(panel.x + 14f, panel.y + 147f, panelWidth - 28f, 8f);
        GUI.color = new Color(0.18f, 0.21f, 0.25f, 1f);
        GUI.DrawTexture(fuelBarBackground, Texture2D.whiteTexture);
        GUI.color = lowFuel
            ? new Color(1f, 0.25f, 0.2f, 1f)
            : fuelFraction <= 0.35f
                ? new Color(1f, 0.7f, 0.2f, 1f)
                : new Color(0.35f, 0.85f, 0.45f, 1f);
        GUI.DrawTexture(
            new Rect(fuelBarBackground.x, fuelBarBackground.y, fuelBarBackground.width * fuelFraction, fuelBarBackground.height),
            Texture2D.whiteTexture
        );

        if (lowFuel)
        {
            GUIStyle warningStyle = new GUIStyle(headerStyle)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 10
            };
            warningStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);
            GUI.Label(
                new Rect(panel.x + 135f, panel.y + 8f, panelWidth - 149f, 18f),
                "LOW FUEL",
                warningStyle
            );
        }
    }

    GUI.color = previousColor;
}

}