using UnityEngine;

/// <summary>
/// Application/start-shift interaction placed at the warehouse entrance.
/// Requires a trigger collider and a player tagged Player.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class WarehouseJobApplicationTrigger : MonoBehaviour
{
    [SerializeField] private WarehouseJobManager jobManager;
    [SerializeField] private Transform interactionPoint;
    [SerializeField, Min(1f)] private float interactionRange = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    private Transform player;
    private bool playerInTrigger;

    private void Awake()
    {
        Collider trigger = GetComponent<Collider>();
        trigger.isTrigger = true;

        if (jobManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            jobManager = FindFirstObjectByType<WarehouseJobManager>();
#else
            jobManager = FindObjectOfType<WarehouseJobManager>();
#endif
        }

        FindPlayer();
    }

    private void Update()
    {
        if (player == null)
            FindPlayer();

        if (!playerInTrigger || player == null || jobManager == null)
            return;

        Vector3 point = interactionPoint != null ? interactionPoint.position : transform.position;
        if (Vector3.Distance(player.position, point) > interactionRange)
            return;

        if (Input.GetKeyDown(interactKey))
            jobManager.BeginWarehouseJob();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") || other.transform.root.CompareTag("Player"))
            playerInTrigger = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") || other.transform.root.CompareTag("Player"))
            playerInTrigger = false;
    }

    private void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
            player = playerObject.transform;
    }

    private void OnGUI()
    {
        if (!playerInTrigger || player == null || jobManager == null)
            return;

        Vector3 point = interactionPoint != null ? interactionPoint.position : transform.position;
        if (Vector3.Distance(player.position, point) > interactionRange)
            return;

        string prompt = jobManager.State == WarehouseJobManager.JobState.Active
            ? "Warehouse shift already in progress"
            : jobManager.State == WarehouseJobManager.JobState.Completed
                ? "Warehouse shift completed"
                : "[E] Apply for " + jobManager.JobTitle;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 20,
            fontStyle = FontStyle.Bold
        };
        GUI.Label(new Rect(0f, Screen.height - 125f, Screen.width, 45f), prompt, style);
    }
}
