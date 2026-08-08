using System.Security.Cryptography;
using Yordamchi.Remoting.Security;

namespace Yordamchi.Tests.Remoting;

/// <summary>
/// Shifrlash va kalit almashinuvi. Asosiy talab: to'g'ri kalit bilan ochiladi, <b>har qanday</b>
/// o'zgartirish yoki noto'g'ri kalit esa istisno beradi — jimgina buzuq matn qaytmaydi.
/// </summary>
public sealed class SecurityTests
{
    [Fact]
    public void A_message_encrypts_and_decrypts_back()
    {
        var key = SessionCipher.NewKey();
        var message = System.Text.Encoding.UTF8.GetBytes("Salom, agent!");

        var sealedBytes = SessionCipher.Encrypt(key, message);
        var opened = SessionCipher.Decrypt(key, sealedBytes);

        Assert.Equal(message, opened);
        Assert.NotEqual(message, sealedBytes[..message.Length]); // haqiqatan shifrlangan
    }

    [Fact]
    public void The_session_key_is_the_expected_size()
    {
        Assert.Equal(32, SessionCipher.NewKey().Length);

        // Ketma-ket ikkita kalit bir xil bo'lmasligi kerak.
        Assert.NotEqual(SessionCipher.NewKey(), SessionCipher.NewKey());
    }

    [Fact]
    public void A_tampered_ciphertext_is_rejected()
    {
        var key = SessionCipher.NewKey();
        var sealedBytes = SessionCipher.Encrypt(key, [1, 2, 3, 4, 5, 6, 7, 8]);

        sealedBytes[^1] ^= 0x01; // bitta bit o'zgartirildi

        // AES-GCM teg mos kelmasa AuthenticationTagMismatchException (CryptographicException vorisi) tashlaydi.
        Assert.ThrowsAny<CryptographicException>(() => SessionCipher.Decrypt(key, sealedBytes));
    }

    [Fact]
    public void A_wrong_key_cannot_open_the_message()
    {
        var sealedBytes = SessionCipher.Encrypt(SessionCipher.NewKey(), [10, 11, 12]);

        Assert.ThrowsAny<CryptographicException>(() => SessionCipher.Decrypt(SessionCipher.NewKey(), sealedBytes));
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SessionCipher.Encrypt(new byte[16], [1, 2, 3]));
    }

    [Fact]
    public void Too_short_data_is_refused_before_decrypt()
    {
        Assert.Throws<CryptographicException>(() => SessionCipher.Decrypt(SessionCipher.NewKey(), [1, 2, 3]));
    }

    // =================================================================================
    //  RSA kalit almashinuvi
    // =================================================================================

    [Fact]
    public void A_session_key_wraps_to_the_public_key_and_unwraps_with_the_private_key()
    {
        using var keyPair = KeyExchange.NewKeyPair();
        var publicBytes = KeyExchange.ExportPublicKey(keyPair);
        using var publicOnly = KeyExchange.ImportPublicKey(publicBytes);

        var sessionKey = SessionCipher.NewKey();
        var wrapped = KeyExchange.WrapSessionKey(publicOnly, sessionKey);
        var unwrapped = KeyExchange.UnwrapSessionKey(keyPair, wrapped);

        Assert.Equal(sessionKey, unwrapped);
        Assert.NotEqual(sessionKey, wrapped); // o'ralgan holida ochiq turmaydi
    }

    [Fact]
    public void The_full_handshake_then_a_sealed_packet_round_trips()
    {
        // Master vaqtinchalik RSA juftini yaratadi va ochiq kalitni yuboradi.
        using var masterKeys = KeyExchange.NewKeyPair();
        var masterPublic = KeyExchange.ExportPublicKey(masterKeys);

        // Agent sessiya kalitini yaratib, master ochiq kaliti bilan o'raydi.
        using var masterPublicOnAgent = KeyExchange.ImportPublicKey(masterPublic);
        var agentSessionKey = SessionCipher.NewKey();
        var wrapped = KeyExchange.WrapSessionKey(masterPublicOnAgent, agentSessionKey);

        // Master o'z maxfiy kaliti bilan sessiya kalitini ochadi.
        var masterSessionKey = KeyExchange.UnwrapSessionKey(masterKeys, wrapped);
        Assert.Equal(agentSessionKey, masterSessionKey);

        // Endi ikkala tomon bir xil sessiya kaliti bilan xabar almashadi.
        var payload = System.Text.Encoding.UTF8.GetBytes("ekran kadri");
        var sealedByAgent = SessionCipher.Encrypt(agentSessionKey, payload);
        var openedByMaster = SessionCipher.Decrypt(masterSessionKey, sealedByAgent);

        Assert.Equal(payload, openedByMaster);
    }

    [Fact]
    public void A_wrong_private_key_cannot_unwrap_the_session_key()
    {
        using var keyPair = KeyExchange.NewKeyPair();
        using var strangerKeys = KeyExchange.NewKeyPair();

        var wrapped = KeyExchange.WrapSessionKey(
            KeyExchange.ImportPublicKey(KeyExchange.ExportPublicKey(keyPair)),
            SessionCipher.NewKey());

        Assert.ThrowsAny<CryptographicException>(() => KeyExchange.UnwrapSessionKey(strangerKeys, wrapped));
    }
}
