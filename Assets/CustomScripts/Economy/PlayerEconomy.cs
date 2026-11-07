using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Persistent GBP wallet shared by jobs and businesses. It is bootstrapped
/// automatically, survives scene changes and saves every balance change.
/// Currency is stored as integer pence to avoid floating-point money errors.
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
    [SerializeField] private bool resetSavedBalanceOnAwake;

    private long balancePence;
    private readonly List<Transaction> transactions = new List<Transaction>();

    public float Balance => balancePence / 100f;
    public IReadOnlyList<Transaction> Transactions => transactions;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureCreated()
    {
        if (Instance != null)
            return;

        GameObject economyObject = new GameObject("PlayerEconomy");
        economyObject.AddComponent<PlayerEconomy>();
    }

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
            balancePence = ToPence(startingBalance);
            transactions.Clear();
            Save();
            resetSavedBalanceOnAwake = false;
        }
        else if (PlayerPrefs.HasKey(BalanceKey))
        {
            long.TryParse(PlayerPrefs.GetString(BalanceKey, "0"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out balancePence);
            balancePence = Math.Max(0L, balancePence);
            LoadTransactions();
        }
        else
        {
            balancePence = ToPence(startingBalance);
            Save();
        }

        BalanceChanged?.Invoke(Balance);
    }

    public void AddMoney(float amount, string description)
    {
        long pence = ToPence(amount);
        if (pence <= 0L)
        {
            Debug.LogWarning("PlayerEconomy: AddMoney requires a positive amount.", this);
            return;
        }

        balancePence += pence;
        RecordTransaction(pence / 100f, description);
    }

    public bool TrySpendMoney(float amount, string description)
    {
        long pence = ToPence(amount);
        if (pence <= 0L || balancePence < pence)
            return false;

        balancePence -= pence;
        RecordTransaction(-pence / 100f, description);
        return true;
    }

    private void RecordTransaction(float amount, string description)
    {
        transactions.Insert(0, new Transaction
        {
            description = string.IsNullOrWhiteSpace(description) ? "Transaction" : description,
            amount = amount,
            balanceAfter = Balance,
            timestampUtc = DateTime.UtcNow.ToString("o")
        });

        if (transactions.Count > MaximumTransactions)
            transactions.RemoveRange(MaximumTransactions, transactions.Count - MaximumTransactions);

        Save();
        BalanceChanged?.Invoke(Balance);
    }

    private static long ToPence(float pounds)
    {
        return (long)Math.Round(pounds * 100d, MidpointRounding.AwayFromZero);
    }

    private void Save()
    {
        PlayerPrefs.SetString(BalanceKey, balancePence.ToString(CultureInfo.InvariantCulture));
        PlayerPrefs.SetString(TransactionsKey,
            JsonUtility.ToJson(new TransactionList { items = transactions }));
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
        GUI.Label(new Rect(Screen.width - 250f, 15f, 230f, 34f), "£" + Balance.ToString("N2"), style);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
