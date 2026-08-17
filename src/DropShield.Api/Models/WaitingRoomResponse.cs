namespace DropShield.Api.Models;

public sealed record WaitingRoomResponse(
    string Status,
    string Drop,
    int RetryAfterSeconds);
