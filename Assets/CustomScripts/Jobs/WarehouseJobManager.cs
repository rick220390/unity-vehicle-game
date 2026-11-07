using System.Collections.Generic;
using UnityEngine;
using WSMGameStudio.HeavyMachinery;

/// <summary>
/// Runs the warehouse shift and provides a reusable entry point for future jobs.
/// The warehouse scene owns this component; the player economy is persistent.
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseJobManager : MonoBehaviour
{
    public enum JobState { Available, Active, Completed }

    [Header("Warehouse equipment")]
    [SerializeField] private GameObject forkliftPrefab;
    [SerializeField] private Transform forkliftSpawnPoint;
    [Tooltip("Player is moved here when the shift is completed. Place this outside the warehouse.")]
    [SerializeField] private Transform playerReturnPoint;
    [SerializeField] private WarehouseLorryArrival lorryArrival;

    [Header("Shift settings")]
    [SerializeField, Min(1)] private int requiredPallets = 9;
    [SerializeField, Min(0f)] private float completionPayment = 120f;
    [SerializeField] private string jobTitle = "Warehouse Forklift Operator";

    private readonly HashSet<int> storedPalletIds = new HashSet<int>();
    private GameObject spawnedForklift;
    private bool paymentIssued;

    public JobState State { get; private set; } = JobState.Available;
    public int StoredPalletCount => storedPalletIds.Count;
    public int RequiredPallets => requiredPallets;
    public float CompletionPayment => completionPayment;
    public string JobTitle => jobTitle;

    public bool BeginWarehouseJob()
    {
        if (State == JobState.Active)
            return false;

        if (PlayerEconomy.Instance == null)
        {
            Debug.LogError("WarehouseJobManager: The persistent PlayerEconomy has not initialised.", this);
            return false;
        }

        if (forkliftPrefab == null || forkliftSpawnPoint == null)
        {
            Debug.LogError("WarehouseJobManager: Assign the forklift prefab and forklift spawn point.", this);
            return false;
        }

        if (playerReturnPoint == null)
        {
            Debug.LogError("WarehouseJobManager: Assign Player Return Point outside the warehouse. The shift cannot start without it.", this);
            return false;
        }

        if (lorryArrival == null)
        {
#if UNITY_2023_1_OR_NEWER
            lorryArrival = FindFirstObjectByType<WarehouseLorryArrival>();
#else
            lorryArrival = FindObjectOfType<WarehouseLorryArrival>();
#endif
        }

        if (lorryArrival == null)
        {
            Debug.LogError("WarehouseJobManager: WarehouseLorryArrival was not found in the scene.", this);
            return false;
        }

        storedPalletIds.Clear();
        paymentIssued = false;

        if (spawnedForklift != null)
            Destroy(spawnedForklift);

        // Accept either a Project prefab or the working forklift placed in the
        // scene. Hide a scene source so the shift creates only one visible truck.
        bool forkliftSourceIsSceneObject = forkliftPrefab.scene.IsValid();
        if (forkliftSourceIsSceneObject)
            forkliftPrefab.SetActive(false);

        spawnedForklift = Instantiate(forkliftPrefab, forkliftSpawnPoint.position, forkliftSpawnPoint.rotation);
        spawnedForklift.name = forkliftPrefab.name + "_WarehouseShift";
        spawnedForklift.SetActive(true);

        ForkliftController spawnedController = spawnedForklift.GetComponent<ForkliftController>();
        if (spawnedController == null)
            spawnedController = spawnedForklift.GetComponentInChildren<ForkliftController>();

        if (spawnedController == null)
        {
            Debug.LogError("WarehouseJobManager: The spawned forklift prefab has no ForkliftController. Shift was not started.", spawnedForklift);
            Destroy(spawnedForklift);
            spawnedForklift = null;
            return false;
        }

        // Bind the exact scene player explicitly. Do not depend on Awake's tag lookup
        // running at the right time on an instantiated forklift.
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null)
        {
            Debug.LogError("WarehouseJobManager: Could not find an active GameObject tagged Player.", this);
        }
        else
        {
            ForkliftPlayerInteractor interactor = spawnedForklift.GetComponent<ForkliftPlayerInteractor>();
            if (interactor == null)
                interactor = spawnedForklift.GetComponentInChildren<ForkliftPlayerInteractor>(true);

            if (interactor != null)
                interactor.BindPlayer(playerObject.transform);
            else
                Debug.LogError("WarehouseJobManager: Spawned forklift has no ForkliftPlayerInteractor component.", spawnedForklift);
        }

        WarehousePalletStorageSlot[] storageSlots = FindObjectsOfType<WarehousePalletStorageSlot>(true);
        foreach (WarehousePalletStorageSlot slot in storageSlots)
            slot.SetForklift(spawnedController);

        // The prefab carries the WSM controllers and the existing enter/exit script.
        // Disable player input until the player enters through ForkliftPlayerInteractor.
        ForkliftPlayerInput forkliftInput = spawnedForklift.GetComponent<ForkliftPlayerInput>();
        if (forkliftInput != null)
            forkliftInput.enablePlayerInput = false;

        lorryArrival.RemoveLorry();
        State = JobState.Active;
        lorryArrival.StartArrival();

        Debug.Log("WarehouseJobManager: Shift started. Store " + requiredPallets + " pallets.", this);
        return true;
    }

    /// <summary>Called by a storage slot only after a pallet has been secured on the rack.</summary>
    public void RegisterPalletStored(Rigidbody pallet)
    {
        if (State != JobState.Active || pallet == null)
            return;

        if (!storedPalletIds.Add(pallet.GetInstanceID()))
            return;

        Debug.Log("WarehouseJobManager: Stored pallet " + StoredPalletCount + "/" + requiredPallets + ".", pallet);

        if (StoredPalletCount >= requiredPallets)
            CompleteJob();
    }

    private void CompleteJob()
    {
        if (State != JobState.Active || paymentIssued)
            return;

        paymentIssued = true;
        State = JobState.Completed;
        PlayerEconomy.Instance.AddMoney(completionPayment, jobTitle + " shift completed");
        Debug.Log("WarehouseJobManager: Shift complete. Paid £" + completionPayment.ToString("N2") + ".", this);

        // Return the player before destroying the forklift, otherwise a seated
        // player would be destroyed along with the forklift hierarchy.
        if (spawnedForklift != null)
        {
            ForkliftPlayerInteractor interactor = spawnedForklift.GetComponent<ForkliftPlayerInteractor>();
            if (interactor == null)
                interactor = spawnedForklift.GetComponentInChildren<ForkliftPlayerInteractor>(true);

            if (interactor != null)
            {
                interactor.ReturnPlayerTo(playerReturnPoint);
            }
            else
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                    playerObject.transform.SetPositionAndRotation(playerReturnPoint.position, playerReturnPoint.rotation);
                else
                    Debug.LogError("WarehouseJobManager: Could not find the player to return outside the warehouse.", this);
            }

            Destroy(spawnedForklift);
            spawnedForklift = null;
        }

        // Remove the delivery lorry and any remaining cargo, even if the
        // player completed the required count before unloading every pallet.
        if (lorryArrival != null)
            lorryArrival.RemoveLorry();
    }

    private void OnGUI()
    {
        if (State == JobState.Available)
            return;

        string message = State == JobState.Active
            ? jobTitle + "  |  Pallets stored: " + StoredPalletCount + "/" + requiredPallets
            : jobTitle + "  |  Shift complete - £" + completionPayment.ToString("N2") + " paid";

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(12, 12, 8, 8)
        };
        GUI.Label(new Rect(15f, 15f, 430f, 42f), message, style);
    }
}
