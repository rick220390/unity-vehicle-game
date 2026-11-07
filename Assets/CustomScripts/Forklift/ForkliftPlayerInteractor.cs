using System.Collections.Generic;
using UnityEngine;
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
    private bool playerCameraControllerWasEnabled;
    private bool playerCameraWasEnabled;
    private bool cursorWasVisible;
    private CursorLockMode cursorLockWas;

    private void Awake()
    {
        if (vehicleController == null) vehicleController = GetComponent<WSMVehicleController>();
        if (vehicleInput == null) vehicleInput = GetComponent<WSMVehiclePlayerInput>();
        if (forkliftInput == null) forkliftInput = GetComponent<ForkliftPlayerInput>();

        // The forklift is instantiated at job start, so Inspector references
        // to scene-only objects are not reliable on the spawned copy. Resolve
        // the camera from the active scene when it was not explicitly assigned.
        if (forkliftCamera == null)
        {
#if UNITY_2023_1_OR_NEWER
            forkliftCamera = FindFirstObjectByType<WSMVehicleCamera>();
#else
            forkliftCamera = FindObjectOfType<WSMVehicleCamera>();
#endif
        }

        if (forkliftCamera != null)
        {
            if (forkliftCameraInput == null)
                forkliftCameraInput = forkliftCamera.GetComponent<WSMVehicleCameraPlayerInput>();

            // Keep the complete camera object off until a driver enters.
            forkliftCamera.gameObject.SetActive(false);
        }

        if (vehicleInput != null) vehicleInput.enablePlayerInput = false;
        if (forkliftInput != null) forkliftInput.enablePlayerInput = false;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null) player = playerObject.transform;
        }

        if (player != null)
        {
            playerRigidbody = player.GetComponent<Rigidbody>();
            if (playerRigidbody == null) playerRigidbody = player.GetComponentInChildren<Rigidbody>();

            if (playerCameraController == null)
            {
                foreach (Behaviour component in player.GetComponentsInChildren<Behaviour>(true))
                {
                    if (component != null && component.GetType().Name == "vThirdPersonCamera")
                    {
                        playerCameraController = component;
                        break;
                    }
                }
            }

            if (playerCamera == null && playerCameraController != null)
                playerCamera = playerCameraController.GetComponent<Camera>();
            if (playerCamera == null) playerCamera = Camera.main;
        }

        if (driverSeatPoint == null || driverExitPoint == null)
            Debug.LogError("ForkliftPlayerInteractor: Assign DriverSeatPoint and DriverExitPoint.", this);
        if (vehicleController == null || vehicleInput == null || forkliftInput == null)
            Debug.LogError("ForkliftPlayerInteractor: One or more WSM forklift components are missing.", this);
        if (forkliftCamera == null)
            Debug.LogError("ForkliftPlayerInteractor: No WSMVehicleCamera was found. Add the camera to the scene and ensure its GameObject is active before the job starts.", this);
    }

    private void Update()
    {
        if (player == null) return;

        if (isOccupied)
        {
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

        if (playerCameraController != null)
            playerCameraController.enabled = false;
        if (playerCamera != null)
            playerCamera.enabled = false;

        player.SetParent(driverSeatPoint, false);
        player.localPosition = Vector3.zero;
        player.localRotation = Quaternion.identity;
        player.localScale = originalLocalScale;

        // Prepare camera target before enabling the camera GameObject.
        forkliftCamera.Target = transform;
        forkliftCamera.gameObject.SetActive(true);
        if (forkliftCameraInput != null)
            forkliftCameraInput.enablePlayerInput = true;

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

    private void ExitForklift()
    {
        if (!isOccupied) return;

        // Lock inputs before restoring the player.
        if (vehicleInput != null) vehicleInput.enablePlayerInput = false;
        if (forkliftInput != null) forkliftInput.enablePlayerInput = false;
        if (forkliftCameraInput != null) forkliftCameraInput.enablePlayerInput = false;

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

        player.SetParent(originalParent, true);
        player.SetPositionAndRotation(driverExitPoint != null ? driverExitPoint.position : transform.position + transform.right * 1.5f,
                                      driverExitPoint != null ? driverExitPoint.rotation : transform.rotation);
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

        if (playerCameraController != null)
            playerCameraController.enabled = playerCameraControllerWasEnabled;
        if (playerCamera != null)
            playerCamera.enabled = playerCameraWasEnabled;

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

        playerCameraControllerWasEnabled = playerCameraController != null && playerCameraController.enabled;
        playerCameraWasEnabled = playerCamera != null && playerCamera.enabled;
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
