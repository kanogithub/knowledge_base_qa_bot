using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CloudKB.Infrastructure;

public interface IStorageService
{
    Task UploadAsync(string tenantId, string fileName, Stream fileStream, CancellationToken ct);
    Task<string> DownloadAsync(string tenantId, string fileName, CancellationToken ct);
}
