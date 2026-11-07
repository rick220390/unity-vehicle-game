using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent GBP wallet shared by jobs and businesses. Add this component to
/// one scene object; it survives scene changes and saves every balance change.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerEconomy : MonoBehaviour
{
    [Serializable]
    public sealed class Transaction
    {
        public string description;
        public float amount;
        public float balanceAfter;
        public string timestampUtc;
    }

    [Serializable]
    private sealed class TransactionList
    {
        public List<Transaction> items = new List<Transaction>();
    }

    private const string BalanceKey = "Game.PlayerEconomy.Balance.GBP";
    private const string TransactionsKey = "Game.PlayerEconomy.Transactions";
    private const int MaximumTransactions = 30;

    public static PlayerEconomy Instance { get; private set; }
    public event Action<float> BalanceChanged;

    [SerializeField, Min(0f)] private float startingBalance = 250f;
    [SerializeField, Min(0f)] private float balance;
    [SerializeField] private bool resetSavedBalanceOnAwake;

    private readonly List<Transaction> transactions = new List<Transaction>();

    public float Balance => balance;
    public IReadOnlyList<Transaction> Transactions => transactions;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (resetSavedBalanceOnAwake)
        {
            balance = startingBalance;
            transactions.Clear();
            Save();
            resetSavedBalanceOnAwake = false;
        }
        else if (PlayerPrefs.HasKey(BalanceKey))
        {
            balance = Mathf.Max(0f, PlayerPrefs.GetFloat(BalanceKey));
            LoadTransactions();
        }
        else
        {
            balance = startingBalance;
            Save();
        }

        BalanceChanged?.Invoke(balance);
    }

    public void AddMoney(float amount, string description)
    {
        if (amount <= 0f)
        {
            Debug.LogWarning("PlayerEconomy: AddMoney requires a positive amount.", this);
            return;
        }

        balance += amount;
        RecordTransaction(amount, description);
    }

    public bool TrySpendMoney(float amount, string description)
    {
        if (amount <= 0f)
        {
            Debug.LogWarning("PlayerEconomy: TrySpendMoney requires a positive amount.", this);
            return false;
        }

        if (balance < amount)
            return false;

        balance -= amount;
        RecordTransaction(-amount, description);
        return true;
    }

    private void RecordTransaction(float amount, string description)
    {
        transactions.Insert(0, new Transaction
        {
            description = string.IsNullOrWhiteSpace(description) ? "Transaction" : description,
            amount = amount,
            balanceAfter = balance,
            timestampUtc = DateTime.UtcNow.ToString("o")
        });

        if (transactions.Count > MaximumTransactions)
            transactions.RemoveRange(MaximumTransactions, transactions.Count - MaximumTransactions);

        Save();
        BalanceChanged?.Invoke(balance);
    }

    private void Save()
    {
        PlayerPrefs.SetFloat(BalanceKey, balance);
        PlayerPrefs.SetString(TransactionsKey, JsonUtility.ToJson(new TransactionList { items = transactions }));
        PlayerPrefs.Save();
    }

    private void LoadTransactions()
    {
        transactions.Clear();
        string json = PlayerPrefs.GetString(TransactionsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return;

        try
        {
            TransactionList saved = JsonUtility.FromJson<TransactionList>(json);
            if (saved != null && saved.items != null)
                transactions.AddRange(saved.items);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("PlayerEconomy: Could not load transaction history: " + exception.Message, this);
        }
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperRight,
            fontSize = 20,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(Screen.width - 250f, 15f, 230f, 34f), "£" + balance.ToString("N2"), style);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
