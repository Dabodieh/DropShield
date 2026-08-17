using System.Security.Cryptography;
using System.Text;
using DropShield.Api.Admission;

namespace DropShield.Api.Inventory;

public sealed class ReservationSessionHasher(AdmissionSigningKeyProvider signingKeys)
{
    public string Hash(string sessionId) => Convert.ToHexString(HMACSHA256.HashData(
        signingKeys.GetActiveKey().Material,
        Encoding.UTF8.GetBytes($"DropShield.Reservation.Session.v1:{sessionId}"))).ToLowerInvariant();
}
