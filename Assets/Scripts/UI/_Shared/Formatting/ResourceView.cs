using System;
using Ignition.Binding;
using UniRx;
using UnityEngine;

public sealed class ResourceTextProvider : DataProvider
{
    private const float MissingWalletLogDelaySeconds = 1f;

    [SerializeField]
    private string resourceId = "currencySoft";

    private readonly ReactiveProperty<string> formattedValue = new(string.Empty);
    private readonly CompositeDisposable subscriptions = new();
    private ResourceDefinition resourceDefinition;
    private float missingWalletElapsedSeconds;
    private bool hasFatalConfigurationError;
    private bool hasLoggedMissingWallet;
    private bool isBound;

    [Bindable("Formatted Value")]
    public IReadOnlyReactiveProperty<string> FormattedValue => formattedValue;

    public override object GetBindingData() => null;

    public override Type GetBindingDataType() => null;

    private void OnEnable()
    {
        hasFatalConfigurationError = false;
        hasLoggedMissingWallet = false;
        missingWalletElapsedSeconds = 0f;
        isBound = false;
        subscriptions.Clear();
        RebindChildren();
        TryBind();
    }

    private void Update()
    {
        if (isBound || hasFatalConfigurationError)
            return;

        if (UiServiceRegistry.Instance?.Wallet != null)
        {
            TryBind();
            return;
        }

        missingWalletElapsedSeconds += Time.unscaledDeltaTime;
        if (!hasLoggedMissingWallet && missingWalletElapsedSeconds >= MissingWalletLogDelaySeconds)
        {
            hasLoggedMissingWallet = true;
            Debug.LogError(
                "ResourceTextProvider: WalletService is unavailable. Ensure UiServiceRegistry is initialized before provider binding.",
                this
            );
        }
    }

    private void OnDisable()
    {
        subscriptions.Clear();
        isBound = false;
    }

    private void OnDestroy()
    {
        subscriptions.Dispose();
        formattedValue.Dispose();
    }

    private void TryBind()
    {
        if (isBound || hasFatalConfigurationError)
            return;

        var normalizedResourceId = NormalizeId(resourceId);
        if (string.IsNullOrEmpty(normalizedResourceId))
        {
            hasFatalConfigurationError = true;
            Debug.LogError("ResourceTextProvider: resourceId is empty.", this);
            return;
        }

        var wallet = UiServiceRegistry.Instance?.Wallet;
        if (wallet == null)
            return;

        if (!wallet.TryGetResourceDefinition(normalizedResourceId, out resourceDefinition))
        {
            hasFatalConfigurationError = true;
            Debug.LogError(
                $"ResourceTextProvider: Unknown resource '{normalizedResourceId}'. Check scene wiring and content.",
                this
            );
            return;
        }

        wallet
            .GetBalanceProperty(normalizedResourceId)
            .Subscribe(amount =>
            {
                formattedValue.Value = ResourceTextFormatter.FormatResource(
                    resourceDefinition,
                    amount
                );
            })
            .AddTo(subscriptions);

        isBound = true;
    }

    private static string NormalizeId(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}
