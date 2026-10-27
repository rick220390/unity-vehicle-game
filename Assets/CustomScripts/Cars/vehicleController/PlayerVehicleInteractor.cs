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

[Header("Mouse Orbit")]
public float mouseSensitivity = 3f;
public float minCameraPitch = -10f;
public float maxCameraPitch = 65f;

private float cameraYaw;
private float cameraPitch = 15f;

private Vehicle current;
private vThirdPersonCamera invectorCam;
private Transform originalParent;
private Vector3 originalLocalScale;
private Rigidbody rb;

private readonly List<Collider> hiddenColliders = new List<Collider>();
private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

private Vehicle nearby;
private string message;
private float messageTimer;

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
        nearby = FindNearbyVehicle();

        if (nearby != null && Input.GetKeyDown(interactKey))
            Enter(nearby);
    }
    else
    {
        // Vehicle driving controls.
        current.SetInput(
            Input.GetAxis("Vertical"),
            Input.GetAxis("Horizontal"),
            Input.GetKey(handbrakeKey)
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

    // Rotate the camera with the mouse.
    cameraYaw += Input.GetAxis("Mouse X") * mouseSensitivity;
    cameraPitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;

    cameraPitch = Mathf.Clamp(
        cameraPitch,
        minCameraPitch,
        maxCameraPitch
    );

    // Look at a point slightly above the vehicle.
    Vector3 pivot = current.transform.position
                  + Vector3.up * camLookHeight;

    // Calculate the camera's orbit around the vehicle.
    Quaternion orbitRotation = Quaternion.Euler(
        cameraPitch,
        current.transform.eulerAngles.y + cameraYaw,
        0f
    );

    Vector3 desiredPos = pivot
                       - orbitRotation * Vector3.forward * camDistance
                       + Vector3.up * (camHeight - camLookHeight);

    // Smooth the camera's movement.
    float positionBlend = 1f - Mathf.Exp(
        -camPositionSmooth * Time.deltaTime
    );

    float rotationBlend = 1f - Mathf.Exp(
        -camRotationSmooth * Time.deltaTime
    );

    cam.transform.position = Vector3.Lerp(
        cam.transform.position,
        desiredPos,
        positionBlend
    );

    // Keep the camera looking at the vehicle.
    Vector3 lookDirection = pivot - cam.transform.position;

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

Vehicle FindNearbyVehicle()
{
    Vehicle best = null;
    float bestDist = enterRange;

    foreach (Vehicle v in Vehicle.All)
    {
        if (v == null || v.HasDriver)
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

void Enter(Vehicle v)
{
    if (!v.TryReserve())
        return;

    current = v;

    // Reset the camera when entering the vehicle.
    cameraYaw = 0f;
    cameraPitch = 15f;

    originalParent = transform.parent;
    originalLocalScale = transform.localScale;

    // Disable Invector character movement while driving.
    foreach (MonoBehaviour m in disableWhileDriving)
    {
        if (m != null)
            m.enabled = false;
    }

    if (invectorCam != null)
        invectorCam.enabled = false;

    SetPlayerPhysics(false);
    SetPlayerVisible(false);

    Transform seat = v.seatPoint != null
        ? v.seatPoint
        : v.transform;

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

    // Restore Invector character movement.
    foreach (MonoBehaviour m in disableWhileDriving)
    {
        if (m != null)
            m.enabled = true;
    }

    if (invectorCam != null)
        invectorCam.enabled = true;

    current.Release();
    current = null;
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
            rb.isKinematic = false;
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
    string text = null;

    if (messageTimer > 0f)
        text = message;
    else if (current != null)
        text = $"[{interactKey}] Exit    [Space] Handbrake";
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

}