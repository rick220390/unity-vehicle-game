using UnityEngine;

/// <summary>
/// Warehouse job application point. Uses the same distance-based interaction
/// pattern as ForkliftPlayerInteractor, so it does not depend on trigger
/// callbacks or the player having a Rigidbody.
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseJobApplicationTrigger : MonoBehaviour
{
    [SerializeField] private WarehouseJobManager jobManager;
    [SerializeField] private Transform interactionPoint;
    [SerializeField, Min(1f)] private float interactionRange = 3.5f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    private Transform player;

    private void Awake()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
            trigger.isTrigger = true;

        ResolveReferences();
    }

    private void Update()
    {
        if (player == null || jobManager == null)
            ResolveReferences();

        if (!CanInteract())
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

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
        }
    }

    private bool CanInteract()
    {
        if (player == null || jobManager == null)
            return false;

        Vector3 point = interactionPoint != null ? interactionPoint.position : transform.position;
        return Vector3.Distance(player.position, point) <= interactionRange;
    }

    private void OnGUI()
    {
        if (player == null || jobManager == null)
            return;

        if (!CanInteract())
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
