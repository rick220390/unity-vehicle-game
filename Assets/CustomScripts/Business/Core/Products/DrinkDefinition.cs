using UnityEngine;

/// <summary>
/// Product data for a drink sold by the pub.
/// Each delivered keg will later become its own KegInstance,
/// which tracks the beer remaining inside that physical keg.
/// </summary>
[CreateAssetMenu(
    fileName = "NewDrinkDefinition",
    menuName = "Business/Products/Drink Definition"
)]
public sealed class DrinkDefinition : ProductDefinition
{
    [Header("Keg Settings")]
    [Tooltip("How many litres a full keg contains.")]
    [SerializeField, Min(1f)] private float kegCapacityLitres = 50f;

    [Tooltip("The price the business pays for one full keg.")]
    [SerializeField, Min(0f)] private float purchasePricePerKeg = 150f;

    [Header("Serving Settings")]
    [Tooltip("The amount of beer dispensed for one standard pint.")]
    [SerializeField, Min(0.01f)] private float pintSizeLitres = 0.568f;

    [Tooltip("The price customers pay for one pint.")]
    [SerializeField, Min(0f)] private float salePricePerPint = 5f;

    public float KegCapacityLitres => kegCapacityLitres;
    public float PurchasePricePerKeg => purchasePricePerKeg;
    public float PintSizeLitres => pintSizeLitres;
    public float SalePricePerPint => salePricePerPint;

    public float PintsPerFullKeg
    {
        get
        {
            if (pintSizeLitres <= 0f)
                return 0f;

            return kegCapacityLitres / pintSizeLitres;
        }
    }

    private void OnValidate()
    {
        kegCapacityLitres = Mathf.Max(1f, kegCapacityLitres);
        purchasePricePerKeg = Mathf.Max(0f, purchasePricePerKeg);
        pintSizeLitres = Mathf.Max(0.01f, pintSizeLitres);
        salePricePerPint = Mathf.Max(0f, salePricePerPint);
    }
}
