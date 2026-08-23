using System.Security.Cryptography;
using System.Text;

namespace FateTakesYouHome.Services;

/// <summary>
/// Encrypts the Home Assistant access token so it never sits in a settings file as plain text.
/// </summary>
/// <remarks>
/// <para>
/// Uses DPAPI at <see cref="DataProtectionScope.CurrentUser"/>. The key is derived from the user's
/// Windows credentials, so the ciphertext is unreadable by other accounts on the machine and
/// useless if the settings file is copied elsewhere.
/// </para>
/// <para>
/// This is not protection against malware already running as the user — nothing on the client side
/// can be. It protects against the realistic problems: a token in a synced roaming profile, a
/// settings file pasted into a bug report, or a backup that ends up somewhere it should not.
/// A Home Assistant Long-Lived Access Token is a bearer credential with no expiry, so leaving it
/// readable would be careless.
/// </para>
/// </remarks>
public static class SecretProtector
{
    /// <summary>
    /// Additional entropy mixed into the derivation.
    /// </summary>
    /// <remarks>
    /// Constant and public — it is not a key. Its purpose is to bind the ciphertext to this
    /// application, so a blob encrypted by another program running as the same user cannot be
    /// swapped in and decrypted here.
    /// </remarks>
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("VagueDustin Enterprises/Fate Takes You Home/token/v1");

    /// <summary>Encrypts a secret and returns it Base64-encoded. Null or empty round-trips as null.</summary>
    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        try
        {
            byte[] cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(cipher);
        }
        catch (CryptographicException)
        {
            // DPAPI is unavailable, which happens in some sandboxed or service contexts. Refusing
            // to store the token is the correct outcome; storing it unencrypted is not.
            return null;
        }
    }

    /// <summary>
    /// Decrypts a Base64 blob produced by <see cref="Protect"/>.
    /// </summary>
    /// <returns>
    /// The plaintext, or null when the blob is absent, malformed, or was encrypted by a different
    /// Windows account. All three mean the same thing to the caller: there is no usable token.
    /// </returns>
    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            return null;
        }

        try
        {
            byte[] cipher = Convert.FromBase64String(protectedBase64);

            byte[] plain = ProtectedData.Unprotect(
                cipher, Entropy, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Renders a token for display without revealing it.
    /// </summary>
    /// <remarks>
    /// Shows the last four characters only. Enough for somebody to confirm which token is stored
    /// when they have several, without putting the credential on screen during a screen share.
    /// </remarks>
    public static string Describe(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "No token stored";
        }

        string trimmed = token.Trim();

        return trimmed.Length <= 4
            ? "Stored"
            : $"Stored · ends in {trimmed[^4..]}";
    }
}
