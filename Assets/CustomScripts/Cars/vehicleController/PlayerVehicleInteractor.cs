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
private bool cockpitView;
private vThirdPersonCamera invectorCam;
private Transform originalParent;
private Rigidbody rb;

private readonly List<Collider> hiddenColliders = new List<Collider>();
private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
private readonly Collider[] overlap = new Collider[16];

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
        // Switch between the existing chase camera and the vehicle's cockpit point.
        if (Input.GetKeyDown(cameraToggleKey))
            cockpitView = !cockpitView;

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
                cockpit.rotation
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

    // Rotate the chase camera with the mouse.
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

        Vector3 p = v.BodyCollider != null
            ? v.BodyCollider.ClosestPoint(transform.position)
            : v.transform.position;

        float d = Vector3.Distance(p, transform.position);

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

    // Start in chase view whenever a vehicle is entered.
    cockpitView = false;
    cameraYaw = 0f;
    cameraPitch = 15f;

    originalParent = transform.parent;

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

    Vector3 pos = FindExitPosition(current);

    Vector3 flatFwd = Vector3.ProjectOnPlane(
        current.transform.forward,
        Vector3.up
    );

    Quaternion rot = flatFwd.sqrMagnitude > 0.001f
        ? Quaternion.LookRotation(flatFwd, Vector3.up)
        : Quaternion.identity;

    transform.SetParent(originalParent, true);
    transform.SetPositionAndRotation(pos, rot);

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
    cockpitView = false;
}

Vector3 FindExitPosition(Vehicle v)
{
    if (v.exitPoints != null)
    {
        foreach (Transform p in v.exitPoints)
        {
            if (p != null && IsSpotFree(p.position, v))
                return p.position;
        }
    }

    // Fallback position above the roof.
    Vector3 basePos = v.seatPoint != null
        ? v.seatPoint.position
        : v.transform.position;

    return basePos + Vector3.up * 2.5f;
}

bool IsSpotFree(Vector3 p, Vehicle v)
{
    int n = Physics.OverlapCapsuleNonAlloc(
        p + Vector3.up * 0.5f,
        p + Vector3.up * 1.5f,
        0.3f,
        overlap,
        ~0,
        QueryTriggerInteraction.Ignore
    );

    for (int i = 0; i < n; i++)
    {
        Collider c = overlap[i];

        if (c.GetComponentInParent<Vehicle>() == v)
            continue;

        if (c.transform.IsChildOf(transform))
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
        text = $"[{interactKey}] Exit    [Space] Handbrake    [{cameraToggleKey}] {(cockpitView ? "External view" : "Cockpit view")}";
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