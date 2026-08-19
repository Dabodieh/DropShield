namespace DropShield.Api.Origin;

public interface IDemoStoreClient
{
    Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpRequest sourceRequest,
        CancellationToken cancellationToken,
        (string HeaderName, string Value)? originAssertionHeader = null,
        OriginForwardingProfile profile = OriginForwardingProfile.DemoStore);
}

public enum OriginForwardingProfile
{
    DemoStore,
    AdobeCommerce,
}
