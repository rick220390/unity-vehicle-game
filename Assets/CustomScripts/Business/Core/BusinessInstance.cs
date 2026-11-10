using System;
using UnityEngine;

[Serializable]
public class BusinessInstance
{
    [Header("Business Information")]

    [SerializeField] private string businessId;

    [SerializeField] private BusinessDefinition definition;
    [SerializeField] private string businessName;

    [SerializeField] private bool isOwned;
    [SerializeField] private bool isOperating;
    [Header("Business Finance")]
    [SerializeField] private BusinessFinance finances;

    [Header("Business Inventory")]
    [SerializeField] private BusinessInventory inventory;


    public string BusinessId => businessId;

    public BusinessDefinition Definition => definition;
    public string BusinessName => businessName;
    public bool IsOwned => isOwned;
    public bool IsOperating => isOperating;
    public BusinessFinance Finances => finances;
    public BusinessInventory Inventory => inventory;
    public int CurrentCapacity => inventory.GetCurrentCapacity();
    public int AvailableCapacity => inventory.GetAvailableCapacity();
    public float StoragePercentage => inventory.GetStoragePercentage();

    public BusinessInstance(BusinessDefinition businessDefinition)
    {
        if (businessDefinition == null)
        {
            Debug.LogError("BusinessInstance: Business Definition missing");
            return;
        }
        businessId = Guid.NewGuid().ToString();

        definition = businessDefinition;
        businessName = businessDefinition.DisplayName;

        isOwned = true;
        isOperating = false;
        finances = new BusinessFinance(businessDefinition.StartingBusinessBalance);
        inventory = new BusinessInventory(businessDefinition.StorageCapacity);
    }
    public void AddStock (ProductDefinition product, int amount)
    {
        inventory.AddProduct(product, amount);
    }
    public void RemoveStock(ProductDefinition product, int amount)
    {
        inventory.RemoveProduct(product, amount); 
    }
    public ProductStock GetStock(ProductDefinition product)
    {
        return inventory.GetStock(product);
    }
    public bool HasStock(ProductDefinition product, int amount)
    {
        ProductStock stock = inventory.GetStock(product);
        if (stock == null)
        {
            return false;
        }
        return stock.Quantity >= amount; 
    }
    public int GetTotalStockQuantity()
    {
        int total = 0;
        foreach (ProductStock item in inventory.StockItems)
        {
            total += item.Quantity;
        }
        return total;
    
    }
    public bool IsInventoryEmpty()
    {
        return inventory.IsEmpty();
    }
   
  
}

