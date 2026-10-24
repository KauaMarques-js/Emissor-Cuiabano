using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Infrastructure;

/// <summary>
/// Importa certificados A1 (.pfx/.p12). O arquivo e a senha ficam protegidos pela Data Protection API
/// (chaves fora do banco, protegidas por DPAPI no Windows). A senha nunca é registrada em log.
/// </summary>
public sealed partial class CertificateStore(AppDbContext db, IDataProtectionProvider dataProtection, DataPaths paths,
    FiscalOptions options, TimeProvider clock) : ICertificateStore
{
    private const string IcpBrasilCnpjOid = "2.16.76.1.3.3";
    private const int MaxFileSize = 1024 * 1024;
    private readonly IDataProtector protector = dataProtection.CreateProtector("FiscalPlatform.Certificates.v1");

    public async Task<CertificateImportResult> ImportAsync(int companyId, Stream pfx, string password, string user, CancellationToken ct = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return new(false, "Empresa não encontrada.", null);

        using var buffer = new MemoryStream();
        await pfx.CopyToAsync(buffer, ct);
        if (buffer.Length == 0 || buffer.Length > MaxFileSize) return new(false, "Arquivo vazio ou grande demais para um certificado A1.", null);
        var bytes = buffer.ToArray();

        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadPkcs12(bytes, password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            return new(false, "Não foi possível abrir o certificado: arquivo inválido ou senha incorreta.", null);
        }

        using (cert)
        {
            var now = clock.GetLocalNow().DateTime;
            if (!cert.HasPrivateKey) return new(false, "O arquivo não contém a chave privada (não é um A1 utilizável).", null);
            if (now > cert.NotAfter) return new(false, $"Certificado expirado em {cert.NotAfter:dd/MM/yyyy}.", null);
            if (now < cert.NotBefore) return new(false, $"Certificado ainda não é válido (início em {cert.NotBefore:dd/MM/yyyy}).", null);

            var notes = new List<string>();
            var certCnpj = ExtractCnpj(cert);
            var matches = certCnpj == company.Cnpj;
            if (!matches && certCnpj is not null && certCnpj[..8] == company.Cnpj[..8])
            {
                matches = true;
                notes.Add("Certificado de outro estabelecimento com a mesma raiz de CNPJ; confirme se é aceito para esta operação.");
            }
            if (certCnpj is null) notes.Add("CNPJ não encontrado no certificado.");
            else if (!matches) notes.Add($"CNPJ do certificado ({DocumentNumbers.Format(certCnpj)}) não corresponde ao da empresa.");

            using (var chain = new X509Chain())
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                if (!chain.Build(cert))
                    notes.Add("Cadeia de certificação não pôde ser validada neste computador: " +
                              string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()).Distinct()));
            }

            var fileName = $"{companyId}-{Guid.NewGuid():N}.pfx.protegido";
            await File.WriteAllBytesAsync(Path.Combine(paths.CertificatesDirectory, fileName), protector.Protect(bytes), ct);

            // Substitui o certificado anterior mantendo apenas o histórico de metadados.
            var previous = await db.Certificates.Where(c => c.CompanyId == companyId && c.IsActive).ToListAsync(ct);
            foreach (var old in previous)
            {
                old.IsActive = false;
                old.ReplacedAt = now;
                DeleteFile(old.StoredFileName);
                old.StoredFileName = null;
                old.ProtectedPassword = null;
            }

            var meta = new CertificateMetadata
            {
                CompanyId = companyId,
                Subject = cert.Subject,
                Issuer = cert.Issuer,
                Thumbprint = cert.Thumbprint,
                CertificateCnpj = certCnpj,
                CnpjMatches = matches,
                NotBefore = cert.NotBefore,
                NotAfter = cert.NotAfter,
                ImportedAt = now,
                ImportedBy = user,
                IsActive = true,
                StoredFileName = fileName,
                ProtectedPassword = protector.Protect(password),
                ValidationNotes = notes.Count > 0 ? string.Join(Environment.NewLine, notes) : null,
            };
            db.Certificates.Add(meta);
            Audit.Log(db, now, companyId, user, "Importar certificado", "Certificado", cert.Thumbprint,
                $"Validade até {cert.NotAfter:dd/MM/yyyy}; CNPJ confere: {(matches ? "sim" : "não")}");
            await db.SaveChangesAsync(ct);

            var status = meta.StatusAt(now, options.CertificateWarningDays);
            var message = status == CertificateStatus.Invalido
                ? "Certificado importado, mas NÃO poderá ser usado para esta empresa: " + string.Join(" ", notes)
                : "Certificado importado com sucesso." + (notes.Count > 0 ? " Avisos: " + string.Join(" ", notes) : "");
            return new(true, message, meta);
        }
    }

    public async Task<X509Certificate2?> LoadActiveAsync(int companyId, CancellationToken ct = default)
    {
        var meta = await db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.IsActive, ct);
        if (meta?.StoredFileName is null || meta.ProtectedPassword is null) return null;
        var path = Path.Combine(paths.CertificatesDirectory, meta.StoredFileName);
        if (!File.Exists(path)) return null;
        var bytes = protector.Unprotect(await File.ReadAllBytesAsync(path, ct));
        return X509CertificateLoader.LoadPkcs12(bytes, protector.Unprotect(meta.ProtectedPassword),
            // No Windows, o TLS (SChannel) não aceita chaves efêmeras como certificado de cliente.
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
    }

    private void DeleteFile(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        var path = Path.Combine(paths.CertificatesDirectory, fileName);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>e-CNPJ ICP-Brasil: OID 2.16.76.1.3.3 no SubjectAltName; fallback para o CN ("NOME:CNPJ").</summary>
    public static string? ExtractCnpj(X509Certificate2 cert)
    {
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value != "2.5.29.17") continue;
            try
            {
                var names = new AsnReader(ext.RawData, AsnEncodingRules.DER).ReadSequence();
                while (names.HasData)
                {
                    var tag = names.PeekTag();
                    if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 0)
                    {
                        var otherName = names.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                        var oid = otherName.ReadObjectIdentifier();
                        var value = otherName.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)).ReadEncodedValue();
                        if (oid == IcpBrasilCnpjOid)
                        {
                            var digits = new string(value.ToArray().Where(b => b is >= (byte)'0' and <= (byte)'9').Select(b => (char)b).ToArray());
                            if (digits.Length >= 14) return digits[..14];
                        }
                    }
                    else names.ReadEncodedValue();
                }
            }
            catch (AsnContentException) { }
        }
        var match = CnpjInSubject().Match(cert.Subject);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"(?<!\d)(\d{14})(?!\d)")]
    private static partial Regex CnpjInSubject();
}
