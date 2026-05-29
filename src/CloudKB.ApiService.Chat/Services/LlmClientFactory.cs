using System;
using System.Collections.Generic;
using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace CloudKB.ApiService.Chat.Services;

public class LlmClientFactory
{
    private readonly IConfiguration _config;

    public LlmClientFactory(IConfiguration config)
    {
        _config = config;
    }

    public IChatClient CreateClient()
    {
        var priorityList = _config.GetSection("LlmProviders:Priority").Get<List<string>>() ?? new List<string>();

        foreach (var providerName in priorityList)
        {
            if (string.Equals(providerName, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                var apiKey = _config["LlmProviders:Gemini:ApiKey"];
                if (!string.IsNullOrEmpty(apiKey))
                {
                    var modelName = _config["LlmProviders:Gemini:ModelName"] ?? "gemini-2.5-flash";
                    var endpoint = _config["LlmProviders:Gemini:Endpoint"] ?? "https://generativelanguage.googleapis.com/v1beta/openai/";
                    
                    var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
                    var credential = new ApiKeyCredential(apiKey);
                    return new OpenAI.Chat.ChatClient(modelName, credential, options).AsIChatClient();
                }
            }
            else if (string.Equals(providerName, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                var apiKey = _config["LlmProviders:OpenAI:ApiKey"];
                if (!string.IsNullOrEmpty(apiKey))
                {
                    var modelName = _config["LlmProviders:OpenAI:ModelName"] ?? "gpt-4o-mini";
                    var endpoint = _config["LlmProviders:OpenAI:Endpoint"];
                    
                    if (!string.IsNullOrEmpty(endpoint))
                    {
                        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
                        var credential = new ApiKeyCredential(apiKey);
                        return new OpenAI.Chat.ChatClient(modelName, credential, options).AsIChatClient();
                    }
                    else
                    {
                        return new OpenAI.Chat.ChatClient(modelName, apiKey).AsIChatClient();
                    }
                }
            }
        }

        // Default fallback mock client if no configured providers have ApiKeys
        return new FallbackChatClient();
    }
}
