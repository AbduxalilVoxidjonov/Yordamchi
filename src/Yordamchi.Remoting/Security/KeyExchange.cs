using System.Security.Cryptography;

namespace Yordamchi.Remoting.Security;

/// <summary>
/// Ulanish boshidagi kalit almashinuvi (RSA-2048 + OAEP-SHA256).
/// <para>
/// <b>Oqim.</b> Ulanishni boshlovchi tomon vaqtinchalik RSA juftligini yaratadi va ochiq
/// kalitni yuboradi. Ikkinchi tomon yangi tasodifiy AES-256 sessiya kalitini shu ochiq kalit
/// bilan o'raydi va qaytaradi. Shundan so'ng ikkala tomon ham sessiya kalitini biladi va
/// keyingi barcha yuklar <see cref="SessionCipher"/> orqali shifrlanadi.
/// </para>
/// <para>
/// Maxfiy (RSA private) kalit hech qachon tarmoqqa chiqmaydi, sessiya kaliti esa faqat
/// o'ralgan holda uzatiladi — shu tufayli aloqani eshitib turgan tomon uni ochib ololmaydi.
/// </para>
/// </summary>
public static class KeyExchange
{
    /// <summary>RSA kalit uzunligi.</summary>
    public const int RsaKeySizeBits = 2048;

    private static readonly RSAEncryptionPadding Padding = RSAEncryptionPadding.OaepSHA256;

    /// <summary>Yangi vaqtinchalik RSA juftligini yaratadi.</summary>
    public static RSA NewKeyPair() => RSA.Create(RsaKeySizeBits);

    /// <summary>Ochiq kalitni uzatishga yaroqli baytlar (SubjectPublicKeyInfo) sifatida beradi.</summary>
    public static byte[] ExportPublicKey(RSA keyPair)
    {
        ArgumentNullException.ThrowIfNull(keyPair);
        return keyPair.ExportSubjectPublicKeyInfo();
    }

    /// <summary>Uzatilgan ochiq kalitni RSA obyektiga qaytaradi.</summary>
    public static RSA ImportPublicKey(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    /// <summary>Sessiya kalitini ochiq kalit bilan o'raydi (faqat maxfiy kalit egasi ocha oladi).</summary>
    public static byte[] WrapSessionKey(RSA recipientPublicKey, byte[] sessionKey)
    {
        ArgumentNullException.ThrowIfNull(recipientPublicKey);
        ArgumentNullException.ThrowIfNull(sessionKey);

        return recipientPublicKey.Encrypt(sessionKey, Padding);
    }

    /// <summary>O'ralgan sessiya kalitini maxfiy kalit bilan ochadi.</summary>
    public static byte[] UnwrapSessionKey(RSA ownKeyPair, byte[] wrappedKey)
    {
        ArgumentNullException.ThrowIfNull(ownKeyPair);
        ArgumentNullException.ThrowIfNull(wrappedKey);

        return ownKeyPair.Decrypt(wrappedKey, Padding);
    }
}
