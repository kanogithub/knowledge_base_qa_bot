using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudKB.SharedKernel;

public class Bm25Engine
{
    private readonly Bm25Options _options;

    public Bm25Engine(Bm25Options options)
    {
        _options = options;
    }

    public IReadOnlyList<ScoredSection> Score(string query, TenantKbIndex index)
    {
        if (string.IsNullOrWhiteSpace(query) || index == null || index.TotalDocuments == 0)
        {
            return Array.Empty<ScoredSection>();
        }

        var queryTokens = Tokeniser.Tokenise(query);
        if (queryTokens.Count == 0)
        {
            return Array.Empty<ScoredSection>();
        }

        var N = index.TotalDocuments;
        var avgdl = index.AverageDocumentLength;
        if (avgdl <= 0) avgdl = 1.0;

        var scores = new List<ScoredSection>();

        // Calculate IDF for each unique query token
        var uniqueQueryTokens = queryTokens.Distinct().ToList();
        var idfMap = new Dictionary<string, double>();

        foreach (var q in uniqueQueryTokens)
        {
            // n(q) is the number of documents containing token q
            var n = index.Sections.Count(s => s.TermFrequencies.ContainsKey(q));
            
            // Standard BM25 IDF with smoothing
            var idf = Math.Log(1.0 + (N - n + 0.5) / (n + 0.5));
            // Prevent negative IDF
            if (idf < 0) idf = 0.0001; 

            idfMap[q] = idf;
        }

        foreach (var section in index.Sections)
        {
            var docScore = 0.0;
            var docLength = section.TokenCount;

            // Prepare heading tokens for Heading Boost comparison
            var headingTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(section.Heading))
            {
                foreach (var t in Tokeniser.Tokenise(section.Heading))
                {
                    headingTokens.Add(t);
                }
            }
            if (section.HeadingPath != null)
            {
                foreach (var pathNode in section.HeadingPath)
                {
                    foreach (var t in Tokeniser.Tokenise(pathNode))
                    {
                        headingTokens.Add(t);
                    }
                }
            }

            foreach (var q in queryTokens)
            {
                if (!idfMap.TryGetValue(q, out var idf))
                {
                    continue;
                }

                // f(q, D)
                section.TermFrequencies.TryGetValue(q, out var tf);

                var numerator = tf * (_options.K1 + 1);
                var denominator = tf + _options.K1 * (1.0 - _options.B + _options.B * (docLength / avgdl));

                var termScore = idf * (numerator / denominator);

                // Apply Heading Boost if the token is present in the heading or heading path
                if (headingTokens.Contains(q))
                {
                    termScore *= _options.HeadingBoost;
                }

                docScore += termScore;
            }

            if (docScore >= _options.RetrievalScoreThreshold)
            {
                scores.Add(new ScoredSection(section.SectionId, docScore));
            }
        }

        return scores
            .OrderByDescending(s => s.Score)
            .Take(_options.TopK)
            .ToList();
    }
}
