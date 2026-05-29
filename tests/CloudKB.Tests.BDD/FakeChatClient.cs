using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace CloudKB.Tests.BDD;

public class FakeChatClient : IChatClient
{
    public ChatOptions? DefaultOptions { get; set; }
    public ChatClientMetadata Metadata { get; } = new("fake-llm-service", new Uri("http://fake-llm"));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages, 
        ChatOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        var responseText = "This is a simulated response from the Grounded AI Knowledge Base.";
        
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
        {
            ModelId = "fake-model"
        };
        
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages, 
        ChatOptions? options = null, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tokens = new[] { "This", " is", " a", " simulated", " response", " from", " the", " Grounded", " AI", " Knowledge", " Base." };

        foreach (var token in tokens)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            // Simulate slight delay in streaming
            await Task.Delay(20, cancellationToken);

            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = new List<AIContent> { new TextContent(token) },
                ModelId = "fake-model"
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ChatClientMetadata))
        {
            return Metadata;
        }
        return null;
    }

    public void Dispose()
    {
        // No-op
    }
}
