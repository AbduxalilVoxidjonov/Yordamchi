using System.Security.Cryptography;

namespace Yordamchi.Remoting.Security;

/// <summary>
/// Sessiya yuklarini <b>AES-256-GCM</b> bilan shifrlaydi. GCM ham maxfiylikni, ham
/// yaxlitlikni beradi: har bir xabar teg (tag) bilan keladi, shuning uchun bilib turib
/// qilingan bitta bayt o'zgartirish ham shifrni ochishda aniqlanadi (istisno tashlanadi).
/// <para>
/// Chiqish formati: <c>nonce(12) || tag(16) || shifrmatn</c>. Nonce har xabarda yangi va
/// tasodifiy — bir xil kalit bilan bir xil nonce ni takrorlash GCM ni buzadi, shuning uchun
/// u hech qachon qayta ishlatilmaydi.
/// </para>
/// </summary>
public static class SessionCipher
{
    /// <summary>AES-256 kaliti — 32 bayt.</summary>
    public const int KeySize = 32;

    /// <summary>GCM uchun tavsiya etilgan nonce uzunligi — 12 bayt.</summary>
    public const int NonceSize = 12;

    /// <summary>Autentifikatsiya tegi — 16 bayt.</summary>
    public const int TagSize = 16;

    /// <summary>Yangi tasodifiy 256-bitli sessiya kaliti yaratadi.</summary>
    public static byte[] NewKey()
    {
        var key = new byte[KeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Ochiq matnni shifrlaydi va <c>nonce || tag || shifrmatn</c> ni qaytaradi.
    /// </summary>
    public static byte[] Encrypt(byte[] key, ReadOnlySpan<byte> plaintext)
    {
        ValidateKey(key);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result.AsSpan(0, NonceSize));
        tag.CopyTo(result.AsSpan(NonceSize, TagSize));
        ciphertext.CopyTo(result.AsSpan(NonceSize + TagSize));

        return result;
    }

    /// <summary>
    /// <see cref="Encrypt"/> yaratgan blokni ochadi. Kalit noto'g'ri yoki ma'lumot
    /// o'zgartirilgan bo'lsa — <see cref="CryptographicException"/>.
    /// </summary>
    public static byte[] Decrypt(byte[] key, ReadOnlySpan<byte> data)
    {
        ValidateKey(key);

        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Shifrlangan ma'lumot juda qisqa — nonce yoki teg yetishmaydi.");

        var nonce = data.Slice(0, NonceSize);
        var tag = data.Slice(NonceSize, TagSize);
        var ciphertext = data.Slice(NonceSize + TagSize);

        var plaintext = new byte[ciphertext.Length];

        using var gcm = new AesGcm(key, TagSize);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySize)
            throw new ArgumentException($"Kalit {KeySize} bayt bo'lishi kerak (berilgani {key.Length}).", nameof(key));
    }
}
