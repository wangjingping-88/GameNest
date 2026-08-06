using GameNest.Infrastructure.Updates;

namespace GameNest.Infrastructure.Tests;

public sealed class ApplicationUpdateOptionsTests
{
    [Fact]
    public void CreateDefaultIncludesProductionTrustedKey()
    {
        var options = ApplicationUpdateOptions.CreateDefault();

        var trustedKey = Assert.Single(options.TrustedKeys);
        Assert.Equal("GAMENESTPUBLIC", trustedKey.KeyId);
        Assert.Equal(
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEMqfR1EWPTKtD0OwOxGhwE1WlpJbN1opxHCKjEFogVvnt6lfrTRNSvs+Hl7hMTzKG2POMtCTQYgf+4lm+rYQZXQ==",
            trustedKey.SubjectPublicKeyInfoBase64);
    }
}
