using UnityEngine;

/// <summary>
/// Product information attached to a physical keg GameObject.
/// The physical object can be carried and installed at a keg position.
/// BeerTap creates the gameplay KegInstance when this keg is installed.
/// </summary>
public class PhysicalKeg : MonoBehaviour
{
    [SerializeField] private DrinkDefinition drinkDefinition;

    public DrinkDefinition Drink => drinkDefinition;
    public bool IsInstalled { get; private set; }

    public void SetInstalled(bool installed)
    {
        IsInstalled = installed;
    }
}