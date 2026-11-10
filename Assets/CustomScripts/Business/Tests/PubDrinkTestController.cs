using UnityEngine;

/// <summary>
/// Temporary in-scene test panel for the pub beer system.
/// Add this to a GameObject, assign a DrinkDefinition, and use the buttons in Play Mode.
/// This is a development tool and can be removed when the real player interaction is ready.
/// </summary>
public class PubDrinkTestController : MonoBehaviour
{
    [Header("Test Setup")]
    [SerializeField] private BeerTap beerTap;
    [SerializeField] private DrinkDefinition testDrink;
    [SerializeField] private bool createTestKegOnStart = true;

    private string statusMessage = "Ready. Assign a drink definition to begin.";

    private void Start()
    {
        if (beerTap == null)
            beerTap = GetComponent<BeerTap>();

        if (beerTap == null)
        {
            statusMessage = "Missing BeerTap. Add BeerTap to this GameObject or assign one.";
            Debug.LogError("PubDrinkTestController: No BeerTap reference found.");
            return;
        }

        if (testDrink == null)
        {
            statusMessage = "Assign a DrinkDefinition asset in the Inspector.";
            return;
        }

        if (createTestKegOnStart)
            LoadTestKeg();
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(15, 15, 340, 430), GUI.skin.box);

        GUILayout.Label("PUB DRINK SYSTEM - TEST PANEL");
        GUILayout.Space(8);

        if (beerTap == null)
        {
            GUILayout.Label("BeerTap reference is missing.");
        }
        else
        {
            GUILayout.Label($"Tap: {beerTap.TapName}");
            GUILayout.Label($"Drink: {GetDrinkName()}");
            GUILayout.Label($"Keg: {(beerTap.HasKeg ? "Loaded" : "Empty")}");
            GUILayout.Label($"Connection: {GetConnectionStatus()}");
            GUILayout.Label($"Remaining: {GetRemainingLitres():0.000} L");
            GUILayout.Label($"Pints dispensed: {beerTap.PintsDispensed}");
            GUILayout.Label($"Recorded sales: {beerTap.TotalSales:0.00}");
        }

        GUILayout.Space(12);

        GUI.enabled = beerTap != null && testDrink != null;
        if (GUILayout.Button("Load a new full test keg", GUILayout.Height(32)))
            LoadTestKeg();

        GUI.enabled = beerTap != null && beerTap.HasKeg;
        if (GUILayout.Button("Connect keg to tap", GUILayout.Height(32)))
            SetStatus(beerTap.ConnectKeg() ? "Keg connected." : "Could not connect keg. Check the Console.");

        if (GUILayout.Button("Disconnect keg", GUILayout.Height(32)))
        {
            beerTap.DisconnectKeg();
            SetStatus("Keg disconnected.");
        }

        if (GUILayout.Button("POUR ONE PINT", GUILayout.Height(44)))
        {
            if (beerTap.TryPourPint(out float salePrice))
                SetStatus($"Pint poured. Recorded sale: {salePrice:0.00}.");
            else
                SetStatus("Pint could not be poured. Is a keg loaded, connected and sufficiently full?");
        }

        if (GUILayout.Button("Reset sales counter", GUILayout.Height(30)))
        {
            beerTap.ResetSales();
            SetStatus("Sales counter reset. Keg contents unchanged.");
        }

        GUI.enabled = true;
        GUILayout.Space(10);
        GUILayout.Label("Status: " + statusMessage);
        GUILayout.Label("This panel is for Play Mode testing only.");

        GUILayout.EndArea();
    }

    private void LoadTestKeg()
    {
        if (beerTap == null || testDrink == null)
        {
            SetStatus("Assign both BeerTap and Test Drink first.");
            return;
        }

        if (beerTap.LoadKeg(testDrink))
            SetStatus($"Loaded a full {testDrink.ProductName} keg.");
    }

    private string GetDrinkName()
    {
        return beerTap != null && beerTap.ConnectedKeg != null && beerTap.ConnectedKeg.Drink != null
            ? beerTap.ConnectedKeg.Drink.ProductName
            : "No drink loaded";
    }

    private string GetConnectionStatus()
    {
        if (beerTap == null || !beerTap.HasKeg)
            return "No keg";
        return beerTap.ConnectedKeg.IsConnected ? "Connected" : "Not connected";
    }

    private float GetRemainingLitres()
    {
        return beerTap != null && beerTap.ConnectedKeg != null
            ? beerTap.ConnectedKeg.RemainingLitres
            : 0f;
    }

    private void SetStatus(string message)
    {
        statusMessage = message;
        Debug.Log("Pub Drink Test: " + message);
    }
}
