using System.Security.Cryptography;

namespace DonyshMobile.Core;

/// <summary>Authenticated encryption for the private message queue; the key never lives beside the file.</summary>
public static class SmsStateProtection
{
    private const string KeyName = "donysh-mobile-sms-encryption-key-v1";
    private static readonly byte[] Header = "DNYSHSMS1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static bool IsEncrypted(ReadOnlySpan<byte> data) => data.StartsWith(Header);

    public static byte[] Encrypt(byte[] data, byte[] key)
    {
        var result = new byte[Header.Length + NonceSize + TagSize + data.Length];
        Header.CopyTo(result, 0);
        var nonce = result.AsSpan(Header.Length, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var cipher = new AesGcm(key, TagSize);
        cipher.Encrypt(nonce, data, result.AsSpan(Header.Length + NonceSize + TagSize),
            result.AsSpan(Header.Length + NonceSize, TagSize), Header);
        return result;
    }

    public static byte[] Decrypt(byte[] data, byte[] key)
    {
        if (!IsEncrypted(data) || data.Length < Header.Length + NonceSize + TagSize)
            throw new InvalidDataException("فایل پیامک‌ها ناقص یا آسیب‌دیده است.");
        var result = new byte[data.Length - Header.Length - NonceSize - TagSize];
        try
        {
            using var cipher = new AesGcm(key, TagSize);
            cipher.Decrypt(data.AsSpan(Header.Length, NonceSize),
                data.AsSpan(Header.Length + NonceSize + TagSize),
                data.AsSpan(Header.Length + NonceSize, TagSize), result, Header);
            return result;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(result);
            throw new InvalidDataException("اطلاعات پیامک‌ها قابل بازگشایی نیست. داده‌های موجود حفظ شدند.", exception);
        }
    }

    internal static async Task<byte[]> GetKeyAsync(CancellationToken cancellationToken, bool createIfMissing = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if ANDROID || WINDOWS || IOS || MACCATALYST || TIZEN
        var saved = await SecureStorage.Default.GetAsync(KeyName);
        if (saved is not null)
        {
            var key = Convert.FromBase64String(saved);
            if (key.Length != 32) throw new InvalidDataException("کلید محافظت از پیامک‌ها معتبر نیست.");
            return key;
        }
        if (!createIfMissing)
            throw new InvalidDataException("کلید محافظت از پیامک‌ها در دسترس نیست. داده‌های موجود حفظ شدند.");
        var created = RandomNumberGenerator.GetBytes(32);
        await SecureStorage.Default.SetAsync(KeyName, Convert.ToBase64String(created));
        return created;
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("A secure platform key provider is required.");
#endif
    }
}
