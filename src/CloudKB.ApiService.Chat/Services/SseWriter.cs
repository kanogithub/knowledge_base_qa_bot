using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CloudKB.ApiService.Chat.Services;

public static class SseWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };


    public static async Task WriteDataAsync(HttpResponse response, object payload)
    {
        var json = JsonSerializer.Serialize(payload, Options);
        await response.WriteAsync($"data: {json}\n\n");
    }

    public static async Task FlushAsync(HttpResponse response)
    {
        await response.Body.FlushAsync();
    }
}
