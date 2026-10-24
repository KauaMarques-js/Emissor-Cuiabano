using System.IO.Compression;
using System.Security.Cryptography;
using FiscalPlatform.Application;
using Microsoft.EntityFrameworkCore;

namespace FiscalPlatform.Infrastructure;

/// <summary>Diretório de dados fora da pasta pública da aplicação (banco, chaves, certificados, XMLs, backups).</summary>
public sealed class DataPaths
{
    public DataPaths(string root)
    {
        Root = Path.GetFullPath(root);
        foreach (var dir in new[] { Root, KeysDirectory, CertificatesDirectory, FilesDirectory, BackupsDirectory })
            Directory.CreateDirectory(dir);
    }

    public string Root { get; }
    public string DatabasePath => Path.Combine(Root, "emissor.db");
    public string KeysDirectory => Path.Combine(Root, "keys");
    public string CertificatesDirectory => Path.Combine(Root, "certificados");
    public string FilesDirectory => Path.Combine(Root, "arquivos-fiscais");
    public string BackupsDirectory => Path.Combine(Root, "backups");
}

public sealed class FileStore(DataPaths paths) : IFiscalFileStore
{
    public async Task<string> SaveAsync(int companyId, int documentId, string fileName, string content, CancellationToken ct = default)
    {
        var safeName = string.Concat(fileName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));
        var relative = Path.Combine(companyId.ToString(), documentId.ToString(), safeName);
        var full = Resolve(relative)!;
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, ct);
        return relative.Replace('\\', '/');
    }

    public Stream? Open(string relativePath)
    {
        var full = Resolve(relativePath);
        return full is not null && File.Exists(full) ? File.OpenRead(full) : null;
    }

    private string? Resolve(string relative)
    {
        var root = Path.GetFullPath(paths.FilesDirectory) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

public sealed class PasswordHasher
{
    private const int Iterations = 100_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1") return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, int.Parse(parts[1]), HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>Backup do banco (cópia consistente via VACUUM INTO) e dos arquivos fiscais em um .zip.</summary>
public sealed class BackupService(AppDbContext db, DataPaths paths)
{
    public async Task<string> CreateAsync()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var tempDb = Path.Combine(paths.BackupsDirectory, $"tmp-{stamp}.db");
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", tempDb);
        var zipPath = Path.Combine(paths.BackupsDirectory, $"backup-{stamp}.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(tempDb, "emissor.db");
            foreach (var file in Directory.EnumerateFiles(paths.FilesDirectory, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file, "arquivos-fiscais/" + Path.GetRelativePath(paths.FilesDirectory, file).Replace('\\', '/'));
        }
        File.Delete(tempDb);
        return Path.GetFileName(zipPath);
    }

    public IEnumerable<FileInfo> List() =>
        new DirectoryInfo(paths.BackupsDirectory).GetFiles("backup-*.zip").OrderByDescending(f => f.Name);

    public string? PathFor(string name)
    {
        var file = List().FirstOrDefault(f => f.Name == name);
        return file?.FullName;
    }
}
