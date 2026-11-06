using System.Collections;
using UnityEngine;

/// <summary>
/// Lets the player open and close the rear trailer doors on the spawned
/// warehouse delivery lorry. Attach this to a scene object, not the lorry prefab.
/// </summary>
[DisallowMultipleComponent]
public class WarehouseTrailerDoorInteraction : MonoBehaviour
{
    [Header("Lorry arrival")]
    [SerializeField] private WarehouseLorryArrival lorryArrival;

    [Header("Player interaction")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform interactionPoint;
    [SerializeField, Min(0.5f)] private float interactionRange = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Door hinges (found automatically by name if left empty)")]
    [SerializeField] private Transform leftDoorHinge;
    [SerializeField] private Transform rightDoorHinge;

    [Header("Door opening")]
    [Tooltip("Local Y rotation added to the left hinge when opening. Change the sign if it swings the wrong way.")]
    [SerializeField] private float leftOpenAngle = -100f;
    [Tooltip("Local Y rotation added to the right hinge when opening. Change the sign if it swings the wrong way.")]
    [SerializeField] private float rightOpenAngle = 100f;
    [SerializeField, Min(0.1f)] private float openingSpeed = 100f;

    [Header("Prompt")]
    [SerializeField] private bool showPrompt = true;

    private Quaternion leftClosedRotation;
    private Quaternion rightClosedRotation;
    private Coroutine doorRoutine;
    private bool doorsOpen;
    private bool hingesCached;
    private bool missingHingesLogged;

    public bool DoorsOpen => doorsOpen;

    private void Awake()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
        }
    }

    private void Update()
    {
        if (player == null || lorryArrival == null || !lorryArrival.HasArrived)
            return;

        if (!hingesCached && !TryFindDoorHinges())
            return;

        Transform rangePoint = interactionPoint != null
            ? interactionPoint
            : (lorryArrival.SpawnedLorry != null ? lorryArrival.SpawnedLorry.transform : null);

        if (rangePoint == null)
            return;

        bool inRange = Vector3.Distance(player.position, rangePoint.position) <= interactionRange;
        if (inRange && Input.GetKeyDown(interactKey))
            ToggleDoors();
    }

    private bool TryFindDoorHinges()
    {
        GameObject lorry = lorryArrival != null ? lorryArrival.SpawnedLorry : null;
        if (lorry == null)
            return false;

        Transform[] allChildren = lorry.GetComponentsInChildren<Transform>(true);

        if (leftDoorHinge == null)
            leftDoorHinge = FindByName(allChildren, "LeftDoorHinge");

        if (rightDoorHinge == null)
            rightDoorHinge = FindByName(allChildren, "RightDoorHinge");

        if (leftDoorHinge == null || rightDoorHinge == null)
        {
            if (!missingHingesLogged)
            {
                Debug.LogError("WarehouseTrailerDoorInteraction: Could not find both LeftDoorHinge and RightDoorHinge under the spawned lorry. Check their exact names.", this);
                missingHingesLogged = true;
            }
            return false;
        }

        leftClosedRotation = leftDoorHinge.localRotation;
        rightClosedRotation = rightDoorHinge.localRotation;
        hingesCached = true;
        return true;
    }

    private static Transform FindByName(Transform[] objects, string exactName)
    {
        foreach (Transform item in objects)
        {
            if (item.name == exactName)
                return item;
        }
        return null;
    }

    private void ToggleDoors()
    {
        if (doorRoutine != null)
            StopCoroutine(doorRoutine);

        doorsOpen = !doorsOpen;
        Quaternion leftTarget = doorsOpen
            ? leftClosedRotation * Quaternion.Euler(0f, leftOpenAngle, 0f)
            : leftClosedRotation;
        Quaternion rightTarget = doorsOpen
            ? rightClosedRotation * Quaternion.Euler(0f, rightOpenAngle, 0f)
            : rightClosedRotation;

        doorRoutine = StartCoroutine(AnimateDoors(leftTarget, rightTarget));
    }

    private IEnumerator AnimateDoors(Quaternion leftTarget, Quaternion rightTarget)
    {
        while (leftDoorHinge != null && rightDoorHinge != null &&
               (Quaternion.Angle(leftDoorHinge.localRotation, leftTarget) > 0.1f ||
                Quaternion.Angle(rightDoorHinge.localRotation, rightTarget) > 0.1f))
        {
            float step = openingSpeed * Time.deltaTime;
            leftDoorHinge.localRotation = Quaternion.RotateTowards(leftDoorHinge.localRotation, leftTarget, step);
            rightDoorHinge.localRotation = Quaternion.RotateTowards(rightDoorHinge.localRotation, rightTarget, step);
            yield return null;
        }

        if (leftDoorHinge != null) leftDoorHinge.localRotation = leftTarget;
        if (rightDoorHinge != null) rightDoorHinge.localRotation = rightTarget;
        doorRoutine = null;
    }

    private void OnGUI()
    {
        if (!showPrompt || player == null || lorryArrival == null || !lorryArrival.HasArrived)
            return;

        Transform rangePoint = interactionPoint != null
            ? interactionPoint
            : (lorryArrival.SpawnedLorry != null ? lorryArrival.SpawnedLorry.transform : null);

        if (rangePoint == null || Vector3.Distance(player.position, rangePoint.position) > interactionRange)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(0f, Screen.height - 150f, Screen.width, 40f),
            doorsOpen ? "[E] Close trailer doors" : "[E] Open trailer doors", style);
    }
}
