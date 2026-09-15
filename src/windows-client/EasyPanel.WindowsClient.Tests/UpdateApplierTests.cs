using System.Security.Cryptography;
using System.Text;
using EasyPanel.WindowsClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes de verificação e aplicação de atualização (Task 8.4 — R16.2–R16.5):
/// pacote válido é aplicado; hash/assinatura inválidos são recusados mantendo a
/// versão; falha de health-check dispara rollback.
/// </summary>
public sealed class UpdateApplierTests
{
    [Fact]
    public async Task ValidPackage_IsApplied()
    {
        var (verifier, sign) = CreateVerifier();
        var content = Encoding.UTF8.GetBytes("pacote-binario");
        var metadata = SignedMetadata(content, sign, "2.0.0");

        var host = new FakeHost(healthy: true);
        var applier = new UpdateApplier(verifier, host, NullLogger<UpdateApplier>.Instance);

        var outcome = await applier.TryApplyAsync(content, metadata, CancellationToken.None);

        Assert.Equal(UpdateOutcome.Applied, outcome);
        Assert.True(host.Applied);
        Assert.False(host.RolledBack);
    }

    [Fact]
    public async Task TamperedContent_IsRejected()
    {
        var (verifier, sign) = CreateVerifier();
        var original = Encoding.UTF8.GetBytes("pacote-original");
        var metadata = SignedMetadata(original, sign, "2.0.0");

        var tampered = Encoding.UTF8.GetBytes("pacote-adulterado");
        var host = new FakeHost(healthy: true);
        var applier = new UpdateApplier(verifier, host, NullLogger<UpdateApplier>.Instance);

        var outcome = await applier.TryApplyAsync(tampered, metadata, CancellationToken.None);

        Assert.Equal(UpdateOutcome.Rejected, outcome);
        Assert.False(host.Applied);
    }

    [Fact]
    public async Task InvalidSignature_IsRejected()
    {
        var (verifier, _) = CreateVerifier();
        // Assina com uma OUTRA chave (não confiável).
        using var rogue = RSA.Create(2048);
        var content = Encoding.UTF8.GetBytes("pacote");
        var hash = SHA256.HashData(content);
        var badSig = rogue.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var metadata = new UpdatePackageMetadata(
            "2.0.0",
            Convert.ToHexString(hash),
            Convert.ToBase64String(badSig));

        var host = new FakeHost(healthy: true);
        var applier = new UpdateApplier(verifier, host, NullLogger<UpdateApplier>.Instance);

        var outcome = await applier.TryApplyAsync(content, metadata, CancellationToken.None);

        Assert.Equal(UpdateOutcome.Rejected, outcome);
    }

    [Fact]
    public async Task UnhealthyAfterApply_RollsBack()
    {
        var (verifier, sign) = CreateVerifier();
        var content = Encoding.UTF8.GetBytes("pacote");
        var metadata = SignedMetadata(content, sign, "2.0.0");

        var host = new FakeHost(healthy: false);
        var applier = new UpdateApplier(verifier, host, NullLogger<UpdateApplier>.Instance);

        var outcome = await applier.TryApplyAsync(content, metadata, CancellationToken.None);

        Assert.Equal(UpdateOutcome.RolledBack, outcome);
        Assert.True(host.RolledBack);
    }

    private static (UpdatePackageVerifier Verifier, RSA Signer) CreateVerifier()
    {
        var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        return (new UpdatePackageVerifier(publicKey), rsa);
    }

    private static UpdatePackageMetadata SignedMetadata(byte[] content, RSA signer, string version)
    {
        var hash = SHA256.HashData(content);
        var sig = signer.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new UpdatePackageMetadata(version, Convert.ToHexString(hash), Convert.ToBase64String(sig));
    }

    private sealed class FakeHost(bool healthy) : IUpdateHost
    {
        public bool Applied { get; private set; }

        public bool RolledBack { get; private set; }

        public Task BackupCurrentAsync(CancellationToken ct) => Task.CompletedTask;

        public Task ApplyAsync(byte[] packageContent, string version, CancellationToken ct)
        {
            Applied = true;
            return Task.CompletedTask;
        }

        public Task<bool> RestartAndHealthCheckAsync(CancellationToken ct) => Task.FromResult(healthy);

        public Task RollbackAsync(CancellationToken ct)
        {
            RolledBack = true;
            return Task.CompletedTask;
        }
    }
}
