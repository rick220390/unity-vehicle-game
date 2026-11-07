using UnityEngine;

/// <summary>
/// Warehouse job application point. Uses distance-based interaction so it
/// does not depend on trigger callbacks or the player having a Rigidbody.
/// Assign Player Transform in the Inspector if the project does not use the
/// exact Unity tag "Player".
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseJobApplicationTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WarehouseJobManager jobManager;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform interactionPoint;

    [Header("Interaction")]
    [SerializeField, Min(1f)] private float interactionRange = 3.5f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    private bool reportedMissingPlayer;
    private bool reportedMissingManager;

    private void Awake()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
            trigger.isTrigger = true;

        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();

        if (playerTransform == null || jobManager == null || !CanInteract())
            return;

        if (Input.GetKeyDown(interactKey))
        {
            Debug.Log("Warehouse application: E pressed. Attempting to start shift.", this);
            jobManager.BeginWarehouseJob();
        }
    }

    private void ResolveReferences()
    {
        if (jobManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            jobManager = FindFirstObjectByType<WarehouseJobManager>();
#else
            jobManager = FindObjectOfType<WarehouseJobManager>();
#endif
        }

        if (playerTransform == null)
        {
            // First try the project's conventional player tag.
            try
            {
                GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
                if (taggedPlayer != null)
                    playerTransform = taggedPlayer.transform;
            }
            catch (UnityException)
            {
                // The Player tag may not exist in this project.
            }
        }

        if (playerTransform == null && Camera.main != null)
        {
            // In the usual third-person setup the main camera is parented
            // beneath the player/camera rig, so its root is a useful fallback.
            Transform cameraRoot = Camera.main.transform.root;
            if (cameraRoot != transform.root)
                playerTransform = cameraRoot;
        }

        if (playerTransform == null && !reportedMissingPlayer)
        {
            reportedMissingPlayer = true;
            Debug.LogWarning("Warehouse application: no player found. Assign the player root Transform to the 'Player Transform' field, or tag the player root exactly 'Player'.", this);
        }

        if (jobManager == null && !reportedMissingManager)
        {
            reportedMissingManager = true;
            Debug.LogWarning("Warehouse application: no active WarehouseJobManager found in this scene.", this);
        }
    }

    private bool CanInteract()
    {
        if (playerTransform == null || jobManager == null)
            return false;

        Vector3 point = interactionPoint != null ? interactionPoint.position : transform.position;
        return Vector3.Distance(playerTransform.position, point) <= interactionRange;
    }

    private void OnGUI()
    {
        if (playerTransform == null || jobManager == null || !CanInteract())
            return;

        string prompt = jobManager.State == WarehouseJobManager.JobState.Active
            ? "Warehouse shift already in progress"
            : jobManager.State == WarehouseJobManager.JobState.Completed
                ? "Warehouse shift completed"
                : "[E] Apply for " + jobManager.JobTitle;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(14, 14, 8, 8)
        };

        GUI.Label(new Rect(0f, Screen.height - 120f, Screen.width, 50f), prompt, style);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 point = interactionPoint != null ? interactionPoint.position : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(point, interactionRange);
    }
}
