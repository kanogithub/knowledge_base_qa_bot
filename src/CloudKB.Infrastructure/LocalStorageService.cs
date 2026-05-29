using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace CloudKB.Infrastructure;

public class LocalStorageService : IStorageService
{
    private readonly string _storagePath;

    public LocalStorageService(IConfiguration config)
    {
        var configuredPath = config["Storage:LocalPath"];
        if (string.IsNullOrEmpty(configuredPath))
        {
            _storagePath = Path.Combine(AppContext.BaseDirectory, "LocalStorage");
        }
        else
        {
            _storagePath = configuredPath;
        }

        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
        Console.WriteLine($"Initialized LocalStorageService at: {_storagePath}");
    }

    public async Task UploadAsync(string tenantId, string fileName, Stream fileStream, CancellationToken ct)
    {
        var tenantDir = Path.Combine(_storagePath, tenantId);
        if (!Directory.Exists(tenantDir))
        {
            Directory.CreateDirectory(tenantDir);
        }

        var filePath = Path.Combine(tenantDir, fileName);
        using var destStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await fileStream.CopyToAsync(destStream, ct);
    }

    public async Task<string> DownloadAsync(string tenantId, string fileName, CancellationToken ct)
    {
        var filePath = Path.Combine(_storagePath, tenantId, fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found in local storage: {filePath}");
        }

        return await File.ReadAllTextAsync(filePath, ct);
    }

    public Task DeleteAsync(string tenantId, string fileName, CancellationToken ct)
    {
        var filePath = Path.Combine(_storagePath, tenantId, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }
}
