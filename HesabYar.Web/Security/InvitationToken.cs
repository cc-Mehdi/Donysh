using System.Security.Cryptography;
using System.Text;

namespace HesabYar.Web.Security;

public static class InvitationToken
{
    public static string Create() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
