using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CloudKB.SharedKernel;

public static class MarkdownParser
{
    private static readonly Regex SlugRegex = new Regex(@"[^a-z0-9\-]", RegexOptions.Compiled);

    public static IReadOnlyList<ParsedSection> Parse(string tenantId, string fileName, string markdownContent)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            return Array.Empty<ParsedSection>();
        }

        var lines = markdownContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sections = new List<ParsedSection>();
        
        var currentHeadingPath = new List<string>();
        var currentContentBuilder = new StringBuilder();
        
        string currentHeading = "Document Start";
        var activeHeadingLevel = 0;

        void CommitCurrentSection()
        {
            var content = currentContentBuilder.ToString().Trim();
            // We only save sections that have content or are not just empty starts
            if (!string.IsNullOrEmpty(content) || currentHeading != "Document Start")
            {
                var slug = Slugify(currentHeading);
                var sectionId = $"{tenantId}#{fileName}#{slug}";
                var tokens = Tokeniser.Tokenise(content);

                sections.Add(new ParsedSection(
                    SectionId: sectionId,
                    TenantId: tenantId,
                    FileName: fileName,
                    Heading: currentHeading,
                    HeadingPath: new List<string>(currentHeadingPath),
                    Content: content,
                    Tokens: tokens,
                    TokenCount: tokens.Count
                ));
            }
            currentContentBuilder.Clear();
        }

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith('#'))
            {
                // Count heading level
                var level = 0;
                while (level < trimmedLine.Length && trimmedLine[level] == '#')
                {
                    level++;
                }

                // Check if it's a valid heading structure (e.g. ## Text)
                if (level > 0 && level < trimmedLine.Length && char.IsWhiteSpace(trimmedLine[level]))
                {
                    var headingText = trimmedLine.Substring(level).Trim();

                    // Commit the section we were previously accumulating
                    CommitCurrentSection();

                    // Update heading path stack based on level
                    if (level <= activeHeadingLevel)
                    {
                        // Pop path back to parent level
                        var itemsToRemove = activeHeadingLevel - level + 1;
                        for (int i = 0; i < itemsToRemove && currentHeadingPath.Count > 0; i++)
                        {
                            currentHeadingPath.RemoveAt(currentHeadingPath.Count - 1);
                        }
                    }

                    currentHeadingPath.Add(headingText);
                    currentHeading = headingText;
                    activeHeadingLevel = level;

                    continue;
                }
            }

            // Append regular text line
            currentContentBuilder.AppendLine(line);
        }

        // Commit final section
        CommitCurrentSection();

        return sections;
    }

    public static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "section";
        
        var lowered = text.ToLowerInvariant().Replace(' ', '-');
        var cleaned = SlugRegex.Replace(lowered, "");
        cleaned = cleaned.Trim('-');
        
        return string.IsNullOrEmpty(cleaned) ? "section" : cleaned;
    }
}

public record ParsedSection(
    string SectionId,
    string TenantId,
    string FileName,
    string Heading,
    List<string> HeadingPath,
    string Content,
    IReadOnlyList<string> Tokens,
    int TokenCount
);
