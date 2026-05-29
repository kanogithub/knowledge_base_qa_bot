using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using CloudKB.SharedKernel;

namespace CloudKB.Tests.BDD;

public class CoreTests
{
    [Fact]
    public void Tokeniser_ShouldNormaliseAndFilterStopwords()
    {
        // Act
        var tokens = Tokeniser.Tokenise("I want you to be happy with a refund.");

        // Assert
        Assert.Equal(new[] { "want", "happy", "refund" }, tokens);
    }

    [Fact]
    public void MarkdownParser_ShouldSplitSectionsCorrectly()
    {
        // Arrange
        var markdown = @"# Welcome to Cloud-KB
This is introduction text.

## Subsection A
More details here.

### Nested Leaf
Deep content.";

        // Act
        var sections = MarkdownParser.Parse("tenant-01", "faq.md", markdown);

        // Assert
        Assert.Equal(3, sections.Count);

        Assert.Equal("tenant-01#faq.md#welcome-to-cloud-kb", sections[0].SectionId);
        Assert.Equal("Welcome to Cloud-KB", sections[0].Heading);
        Assert.Equal(new[] { "Welcome to Cloud-KB" }, sections[0].HeadingPath);
        Assert.Contains("This is introduction text.", sections[0].Content);

        Assert.Equal("tenant-01#faq.md#subsection-a", sections[1].SectionId);
        Assert.Equal("Subsection A", sections[1].Heading);
        Assert.Equal(new[] { "Welcome to Cloud-KB", "Subsection A" }, sections[1].HeadingPath);
        Assert.Contains("More details here.", sections[1].Content);

        Assert.Equal("tenant-01#faq.md#nested-leaf", sections[2].SectionId);
        Assert.Equal("Nested Leaf", sections[2].Heading);
        Assert.Equal(new[] { "Welcome to Cloud-KB", "Subsection A", "Nested Leaf" }, sections[2].HeadingPath);
        Assert.Contains("Deep content.", sections[2].Content);
    }

    [Fact]
    public void Bm25Engine_ShouldScoreCorrectlyAndApplyHeadingBoost()
    {
        // Arrange
        var options = new Bm25Options(
            K1: 1.2,
            B: 0.75,
            HeadingBoost: 2.0,
            RetrievalScoreThreshold: 0.1,
            TopK: 3
        );
        var engine = new Bm25Engine(options);

        var section1 = new IndexedSectionMeta(
            SectionId: "sec-01",
            FileName: "test.md",
            Heading: "Refund Policy",
            HeadingPath: new List<string> { "Refund Policy" },
            TokenCount: 5,
            TermFrequencies: new Dictionary<string, int> { { "refund", 1 }, { "policy", 1 } }
        );

        var section2 = new IndexedSectionMeta(
            SectionId: "sec-02",
            FileName: "test.md",
            Heading: "Shipping Details",
            HeadingPath: new List<string> { "Shipping Details" },
            TokenCount: 10,
            TermFrequencies: new Dictionary<string, int> { { "shipping", 2 }, { "refund", 1 } }
        );

        var index = new TenantKbIndex(
            TenantId: "tenant-01",
            TotalDocuments: 2,
            AverageDocumentLength: 7.5,
            LastUpdatedAt: DateTime.UtcNow,
            Sections: new List<IndexedSectionMeta> { section1, section2 }
        );

        // Act - Query "refund" which is in both, but boosted in section1 heading
        var results = engine.Score("refund", index);

        // Assert
        Assert.NotEmpty(results);
        Assert.Equal("sec-01", results[0].SectionId); // Section 1 should be first due to heading boost and shorter length
    }
}

