using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CloudKB.ApiService.Notification.Services;

public interface INotificationStreamService
{
    Task StreamEventsAsync(string tenantId, HttpResponse response, CancellationToken ct);
}
