using System.Numerics;
using System.Text;
using BasisAccountServ.Api;
using NSec.Cryptography;
using Xunit;

namespace BasisAccountServ.Api.Tests;

public sealed class BasisDidProofTests
{
    [Theory]
    [InlineData("")]
    [InlineData("did:web:example.com")]
    [InlineData("did:key:")]
    [InlineData("did:key:z")]
    [InlineData("did:key:znot-a-valid-base58-key")]
    public void RejectsMalformedOrUnsupportedDidKeys(string did)
    {
        Assert.False(BasisDidKey.TryDecodeEd25519PublicKey(did, out _));
    }

    [Fact]
    public void DecodesBasisKnownDidKey()
    {
        const string did = "did:key:z6MkhaXgBZDvotDkL5257faiztiGiC2QtKLGpbnnEGta2doK";
        Assert.True(BasisDidKey.TryDecodeEd25519PublicKey(did, out var publicKey));
        Assert.Equal(32, publicKey.Length);
    }

    [Fact]
    public void VerifiesEd25519SignatureOverExactPayload()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var did = EncodeDid(publicKey);
        var payload = Encoding.UTF8.GetBytes("basis accountserv did-link proof");
        var signature = SignatureAlgorithm.Ed25519.Sign(key, payload);

        Assert.True(BasisDidProof.Verify(did, payload, signature));
        Assert.False(BasisDidProof.Verify(did, Encoding.UTF8.GetBytes("different payload"), signature));
        Assert.False(BasisDidProof.Verify(did, payload, new byte[63]));
    }

    [Fact]
    public void LinkPayloadIsBoundToAccountDidIssuerAndChallenge()
    {
        var account = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var expiry = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var nonce = new byte[32];
        var first = DidLinkPayload.Create("https://auth.example.test", account,
            "did:key:zExample", challenge, expiry, nonce);

        Assert.Equal(first, DidLinkPayload.Create("https://auth.example.test", account,
            "did:key:zExample", challenge, expiry, nonce));
        Assert.NotEqual(first, DidLinkPayload.Create("https://other.example.test", account,
            "did:key:zExample", challenge, expiry, nonce));
        Assert.NotEqual(first, DidLinkPayload.Create("https://auth.example.test", Guid.NewGuid(),
            "did:key:zExample", challenge, expiry, nonce));
        Assert.NotEqual(first, DidLinkPayload.Create("https://auth.example.test", account,
            "did:key:zOther", challenge, expiry, nonce));
    }

    private static string EncodeDid(byte[] publicKey)
    {
        var multicodecKey = new byte[34];
        multicodecKey[0] = 0xED;
        multicodecKey[1] = 0x01;
        publicKey.CopyTo(multicodecKey, 2);
        return "did:key:z" + EncodeBase58(multicodecKey);
    }

    private static string EncodeBase58(byte[] bytes)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var result = new StringBuilder();
        while (value > 0)
        {
            value = BigInteger.DivRem(value, 58, out var remainder);
            result.Insert(0, alphabet[(int)remainder]);
        }
        var zeroes = bytes.TakeWhile(b => b == 0).Count();
        return new string('1', zeroes) + result;
    }
}
