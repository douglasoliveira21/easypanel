using System.Security.Cryptography;

namespace EasyPanel.WindowsClient;

/// <summary>Metadados de um pacote de atualização recebidos do backend (R16.1).</summary>
/// <param name="Version">Versão do pacote (SemVer).</param>
/// <param name="Sha256">Hash SHA-256 esperado (hex ou base64).</param>
/// <param name="Signature">Assinatura do hash (base64), verificável com a chave pública embutida.</param>
public sealed record UpdatePackageMetadata(string Version, string Sha256, string Signature);

/// <summary>
/// Verifica a integridade e a autenticidade de um pacote de atualização antes de
/// aplicá-lo (R16.2/R16.3): confere o hash SHA-256 do conteúdo contra o esperado e
/// valida a assinatura do hash com a chave pública embutida no agente (RSA). Um
/// pacote com hash divergente ou assinatura inválida é recusado, mantendo a versão
/// atual (R16.4).
/// </summary>
public sealed class UpdatePackageVerifier
{
    private readonly RSA _publicKey;

    /// <summary>
    /// Cria o verificador com a chave pública (formato SubjectPublicKeyInfo, DER)
    /// usada para validar a assinatura dos pacotes.
    /// </summary>
    public UpdatePackageVerifier(byte[] publicKeyDer)
    {
        ArgumentNullException.ThrowIfNull(publicKeyDer);
        _publicKey = RSA.Create();
        _publicKey.ImportSubjectPublicKeyInfo(publicKeyDer, out _);
    }

    /// <summary>
    /// Verifica o conteúdo do pacote contra os metadados. Retorna <c>true</c> apenas
    /// se o hash confere <b>e</b> a assinatura é válida (R16.3). Qualquer divergência
    /// resulta em recusa (<c>false</c>), sem lançar.
    /// </summary>
    public bool Verify(byte[] packageContent, UpdatePackageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(packageContent);
        ArgumentNullException.ThrowIfNull(metadata);

        var actualHash = SHA256.HashData(packageContent);

        if (!TryDecode(metadata.Sha256, out var expectedHash)
            || !CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            return false;
        }

        if (!TryDecode(metadata.Signature, out var signature))
        {
            return false;
        }

        // A assinatura cobre o hash do conteúdo (assinatura de digest), verificada
        // com PKCS#1 v1.5 sobre SHA-256.
        try
        {
            return _publicKey.VerifyHash(
                actualHash,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Decodifica um valor hex ou base64 em bytes; falha silenciosa → <c>false</c>.</summary>
    private static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Tenta hex primeiro (comprimento par de dígitos hex), depois base64.
        try
        {
            if (value.Length % 2 == 0 && value.All(Uri.IsHexDigit))
            {
                bytes = Convert.FromHexString(value);
                return true;
            }
        }
        catch (FormatException)
        {
            // cai para base64
        }

        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
