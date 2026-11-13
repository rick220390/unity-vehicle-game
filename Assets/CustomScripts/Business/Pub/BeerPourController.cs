using UnityEngine;

/// <summary>
/// Fills a pint only while the player holds the interaction key.
/// Beer is removed from the keg continuously as the glass fills.
/// </summary>
public class BeerPourController : MonoBehaviour
{
    [Header("References")]
    public BeerTap beerTap;
    public BeerGlassLiquid glassLiquid;

    [Header("Pour Settings")]
    [Tooltip("How many pints are poured per second while the key is held.")]
    [Min(0.01f)] public float pintsPerSecond = 0.15f;

    [Tooltip("The BeerGlassLiquid fill value that represents one pint. Calibrate this to the glass.")]
    [Min(0.001f)] public float fullPintFillAmount = 0.05f;

    [Header("Glass Setup")]
    [Tooltip("For this test setup, start with an empty glass each time Play Mode begins.")]
    public bool emptyGlassOnStart = true;

    private bool isPouring;

    public bool IsPouring => isPouring;

    private void Awake()
    {
        if (beerTap == null)
            beerTap = GetComponent<BeerTap>();
    }

    private void Start()
    {
        // The test glass must start empty. Otherwise a saved Inspector fill value
        // of 0.05 would already equal the calibrated full-pint target.
        if (emptyGlassOnStart && glassLiquid != null)
            glassLiquid.SetFillAmount(0f);

        if (glassLiquid == null)
            Debug.LogError("Beer Pour: Glass Liquid reference is missing.", this);
        if (beerTap == null)
            Debug.LogError("Beer Pour: Beer Tap reference is missing.", this);
    }

    /// <summary>Called while the player holds the interaction key.</summary>
    public void SetPouring(bool shouldPour)
    {
        if (!shouldPour)
        {
            isPouring = false;
            return;
        }

        if (beerTap == null || glassLiquid == null || !beerTap.HasKeg ||
            !beerTap.ConnectedKeg.IsConnected || glassLiquid.fillAmount >= fullPintFillAmount)
        {
            isPouring = false;
            return;
        }

        isPouring = true;
    }

    private void Update()
    {
        if (!isPouring)
            return;

        if (beerTap == null || glassLiquid == null || !beerTap.HasKeg ||
            !beerTap.ConnectedKeg.IsConnected)
        {
            isPouring = false;
            return;
        }

        KegInstance keg = beerTap.ConnectedKeg;
        DrinkDefinition drink = keg.Drink;

        if (drink == null || drink.PintSizeLitres <= 0f || keg.IsEmpty)
        {
            isPouring = false;
            Debug.Log("Beer Pour: The keg is empty.", this);
            return;
        }

        float targetFill = Mathf.Min(fullPintFillAmount,
            glassLiquid.fillAmount + fullPintFillAmount * pintsPerSecond * Time.deltaTime);

        float fillIncrease = targetFill - glassLiquid.fillAmount;
        float litresNeeded = (fillIncrease / fullPintFillAmount) * drink.PintSizeLitres;

        if (litresNeeded <= 0f)
        {
            isPouring = false;
            return;
        }

        if (!keg.TryDispense(litresNeeded))
        {
            isPouring = false;
            Debug.Log("Beer Pour: Not enough beer remains.", this);
            return;
        }

        glassLiquid.SetFillAmount(targetFill);

        if (glassLiquid.fillAmount >= fullPintFillAmount - 0.0001f)
        {
            beerTap.RecordCompletedPint();
            isPouring = false;
            Debug.Log("Beer Pour: Pint completed.", this);
        }
    }
}
