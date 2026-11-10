using UnityEngine;

[CreateAssetMenu(fileName = "NewProduct", menuName = "Business/Products/Product Definition")]
public class ProductDefinition : ScriptableObject
{
    [Header("Product Information")]
    [SerializeField] private string productId;
    [SerializeField] private string productName;
    [SerializeField] private string description; 
    [Header("Product Category")]
    [SerializeField] private ProductCategory category;

    public ProductCategory Category => category;

    public string ProductId => productId;
    public string ProductName => productName;
    public string Description => description; 
}
