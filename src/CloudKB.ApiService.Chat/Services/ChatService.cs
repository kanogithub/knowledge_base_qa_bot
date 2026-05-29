using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudKB.Infrastructure;
using CloudKB.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace CloudKB.ApiService.Chat.Services;

public interface IChatService
{
    Task StreamAnswerAsync(string tenantId, string query, HttpResponse response, CancellationToken cancellationToken);
}

public class ChatService : IChatService
{
    private readonly RedisIndexLoader _indexLoader;
    private readonly CloudKbDbContext _dbContext;
    private readonly IChatClient _chatClient;
    private readonly double _threshold;
    private readonly int _topK;
    private readonly double _k1;
    private readonly double _b;
    private readonly double _headingBoost;

    public ChatService(
        RedisIndexLoader indexLoader,
        CloudKbDbContext dbContext,
        IChatClient chatClient,
        IConfiguration configuration)
    {
        _indexLoader = indexLoader;
        _dbContext = dbContext;
        _chatClient = chatClient;

        _k1 = double.TryParse(configuration["BM25:K1"], out var valK1) ? valK1 : 1.2;
        _b = double.TryParse(configuration["BM25:B"], out var valB) ? valB : 0.75;
        _headingBoost = double.TryParse(configuration["BM25:HeadingBoost"], out var valBoost) ? valBoost : 1.5;
        _threshold = double.TryParse(configuration["BM25:RetrievalScoreThreshold"], out var valThresh) ? valThresh : 0.5;
        _topK = int.TryParse(configuration["BM25:TopK"], out var valK) ? valK : 3;
    }

    public async Task StreamAnswerAsync(string tenantId, string query, HttpResponse response, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Load TenantKbIndex from Redis
            var index = await _indexLoader.LoadAsync(tenantId);
            if (index == null)
            {
                var chunk = new ChatStreamChunk("knowledge base has not been indexed", true, null);
                await SseWriter.WriteDataAsync(response, chunk);
                await SseWriter.FlushAsync(response);
                return;
            }

            // 2 & 3. Tokenise and score sections
            var engine = new Bm25Engine(new Bm25Options(_k1, _b, _headingBoost, _threshold, _topK));
            var scored = engine.Score(query, index);

            // 4. Check early exit
            if (scored.Count == 0 || !scored.Any(s => s.Score >= _threshold))
            {
                var chunk = new ChatStreamChunk("我無法從現有的知識庫中確認此訊息。", true, null);
                await SseWriter.WriteDataAsync(response, chunk);
                await SseWriter.FlushAsync(response);
                return;
            }

            // 5. Fetch top-K section content from PostgreSQL
            var sectionIds = scored.Select(s => s.SectionId).ToList();
            var sectionsContent = await _dbContext.TenantSections
                .Where(s => sectionIds.Contains(s.Id) && s.TenantId == tenantId)
                .ToListAsync(cancellationToken);

            // Map content back to scores and make citations
            var citations = new List<SourceCitation>();
            var topKSections = new List<TenantSection>();

            foreach (var ss in scored)
            {
                var section = sectionsContent.FirstOrDefault(sc => sc.Id == ss.SectionId);
                if (section != null)
                {
                    topKSections.Add(section);
                    citations.Add(new SourceCitation(
                        SectionId: section.Id,
                        FileName: section.FileName,
                        Heading: section.Heading,
                        HeadingPath: section.HeadingPath,
                        Score: ss.Score
                    ));
                }
            }

            if (topKSections.Count == 0)
            {
                var chunk = new ChatStreamChunk("我無法從現有的知識庫中確認此訊息。", true, null);
                await SseWriter.WriteDataAsync(response, chunk);
                await SseWriter.FlushAsync(response);
                return;
            }

            // 6. Write SSE source citation frame first
            var firstChunk = new ChatStreamChunk("", false, citations);
            await SseWriter.WriteDataAsync(response, firstChunk);
            await SseWriter.FlushAsync(response);

            // 7. Build grounded system prompt
            var sb = new StringBuilder();
            sb.AppendLine("You are a knowledge base assistant. Answer the user's question ONLY using the source sections provided below.");
            sb.AppendLine("If the sources do not contain the answer, say you cannot confirm.");
            sb.AppendLine("Cite your sources using the format: [filename#heading].");
            sb.AppendLine();
            sb.AppendLine("--- SOURCES ---");
            foreach (var sec in topKSections)
            {
                sb.AppendLine($"### {sec.FileName}#{sec.Heading}");
                sb.AppendLine(sec.Content);
                sb.AppendLine();
            }
            sb.AppendLine("--- END SOURCES ---");

            var systemPrompt = sb.ToString();

            // 8. Call LLM
            var chatMessages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, query)
            };

            try
            {
                await foreach (var update in _chatClient.GetStreamingResponseAsync(chatMessages, null, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var text = update.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        var tokenChunk = new ChatStreamChunk(text, false, null);
                        await SseWriter.WriteDataAsync(response, tokenChunk);
                        await SseWriter.FlushAsync(response);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"LLM Error: {ex.Message}");
            }

            // 9. Write terminal SSE frame
            var finalChunk = new ChatStreamChunk("", true, citations);
            await SseWriter.WriteDataAsync(response, finalChunk);
            await SseWriter.FlushAsync(response);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[ChatService] Chat stream connection cancelled by client for tenant {tenantId}");
        }
    }
}
