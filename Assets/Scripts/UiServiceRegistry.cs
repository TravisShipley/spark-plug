using System.Collections.Generic;
using UnityEngine;

public sealed class UiServiceRegistry : MonoBehaviour, IGeneratorLookup
{
    [SerializeField]
    private bool dontDestroyOnLoad = false;

    private readonly Dictionary<string, GeneratorService> generatorsById = new Dictionary<
        string,
        GeneratorService
    >(System.StringComparer.Ordinal);

    public WalletService Wallet { get; private set; }

    public static UiServiceRegistry Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);
    }

    public void Initialize(WalletService wallet)
    {
        Wallet = wallet;
    }

    public void RegisterGenerator(string generatorId, GeneratorService generator)
    {
        if (string.IsNullOrWhiteSpace(generatorId) || generator == null)
            return;

        generatorId = generatorId.Trim();
        generatorsById[generatorId] = generator;
    }

    public bool TryGetGenerator(string generatorId, out GeneratorService generator)
    {
        generator = null;

        if (string.IsNullOrWhiteSpace(generatorId))
            return false;

        generatorId = generatorId.Trim();
        return generatorsById.TryGetValue(generatorId, out generator) && generator != null;
    }

    public void Clear()
    {
        generatorsById.Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
