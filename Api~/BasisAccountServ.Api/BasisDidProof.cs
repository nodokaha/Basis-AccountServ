using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace BasisAccountServ.Api;

public static class BasisDidKey
{
    private const string Prefix = "did:key:z";
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private const byte Ed25519Multicodec = 0xED;

    public static bool TryDecodeEd25519PublicKey(string did, out byte[] publicKey)
    {
        publicKey = [];
        if (string.IsNullOrEmpty(did) || did.Length > 128 || !did.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var encoded = did[Prefix.Length..];
        if (encoded.Length == 0 || !TryDecodeBase58(encoded, out var decoded))
            return false;

        // Basis encodes the Ed25519 multicodec (0xED) as the varint bytes ED 01.
        if (decoded.Length != 34 || decoded[0] != Ed25519Multicodec || decoded[1] != 0x01)
            return false;

        publicKey = decoded[2..];
        return publicKey.Length == 32;
    }

    private static bool TryDecodeBase58(string value, out byte[] bytes)
    {
        bytes = [];
        BigInteger number = BigInteger.Zero;
        foreach (var character in value)
        {
            var digit = Base58Alphabet.IndexOf(character);
            if (digit < 0) return false;
            number = number * 58 + digit;
        }

        var body = number.IsZero ? [] : number.ToByteArray(isUnsigned: true, isBigEndian: true);
        var leadingZeroes = value.TakeWhile(c => c == '1').Count();
        bytes = new byte[leadingZeroes + body.Length];
        body.CopyTo(bytes, leadingZeroes);
        return true;
    }
}

public static class BasisDidProof
{
    public static bool Verify(string did, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 64 || !BasisDidKey.TryDecodeEd25519PublicKey(did, out var rawPublicKey))
            return false;

        try
        {
            var publicKey = PublicKey.Import(
                SignatureAlgorithm.Ed25519,
                rawPublicKey,
                KeyBlobFormat.RawPublicKey);
            return SignatureAlgorithm.Ed25519.Verify(publicKey, payload, signature);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public static class DidLinkPayload
{
    public const string Purpose = "accountserv:did-link:v1";

    // Length-prefixed UTF-8 fields avoid delimiter ambiguity. Clients sign these exact bytes.
    public static byte[] Create(
        string issuer,
        Guid accountId,
        string did,
        Guid challengeId,
        DateTimeOffset expiresAt,
        ReadOnlySpan<byte> nonce)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteField(writer, Purpose);
        WriteField(writer, issuer);
        WriteField(writer, accountId.ToString("D"));
        WriteField(writer, did);
        WriteField(writer, challengeId.ToString("D"));
        WriteField(writer, expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Write(nonce.Length);
        writer.Write(nonce);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteField(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
