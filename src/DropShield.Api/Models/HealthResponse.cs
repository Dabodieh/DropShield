namespace DropShield.Api.Models;

public sealed record HealthResponse(
    string Status,
    string Service,
    string StateProvider,
    string State);
