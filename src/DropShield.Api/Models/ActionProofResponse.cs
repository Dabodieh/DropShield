namespace DropShield.Api.Models;

public sealed record ActionProofResponse(string Action, string Token, int ExpiresInSeconds);
