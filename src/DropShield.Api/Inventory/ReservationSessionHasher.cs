using System.Security.Cryptography;
using System.Text;
using DropShield.Api.Security;

namespace DropShield.Api.Inventory;

public sealed class ReservationSessionHasher(InternalHashingKeyProvider hashingKeys)
{
    public string Hash(string sessionId) => Convert.ToHexString(HMACSHA256.HashData(
        hashingKeys.Material,
        Encoding.UTF8.GetBytes($"DropShield.Reservation.Session.v1:{sessionId}"))).ToLowerInvariant();
}
