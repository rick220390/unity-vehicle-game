using UnityEngine;

/// <summary>
/// A pub beer tap that can be connected to one keg and dispense standard pints.
/// This is the gameplay-facing layer between a physical keg and serving a drink.
/// </summary>
public class BeerTap : MonoBehaviour
{
    [Header("Tap Settings")]
    [SerializeField] private string tapName = "Lager Tap";

    private KegInstance connectedKeg;
    private int pintsDispensed;
    private float totalSales;

    public string TapName => tapName;
    public KegInstance ConnectedKeg => connectedKeg;
    public int PintsDispensed => pintsDispensed;
    public float TotalSales => totalSales;
    public bool HasKeg => connectedKeg != null;

    /// <summary>
    /// Loads a newly delivered keg onto this tap.
    /// The keg is not connected until ConnectKeg is called.
    /// </summary>
    public bool LoadKeg(DrinkDefinition drink)
    {
        if (drink == null)
        {
            Debug.LogWarning("BeerTap: Cannot load a keg without a drink definition.");
            return false;
        }

        if (connectedKeg != null && connectedKeg.IsConnected)
            connectedKeg.Disconnect();

        connectedKeg = new KegInstance(drink);
        Debug.Log($"BeerTap: Loaded a full {drink.ProductName} keg.");
        return true;
    }

    public bool ConnectKeg()
    {
        if (connectedKeg == null)
        {
            Debug.LogWarning("BeerTap: There is no keg loaded.");
            return false;
        }

        bool connected = connectedKeg.Connect();

        if (connected)
            Debug.Log($"BeerTap: {connectedKeg.Drink.ProductName} keg connected.");

        return connected;
    }

    public void DisconnectKeg()
    {
        if (connectedKeg == null || !connectedKeg.IsConnected)
            return;

        connectedKeg.Disconnect();
        Debug.Log("BeerTap: Keg disconnected.");
    }

    /// <summary>
    /// Dispenses one standard pint. Sales are recorded only if dispensing succeeds.
    /// </summary>
    public bool TryPourPint(out float salePrice)
    {
        salePrice = 0f;

        if (connectedKeg == null || connectedKeg.Drink == null)
        {
            Debug.LogWarning("BeerTap: No keg is loaded.");
            return false;
        }

        DrinkDefinition drink = connectedKeg.Drink;

        if (!connectedKeg.TryDispense(drink.PintSizeLitres))
            return false;

        salePrice = drink.SalePricePerPint;
        pintsDispensed++;
        totalSales += salePrice;

        Debug.Log($"Poured a pint of {drink.ProductName}. Sale value: {salePrice:0.00}. Remaining: {connectedKeg.RemainingLitres:0.000} L.");
        return true;
    }

    /// <summary>
    /// Records the sale after the glass has been filled by the gradual pour controller.
    /// The beer has already been dispensed continuously during the pour.
    /// </summary>
    public void RecordCompletedPint()
    {
        if (connectedKeg == null || connectedKeg.Drink == null)
            return;

        float salePrice = connectedKeg.Drink.SalePricePerPint;
        pintsDispensed++;
        totalSales += salePrice;
        Debug.Log($"Pint sale recorded for {connectedKeg.Drink.ProductName}. Sale value: {salePrice:0.00}.");
    }

    /// <summary>
    /// Resets the tap's test sales counters without changing the keg contents.
    /// </summary>
    public void ResetSales()
    {
        pintsDispensed = 0;
        totalSales = 0f;
    }
}
