public interface IUpgradesContext
{
    WalletService Wallet { get; }
    bool TryGetGenerator(string generatorId, out GeneratorService generator);
}