using UnityEngine;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;





[Serializable]
public class BusinessInventory
{
    [Header("Business Stock")]

    [SerializeField] private List<ProductStock> stockItems = new List<ProductStock>();
    [Header("Storage Capacity")]
    [SerializeField] private int maximumCapacity = 100;
    public IReadOnlyList<ProductStock> StockItems => stockItems;



    public void AddProduct(ProductDefinition product, int amount)
{
    if (product == null)
    {
        Debug.LogWarning("BusinessInventory: Product is missing.");
        return;
    }
    if (amount <= 0)
     {
        Debug.LogWarning("BusinessInventory: Amount must be greater then 0");
        return;
     }
    if (!CanFitStock(amount))
     {
        Debug.LogWarning("BusinessInventory: Not Enough Storage capacity. ");
       return;
     }
     


    ProductStock existingStock = stockItems.Find(item => item.Product == product);

    if (existingStock != null)
    {
        existingStock.AddStock(amount);
        return;
    }
    ProductStock newStock = new ProductStock(product, amount);
    stockItems.Add(newStock);

    if (amount <= 0)
        {
            Debug.LogWarning("BusinessInventory: Amount must be greater then 0");
            return;
        }
       
}
   
    
    public ProductStock GetStock(ProductDefinition product)
    {
        if (product == null)
        {
            return null;
        }
        return  stockItems.Find(item => item.Product == product);
    }
    public void RemoveProduct(ProductDefinition product, int amount)
    {
        if (product == null)
        {
            Debug.LogWarning("businessOnventory: product is missing ");
            return;
        }
        if (amount <= 0)
        {
            Debug.LogWarning("BusinessInventory: Amount must be more then 0");
            return;
        }
        ProductStock existingStock = GetStock(product);
        
        if (existingStock == null)
        {
            Debug.LogWarning("BussinessInventory: Product not found in the inventroy");
            return;
        }
        if (existingStock.Quantity < amount)
        {
            Debug.LogWarning("BusinessInventory: Not enough Stock available");
            return;
        }
        existingStock.RemoveStock(amount);
        if (existingStock.Quantity <= 0)
        {
            stockItems.Remove(existingStock);
        }
    }
    public int GetCurrentCapacity()
    {
        int total = 0;
        foreach (ProductStock item in stockItems)
        {
            total += item.Quantity;
        }
        return total; 
    }
    public int GetAvailableCapacity()
    {
        return maximumCapacity - GetCurrentCapacity();
    }
    public bool CanFitStock(int amount)
    {
        if (amount <= 0)
        {
            return false;
        }
        return GetAvailableCapacity() >= amount;
    }
    public bool IsEmpty()
    {
        return stockItems.Count == 0;
    }
    public float GetStoragePercentage()
    {
        if  (maximumCapacity <= 0)
        {
            return 0f;
        }
        return (float)GetCurrentCapacity() / maximumCapacity * 100f;
    }
     public BusinessInventory()
    {
        
    }
    public BusinessInventory(int capacity)
    {
        maximumCapacity = Math.Max(1, capacity);
    }
   
    
    

}

