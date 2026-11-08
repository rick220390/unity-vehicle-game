using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WSMGameStudio.Vehicles;
using WSMGameStudio.HeavyMachinery;

/// <summary>
/// Connects the WSM forklift to the existing Invector player without
/// modifying the original WSM or car controller scripts.
/// Attach this component to the forklift root.
/// </summary>
[DisallowMultipleComponent]
public class ForkliftPlayerInteractor : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private Transform player;
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("Forklift positions")]
    [SerializeField] private Transform driverSeatPoint;
    [SerializeField] private Transform driverExitPoint;

    [Header("WSM components")]
    [SerializeField] private WSMVehicleController vehicleController;
    [SerializeField] private WSMVehiclePlayerInput vehicleInput;
    [SerializeField] private ForkliftPlayerInput forkliftInput;
    [SerializeField] private WSMVehicleCamera forkliftCamera;
    [SerializeField] private WSMVehicleCameraPlayerInput forkliftCameraInput;
    [Tooltip("Optional camera look pivot. If empty, one is created at the forklift renderer bounds centre.")]
    [SerializeField] private Transform forkliftCameraTarget;

    [Header("Player camera")]
    [Tooltip("Usually the vThirdPersonCamera component on the player.")]
    [SerializeField] private Behaviour playerCameraController;
    [Tooltip("The physical Camera used by the normal player camera.")]
    [SerializeField] private Camera playerCamera;

    private bool isOccupied;
    private Transform originalParent;
    private Vector3 originalLocalScale;
    private Rigidbody playerRigidbody;
    private bool originalRigidbodyKinematic;
    private readonly List<Collider> playerColliders = new List<Collider>();
    private readonly List<bool> colliderStates = new List<bool>();
    private readonly List<Behaviour> lockedBehaviours = new List<Behaviour>();
    private readonly List<bool> behaviourStates = new List<bool>();
    private readonly List<Behaviour> scenePlayerCameraControllers = new List<Behaviour>();
    private readonly List<bool> scenePlayerCameraControllerStates = new List<bool>();
    private readonly List<Camera> scenePlayerCameras = new List<Camera>();
    private readonly List<bool> scenePlayerCameraStates = new List<bool>();
    private bool cursorWasVisible;
    private bool forkliftCameraInputWasEnabled;
    private CursorLockMode cursorLockWas;
    private void ConfigureCameraTags()
    {
        // WSMVehicleCamera controls its own Camera component (GetComponent<Camera>).
        // It does not need Camera.main, so keep the two camera identities separate.
        if (playerCamera != null)
            playerCamera.tag = "MainCamera";

        if (forkliftCamera != null)
            forkliftCamera.tag = "ForkliftCamera";
    }

    private void Awake()
    {
        if (vehicleController == null) vehicleController = GetComponent<WSMVehicleController>();
        if (vehicleInput == null) vehicleInput = GetComponent<WSMVehiclePlayerInput>();
        if (forkliftInput == null) forkliftInput = GetComponent<ForkliftPlayerInput>();

        // A scene forklift may already have disabled the shared WSM camera
        // before this runtime-spawned forklift executes Awake. FindObjectsOfType
        // does not return components on inactive GameObjects, so search loaded
        // scene objects including inactive ones.
        if (forkliftCamera == null)
        {
            WSMVehicleCamera[] cameras = Resources.FindObjectsOfTypeAll<WSMVehicleCamera>();
            foreach (WSMVehicleCamera candidate in cameras)
            {
                if (candidate == null || !candidate.gameObject.scene.IsValid() ||
                    !candidate.gameObject.scene.isLoaded)
                    continue;

                forkliftCamera = candidate;
                break;
            }
        }

        if (forkliftCamera != null)
        {
            if (forkliftCameraInput == null)
                forkliftCameraInput = forkliftCamera.GetComponent<WSMVehicleCameraPlayerInput>();

            // Keep the forklift camera separate from Camera.main at all times.
            forkliftCamera.tag = "ForkliftCamera";

            // Keep the complete camera object off until a driver enters.
            forkliftCamera.gameObject.SetActive(false);
        }

        if (vehicleInput != null) vehicleInput.enablePlayerInput = false;
        if (forkliftInput != null) forkliftInput.enablePlayerInput = false;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null) BindPlayer(playerObject.transform);
        }

        if (player != null)
        {
            playerRigidbody = player.GetComponent<Rigidbody>();
            if (playerRigidbody == null) playerRigidbody = player.GetComponentInChildren<Rigidbody>();

            ResolvePlayerCameraRig();

            // Keep the on-foot player camera as the only MainCamera.
            if (playerCamera == null)
                Debug.LogWarning("ForkliftPlayerInteractor: Could not resolve the player's physical Camera from vThirdPersonCamera. Assign Player Camera in the Inspector; Camera.main is deliberately not used as a fallback.", this);

            ConfigureCameraTags();
        }

        if (driverSeatPoint == null || driverExitPoint == null)
            Debug.LogError("ForkliftPlayerInteractor: Assign DriverSeatPoint and DriverExitPoint.", this);
        if (vehicleController == null || vehicleInput == null || forkliftInput == null)
            Debug.LogError("ForkliftPlayerInteractor: One or more WSM forklift components are missing.", this);
        if (forkliftCamera == null)
            Debug.LogWarning("ForkliftPlayerInteractor: No WSMVehicleCamera was found. Forklift entry will use the player camera as a fallback.", this);
    }

    /// <summary>Explicitly binds the scene player to this runtime-spawned forklift.</summary>
    public void BindPlayer(Transform playerTransform)
    {
        player = ResolvePlayerRoot(playerTransform);
        if (player == null)
        {
            Debug.LogError("ForkliftPlayerInteractor: BindPlayer received a null player.", this);
            return;
        }

        playerRigidbody = player.GetComponent<Rigidbody>();
        if (playerRigidbody == null) playerRigidbody = player.GetComponentInChildren<Rigidbody>();

        ResolvePlayerCameraRig();

        // Do not use Camera.main here: it could resolve to the forklift camera.
        if (playerCamera == null)
            Debug.LogWarning("ForkliftPlayerInteractor: Could not resolve the player's physical Camera from vThirdPersonCamera. Assign Player Camera in the Inspector.", this);

        ConfigureCameraTags();

        Debug.Log("ForkliftPlayerInteractor: Player bound to spawned forklift: " + player.name, this);
    }

    /// <summary>
    /// The Player tag can be on a nested VBot bone/collider rather than the
    /// Invector character root. Always bind the actual ThirdPersonController root.
    /// </summary>
    private Transform ResolvePlayerRoot(Transform candidate)
    {
        if (candidate == null) return null;

        Transform fallback = candidate;
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current.name == "ThirdPersonController_LITE")
                return current;

            MonoBehaviour[] components = current.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour component in components)
            {
                if (component == null) continue;
                string typeName = component.GetType().Name;
                if (typeName == "vThirdPersonController" ||
                    typeName == "vThirdPersonInput")
                    return current;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Invector's camera is a separate scene object, not a child of the player.
    /// </summary>
    private void ResolvePlayerCameraRig()
    {
        if (playerCameraController != null && playerCamera != null) return;
        vThirdPersonCamera[] rigs = Resources.FindObjectsOfTypeAll<vThirdPersonCamera>();
        foreach (vThirdPersonCamera rig in rigs)
        {
            if (rig == null || !rig.gameObject.scene.IsValid() || !rig.gameObject.scene.isLoaded) continue;
            if (playerCameraController == null) playerCameraController = rig;
            if (playerCamera == null) playerCamera = rig.GetComponent<Camera>();
            if (playerCameraController != null && playerCamera != null) break;
        }
    }

    private void DisableScenePlayerCameras()
    {
        scenePlayerCameraControllers.Clear();
        scenePlayerCameraControllerStates.Clear();
        scenePlayerCameras.Clear();
        scenePlayerCameraStates.Clear();

        vThirdPersonCamera[] rigs = Resources.FindObjectsOfTypeAll<vThirdPersonCamera>();
        foreach (vThirdPersonCamera rig in rigs)
        {
            if (rig == null || !rig.gameObject.scene.IsValid() || !rig.gameObject.scene.isLoaded) continue;
            scenePlayerCameraControllers.Add(rig);
            scenePlayerCameraControllerStates.Add(rig.enabled);
            rig.enabled = false;

            Camera physicalCamera = rig.GetComponent<Camera>();
            if (physicalCamera != null)
            {
                scenePlayerCameras.Add(physicalCamera);
                scenePlayerCameraStates.Add(physicalCamera.enabled);
                physicalCamera.enabled = false;
            }
        }
        if (scenePlayerCameraControllers.Count == 0)
            Debug.LogWarning("ForkliftPlayerInteractor: No loaded vThirdPersonCamera was found to disable.", this);
    }

    private void RestoreScenePlayerCameras()
    {
        for (int i = 0; i < scenePlayerCameraControllers.Count; i++)
            if (scenePlayerCameraControllers[i] != null)
                scenePlayerCameraControllers[i].enabled = scenePlayerCameraControllerStates[i];
        for (int i = 0; i < scenePlayerCameras.Count; i++)
            if (scenePlayerCameras[i] != null)
                scenePlayerCameras[i].enabled = scenePlayerCameraStates[i];
    }

    private void Update()
    {
        // The player may be created after this forklift (or assigned by the job manager).
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null) BindPlayer(playerObject.transform);
        }

        if (player == null) return;

        if (isOccupied)
        {
            // Read mouse delta directly. The WSM input component can be configured
            // for its legacy keyboard-only mode, which silently ignores mouse look.
            if (forkliftCamera != null && Mouse.current != null)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                forkliftCamera.HorizontalInput = mouseDelta.x;
                forkliftCamera.VerticalInput = -mouseDelta.y;
            }
            else if (forkliftCamera != null)
            {
                forkliftCamera.HorizontalInput = 0f;
                forkliftCamera.VerticalInput = 0f;
            }

            if (Input.GetKeyDown(interactKey))
                ExitForklift();
            return;
        }

        if (driverSeatPoint != null &&
            Vector3.Distance(player.position, transform.position) <= interactionRange &&
            Input.GetKeyDown(interactKey))
        {
            EnterForklift();
        }
    }

    private void EnterForklift()
    {
        if (isOccupied || player == null || driverSeatPoint == null) return;

        if (vehicleController == null || vehicleInput == null || forkliftInput == null)
        {
            Debug.LogError("ForkliftPlayerInteractor: Cannot enter because a required WSM forklift controller/input component is missing.", this);
            return;
        }

        isOccupied = true;
        originalParent = player.parent;
        originalLocalScale = player.localScale;

        CapturePlayerStates();

        // Stop and immobilise the character before attaching them to the seat.
        if (playerRigidbody != null)
        {
            originalRigidbodyKinematic = playerRigidbody.isKinematic;
            SetVelocity(Vector3.zero);
            playerRigidbody.isKinematic = true;
        }

        foreach (Collider col in playerColliders)
            if (col != null) col.enabled = false;

        foreach (Behaviour behaviour in lockedBehaviours)
            if (behaviour != null) behaviour.enabled = false;

        // Invector's camera is a separate scene object, not a player child.
        DisableScenePlayerCameras();

        player.SetParent(driverSeatPoint, false);
        player.localPosition = Vector3.zero;
        player.localRotation = Quaternion.identity;
        player.localScale = originalLocalScale;

        // Mouse delta is only used by the WSM camera in Enhanced mode.
        // Legacy mode ignores mouse input and only responds to look-direction buttons.
        if (forkliftCamera != null)
        {
            // WSM's rear camera expects the vehicle root (including its Rigidbody)
            // as Target. A renderer-centre child has no Rigidbody and is not a valid
            // vehicle target for its speed/framing calculations.
            forkliftCamera.CameraMode = WSMVehicleCameraMode.Enhanced;
            forkliftCamera.CameraCollision = false;
            forkliftCamera.Target = transform;

            // Disable WSM's own input reader to prevent its Legacy mode from
            // overwriting the mouse values with zero every frame.
            if (forkliftCameraInput != null)
            {
                forkliftCameraInputWasEnabled = forkliftCameraInput.enabled;
                forkliftCameraInput.enabled = false;
            }

            forkliftCamera.gameObject.SetActive(true);
            forkliftCamera.MoveToStartPosition();
            forkliftCamera.HorizontalInput = 0f;
            forkliftCamera.VerticalInput = 0f;
        }

        vehicleController.IsEngineOn = true;
        vehicleController.SteeringInput = 0f;
        vehicleController.AccelerationInput = 0f;
        vehicleController.BrakesInput = 0f;
        vehicleController.HandBrakeInput = 0f;
        vehicleController.ClutchInput = 0f;

        vehicleInput.enablePlayerInput = true;
        forkliftInput.enablePlayerInput = true;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    /// <summary>
    /// Aim at the visual centre of the forklift instead of the root object's
    /// commonly ground-level origin.
    /// </summary>
    private Transform ResolveForkliftCameraTarget()
    {
        if (forkliftCameraTarget != null)
            return forkliftCameraTarget;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool foundRenderer = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer.transform == forkliftCamera.transform ||
                renderer.transform.IsChildOf(forkliftCamera.transform))
                continue;

            if (!foundRenderer)
            {
                bounds = renderer.bounds;
                foundRenderer = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        GameObject pivot = new GameObject("ForkliftCameraTarget");
        pivot.transform.SetParent(transform, false);
        pivot.transform.localPosition = foundRenderer
            ? transform.InverseTransformPoint(bounds.center)
            : Vector3.up * 1.5f;
        pivot.transform.localRotation = Quaternion.identity;
        forkliftCameraTarget = pivot.transform;
        return forkliftCameraTarget;
    }
    /// <summary>
    /// Safely returns the player to the job's designated exit point before
    /// the forklift is despawned. Works whether the player is seated or on foot.
    /// </summary>
    public void ReturnPlayerTo(Transform destination)
    {
        if (destination == null)
        {
            Debug.LogError("ForkliftPlayerInteractor: Return destination is not assigned.", this);
            return;
        }

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                BindPlayer(playerObject.transform);
        }

        if (player == null)
        {
            Debug.LogError("ForkliftPlayerInteractor: Cannot return player because no player is bound.", this);
            return;
        }

        if (isOccupied)
            ExitForklift(destination.position, destination.rotation);
        else
            player.SetPositionAndRotation(destination.position, destination.rotation);
    }

    private void ExitForklift()
    {
        Vector3 exitPosition = driverExitPoint != null
            ? driverExitPoint.position
            : transform.position + transform.right * 1.5f;
        Quaternion exitRotation = driverExitPoint != null
            ? driverExitPoint.rotation
            : transform.rotation;

        ExitForklift(exitPosition, exitRotation);
    }

    private void ExitForklift(Vector3 exitPosition, Quaternion exitRotation)
    {
        if (!isOccupied) return;

        // Lock inputs before restoring the player.
        if (vehicleInput != null) vehicleInput.enablePlayerInput = false;
        if (forkliftInput != null) forkliftInput.enablePlayerInput = false;
        if (forkliftCameraInput != null)
        {
            forkliftCameraInput.enablePlayerInput = false;
            forkliftCameraInput.enabled = forkliftCameraInputWasEnabled;
        }

        if (vehicleController != null)
        {
            vehicleController.SteeringInput = 0f;
            vehicleController.AccelerationInput = 0f;
            vehicleController.BrakesInput = 1f;
            vehicleController.HandBrakeInput = 1f;
            vehicleController.ClutchInput = 0f;
            vehicleController.IsEngineOn = false;
        }

        if (forkliftCamera != null)
            forkliftCamera.gameObject.SetActive(false);

        // Restore Camera.main to the Invector player camera before re-enabling it.
        ConfigureCameraTags();

        player.SetParent(originalParent, true);
        player.SetPositionAndRotation(exitPosition, exitRotation);
        player.localScale = originalLocalScale;

        foreach (int index in GetIndices(playerColliders.Count))
            if (playerColliders[index] != null)
                playerColliders[index].enabled = colliderStates[index];

        for (int i = 0; i < lockedBehaviours.Count; i++)
            if (lockedBehaviours[i] != null)
                lockedBehaviours[i].enabled = behaviourStates[i];

        if (playerRigidbody != null)
        {
            playerRigidbody.isKinematic = originalRigidbodyKinematic;
            SetVelocity(Vector3.zero);
        }

        RestoreScenePlayerCameras();

        Cursor.visible = cursorWasVisible;
        Cursor.lockState = cursorLockWas;

        isOccupied = false;
    }

    private void CapturePlayerStates()
    {
        lockedBehaviours.Clear();
        behaviourStates.Clear();
        playerColliders.Clear();
        colliderStates.Clear();

        foreach (Behaviour behaviour in player.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null) continue;
            string typeName = behaviour.GetType().Name;
            if (typeName == "vThirdPersonInput" ||
                typeName == "vThirdPersonController" ||
                typeName == "vThirdPersonMotor")
            {
                lockedBehaviours.Add(behaviour);
                behaviourStates.Add(behaviour.enabled);
            }
        }

        foreach (Collider col in player.GetComponentsInChildren<Collider>(true))
        {
            playerColliders.Add(col);
            colliderStates.Add(col.enabled);
        }

        cursorWasVisible = Cursor.visible;
        cursorLockWas = Cursor.lockState;
    }

    private void SetVelocity(Vector3 velocity)
    {
        if (playerRigidbody == null) return;
#if UNITY_6000_0_OR_NEWER
        playerRigidbody.linearVelocity = velocity;
#else
        playerRigidbody.velocity = velocity;
#endif
        playerRigidbody.angularVelocity = Vector3.zero;
    }

    private static IEnumerable<int> GetIndices(int count)
    {
        for (int i = 0; i < count; i++) yield return i;
    }

    private void OnGUI()
    {
        if (player == null) return;

        bool inRange = Vector3.Distance(player.position, transform.position) <= interactionRange;
        if (!isOccupied && inRange)
            DrawPrompt("[F] Operate forklift");
        else if (isOccupied)
            DrawPrompt("[F] Exit forklift");
    }

    private static void DrawPrompt(string message)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(0f, Screen.height - 110f, Screen.width, 40f), message, style);
    }

    private void OnDisable()
    {
        if (isOccupied && Application.isPlaying)
            ExitForklift();
    }
}
