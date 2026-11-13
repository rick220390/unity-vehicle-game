using System.Collections;
using UnityEngine;

/// <summary>
/// Animates one pint filling in the glass, then records the sale through BeerTap.
/// Attach this to the beer dispenser and assign its BeerTap and glass liquid.
/// </summary>
public class BeerPourController : MonoBehaviour
{
    [Header("References")]
    public BeerTap beerTap;
    public BeerGlassLiquid glassLiquid;

    [Header("Pour Settings")]
    [Min(0.1f)] public float pourDuration = 3f;
    [Range(0.01f, 1f)] public float fillPerPint = 1f;

    private Coroutine pourRoutine;
    private bool isPouring;

    public bool IsPouring => isPouring;

    private void Awake()
    {
        if (beerTap == null)
            beerTap = GetComponent<BeerTap>();

        if (glassLiquid != null)
            glassLiquid.SetFillAmount(0f);
    }

    public bool TryStartPour()
    {
        if (isPouring)
        {
            Debug.Log("Beer Pour: A pint is already being poured.");
            return false;
        }

        if (beerTap == null || glassLiquid == null)
        {
            Debug.LogWarning("Beer Pour: Assign the BeerTap and BeerGlassLiquid references.");
            return false;
        }

        if (!beerTap.HasKeg || !beerTap.ConnectedKeg.IsConnected)
        {
            Debug.Log("Beer Pour: The tap is not connected to a keg.");
            return false;
        }

        KegInstance keg = beerTap.ConnectedKeg;
        if (keg.Drink == null || keg.RemainingLitres < keg.Drink.PintSizeLitres)
        {
            Debug.Log("Beer Pour: Not enough beer remains for a full pint.");
            return false;
        }

        if (glassLiquid.fillAmount >= 0.99f)
        {
            Debug.Log("Beer Pour: The glass is already full.");
            return false;
        }

        pourRoutine = StartCoroutine(PourRoutine());
        return true;
    }

    private IEnumerator PourRoutine()
    {
        isPouring = true;

        float startingFill = glassLiquid.fillAmount;
        float targetFill = Mathf.Min(1f, startingFill + fillPerPint);
        float elapsed = 0f;

        Debug.Log("Beer Pour: Pouring...");

        while (elapsed < pourDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / pourDuration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);

            glassLiquid.SetFillAmount(Mathf.Lerp(startingFill, targetFill, easedProgress));
            yield return null;
        }

        glassLiquid.SetFillAmount(targetFill);

        if (beerTap.TryPourPint(out float salePrice))
            Debug.Log($"Beer Pour: Pint completed. Sale recorded: {salePrice:0.00}.");
        else
        {
            glassLiquid.SetFillAmount(startingFill);
            Debug.LogWarning("Beer Pour: The pint could not be recorded; the glass fill was reset.");
        }

        isPouring = false;
        pourRoutine = null;
    }
}
