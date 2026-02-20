public interface IGeneratorLookup
{
    bool TryGetGenerator(string generatorId, out GeneratorService generator);
}
