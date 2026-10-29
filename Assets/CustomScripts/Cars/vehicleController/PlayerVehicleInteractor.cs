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

[Header("Driving HUD")]
[Tooltip("Show speed, RPM, current gear and fuel level only while seated in a vehicle.")]
public bool showDrivingTelemetry = true;
[Tooltip("Display the current vehicle's fuel level when it has a VehicleFuel component.")]
public bool showFuelGauge = true;

private float cameraYaw;
private float cameraPitch = 15f;

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
                Enter(nearby, nearbyDoor.seatPointOverride);
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
            TryExit();
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

        if (cockpit != null)
        {
            cam.transform.SetPositionAndRotation(
                cockpit.position,
                cockpit.rotation * Quaternion.Euler(0f, cockpitYawOffset, 0f)
            );
        }
        else
        {
            cam.transform.SetPositionAndRotation(
                current.transform.position + Vector3.up,
                current.transform.rotation
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
    if (!v.TryReserve())
        return;

    current = v;

    // Start in chase view whenever a vehicle is entered.
    cockpitView = false;
    cameraYaw = 0f;
    cameraPitch = 15f;

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
        {
            if (m == null)
                continue;

            bool alreadyTracked = false;
            foreach (BehaviourEnabledState state in disabledBehaviourStates)
            {
                if (state.behaviour == m)
                {
                    alreadyTracked = true;
                    break;
                }
            }

            if (!alreadyTracked)
                disabledBehaviourStates.Add(
                    new BehaviourEnabledState(m, m.enabled));

            m.enabled = false;
        }
    }

    invectorCameraWasEnabled = invectorCam != null && invectorCam.enabled;
    if (invectorCam != null)
        invectorCam.enabled = false;

    SetPlayerPhysics(false);
    SetPlayerVisible(false);

    Transform seat = seatPointOverride != null
        ? seatPointOverride
        : (v.seatPoint != null ? v.seatPoint : v.transform);

    transform.SetParent(seat, true);
    transform.localPosition = Vector3.zero;
    transform.localRotation = Quaternion.identity;
}

// ---------------- Exit Vehicle ----------------

void TryExit()
{
    if (current.Speed > maxExitSpeed)
    {
        Say("Slow down to get out");
        return;
    }

    if (!TryFindExitPosition(current, out Vector3 pos))
    {
        Say("No safe place to exit here");
        return;
    }

    Vector3 flatFwd = Vector3.ProjectOnPlane(
        current.transform.forward,
        Vector3.up
    );

    Quaternion rot = flatFwd.sqrMagnitude > 0.001f
        ? Quaternion.LookRotation(flatFwd, Vector3.up)
        : Quaternion.identity;

    transform.SetParent(originalParent, true);
    transform.SetPositionAndRotation(pos, rot);
    // Reparenting under a scaled seat can alter the player's local scale.
    // Restore the exact scale captured before entering the vehicle.
    transform.localScale = originalLocalScale;

    SetPlayerVisible(true);
    SetPlayerPhysics(true);

    // Restore exactly the component states that existed before entering.
    RestoreDrivingComponents();

    if (invectorCam != null)
        invectorCam.enabled = invectorCameraWasEnabled;

    Vehicle exitedVehicle = current;
    current = null;

    if (exitedVehicle != null)
        exitedVehicle.Release();

    cockpitView = false;
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