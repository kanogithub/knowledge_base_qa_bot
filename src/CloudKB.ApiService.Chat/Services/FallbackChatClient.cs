using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace CloudKB.ApiService.Chat.Services;

public class FallbackChatClient : IChatClient
{
    public ChatOptions? DefaultOptions { get; set; }
    public ChatClientMetadata Metadata { get; } = new("fallback-llm", new Uri("http://localhost"));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages, 
        ChatOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "This is a simulated response (fallback).")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages, 
        ChatOptions? options = null, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tokens = new[] { "This", " is", " a", " simulated", " response", " (fallback)." };
        foreach (var token in tokens)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = new List<AIContent> { new TextContent(token) }
            };
            await Task.Delay(50, cancellationToken);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() {}
}
