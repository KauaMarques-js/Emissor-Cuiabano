using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FiscalPlatform.Application;
using FiscalPlatform.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.UnitTests;

public sealed class CertificateStoreTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly AppDbContext db;
    private readonly DataPaths paths = new(Path.Combine(Path.GetTempPath(), "emissor-tests-" + Guid.NewGuid().ToString("N")));
    private readonly CertificateStore store;
    private readonly int companyId;

    public CertificateStoreTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
        var company = TestData.Company();
        company.Id = 0;
        db.Companies.Add(company);
        db.SaveChanges();
        companyId = company.Id;
        store = new CertificateStore(db, new EphemeralDataProtectionProvider(), paths, new FiscalOptions(), TimeProvider.System);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
        Directory.Delete(paths.Root, true);
    }

    private static byte[] CreatePfx(string cn, string? sanCnpj, string password, DateTimeOffset? notAfter = null)
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (sanCnpj is not null)
        {
            // otherName ICP-Brasil 2.16.76.1.3.3 com o CNPJ
            var w = new AsnWriter(AsnEncodingRules.DER);
            using (w.PushSequence())
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
            {
                w.WriteObjectIdentifier("2.16.76.1.3.3");
                using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                    w.WriteOctetString(System.Text.Encoding.ASCII.GetBytes(sanCnpj));
            }
            req.CertificateExtensions.Add(new X509Extension("2.5.29.17", w.Encode(), false));
        }
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), notAfter ?? DateTimeOffset.Now.AddYears(1));
        return cert.Export(X509ContentType.Pfx, password);
    }

    [Fact]
    public async Task Imports_matching_certificate_and_protects_file()
    {
        var pfx = CreatePfx("EMPRESA TESTE", "11222333000181", "segredo123");
        var result = await store.ImportAsync(companyId, new MemoryStream(pfx), "segredo123", "teste");

        Assert.True(result.Success, result.Message);
        Assert.True(result.Certificate!.CnpjMatches);
        Assert.Equal("11222333000181", result.Certificate.CertificateCnpj);
        Assert.NotEqual("segredo123", result.Certificate.ProtectedPassword);
        var stored = await File.ReadAllBytesAsync(Path.Combine(paths.CertificatesDirectory, result.Certificate.StoredFileName!));
        Assert.NotEqual(pfx, stored);

        using var loaded = await store.LoadActiveAsync(companyId);
        Assert.NotNull(loaded);
        Assert.True(loaded!.HasPrivateKey);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var pfx = CreatePfx("EMPRESA TESTE:11222333000181", null, "certo");
        var result = await store.ImportAsync(companyId, new MemoryStream(pfx), "errado", "teste");
        Assert.False(result.Success);
        Assert.Contains("senha incorreta", result.Message);
    }

    [Fact]
    public async Task Certificate_of_other_cnpj_is_flagged_and_replacement_removes_old_file()
    {
        var other = await store.ImportAsync(companyId, new MemoryStream(CreatePfx("OUTRA:99888777000166", null, "x")), "x", "teste");
        Assert.True(other.Success);
        Assert.False(other.Certificate!.CnpjMatches);
        var oldFile = Path.Combine(paths.CertificatesDirectory, other.Certificate.StoredFileName!);

        var good = await store.ImportAsync(companyId, new MemoryStream(CreatePfx("EMPRESA TESTE:11222333000181", null, "y")), "y", "teste");
        Assert.True(good.Certificate!.CnpjMatches);
        Assert.False(File.Exists(oldFile));
        Assert.Single(db.Certificates.Where(c => c.CompanyId == companyId && c.IsActive));
        Assert.Equal(2, db.Certificates.Count());
    }

    [Fact]
    public async Task Expired_certificate_is_not_imported()
    {
        var pfx = CreatePfx("EMPRESA TESTE:11222333000181", null, "x", DateTimeOffset.Now.AddHours(-1));
        var result = await store.ImportAsync(companyId, new MemoryStream(pfx), "x", "teste");
        Assert.False(result.Success);
        Assert.Contains("expirado", result.Message);
    }
}
