using System.Security.Cryptography;
using System.Text;

namespace Haworks.BuildingBlocks.Common;

public static class CryptoHelper
{
    public static string ComputeHMACSHA256(string key, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexStringLower(hashBytes);
    }

    public static string ComputeHash(string data)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexStringLower(hashBytes);
    }
}
