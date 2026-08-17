namespace DropShield.DemoStore.Options;

public sealed class DemoStoreOptions
{
    public const string SectionName = "DemoStore";

    public int StockLookupDelayMilliseconds { get; init; } = 50;

    public int InitialAvailableStock { get; init; } = 500;
}

