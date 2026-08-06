#:property TargetFramework=net10.0
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Security.Cryptography;

if (args.Length < 1)
{
    return 2;
}

switch (args[0])
{
    case "generate" when args.Length == 3:
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(args[1], Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
        File.WriteAllText(args[2], Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;
    }
    case "sign" when args.Length == 3:
    {
        var privateKeyValue = Environment.GetEnvironmentVariable("GAMENEST_UPDATE_PRIVATE_KEY");
        if (string.IsNullOrWhiteSpace(privateKeyValue))
        {
            return 3;
        }

        var privateKey = Convert.FromBase64String(privateKeyValue);
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
            if (bytesRead != privateKey.Length)
            {
                return 4;
            }

            var manifest = File.ReadAllBytes(args[1]);
            var signature = ecdsa.SignData(
                manifest,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            File.WriteAllBytes(args[2], signature);
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }
    case "verify" when args.Length == 3:
    {
        var publicKeyValue = Environment.GetEnvironmentVariable("GAMENEST_UPDATE_PUBLIC_KEY");
        if (string.IsNullOrWhiteSpace(publicKeyValue))
        {
            return 5;
        }

        var publicKey = Convert.FromBase64String(publicKeyValue);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
        if (bytesRead != publicKey.Length)
        {
            return 6;
        }

        var verified = ecdsa.VerifyData(
            File.ReadAllBytes(args[1]),
            File.ReadAllBytes(args[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return verified ? 0 : 7;
    }
    default:
        return 2;
}
