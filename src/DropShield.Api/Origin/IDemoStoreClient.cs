namespace DropShield.Api.Origin;

public interface IDemoStoreClient
{
    Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpRequest sourceRequest,
        CancellationToken cancellationToken);
}
