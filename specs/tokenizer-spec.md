# Cloud-KB — Tokeniser and Text Normalisation Specification

This specification defines the deterministic tokenisation and text normalisation pipeline for the Cloud-KB system. 

To ensure BM25 scoring consistency across the background compilation worker, the in-memory chat retrieval engine, and BDD integration tests, **all components MUST implement this exact text normalisation pipeline.**

---

## 1. Normalisation Pipeline Steps

The text input (either a Markdown section content or a user chat query) must pass through the following 4 stages in sequence:

```
[ Raw Text ] 
     │
     ▼
 1. Lowercasing ────────► Convert all text to lowercase
     │
     ▼
 2. Punctuation Strip ──► Remove non-alphanumeric characters, replace with space
     │
     ▼
 3. Word Splitting ─────► Split text by whitespace into raw tokens
     │
     ▼
 4. Stopword Filtering ──► Remove tokens in the official stopword list
     │
     ▼
[ Normalised Tokens ]
```

---

## 2. Technical Requirements per Stage

### Stage 1: Lowercasing
- Convert the entire string to lowercase using culture-invariant rules.
- **C# Implementation:** `text.ToLowerInvariant()`

### Stage 2: Punctuation Stripping
- Replace all characters that are not letters, digits, or spaces with a single space character ` `. 
- Punctuation characters include standard ASCII punctuation and Markdown syntax symbols (`#`, `*`, `_`, `[`, `]`, `(`, `)`, `` ` ``, `-`, `+`, `!`, `?`, `.`, `,`, `;`, `:`, `/`, `\`).
- **Regex Reference:** `[^a-z0-9\s]` (case-insensitive) replaced with `" "`.

### Stage 3: Word Splitting
- Split the text using white space characters as delimiters.
- Remove empty entries caused by consecutive spaces.
- **C# Implementation:** `text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)`

### Stage 4: Stopword Filtering
- Filter out any token that matches the official stopword list below.
- Filter out any token that has a length of **1 character** (e.g., `a`, `i`, `s`).
- **C# Implementation:** Use `.NET 10 SearchValues<string>` containing the stopwords list for $O(1)$ lookup performance.

---

## 3. Official Stopword List

The system uses the standard English stopword list. The exact set of 50 terms is defined below:

```json
[
  "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", 
  "any", "are", "arent", "as", "at", "be", "because", "been", "before", "being", 
  "below", "between", "both", "but", "by", "can", "cant", "cannot", "could", "couldnt", 
  "did", "didnt", "do", "does", "doesnt", "doing", "dont", "down", "during", "each", 
  "few", "for", "from", "further", "had", "hadnt", "has", "hasnt", "have", "havent", 
  "having", "he", "hed", "hell", "hes", "her", "here", "heres", "hers", "herself", 
  "him", "himself", "his", "how", "hows", "i", "id", "ill", "im", "ive", "if", 
  "in", "into", "is", "isnt", "it", "its", "itself", "lets", "me", "more", "most", 
  "mustnt", "my", "myself", "no", "nor", "not", "of", "off", "on", "once", "only", 
  "or", "other", "ought", "our", "ours", "ourselves", "out", "over", "own", "same", 
  "shant", "she", "shed", "shell", "shes", "should", "shouldnt", "so", "some", "such", 
  "than", "that", "thats", "the", "their", "theirs", "them", "themselves", "then", "there", 
  "theres", "these", "they", "theyd", "theyll", "theyre", "theyve", "this", "those", "through", 
  "to", "too", "under", "until", "up", "very", "was", "wasnt", "we", "wed", "well", 
  "were", "weve", "werent", "what", "whats", "when", "whens", "where", "wheres", "which", 
  "while", "who", "whos", "whom", "why", "whys", "with", "wont", "would", "wouldnt", 
  "you", "youd", "youll", "youre", "youve", "your", "yours", "yourself", "yourselves"
]
```

---

## 4. Normalisation Examples

### Example 1: Headings & Body Text
- **Raw Input:** `## Refund Timeline (Business Days)`
- **Stage 1 (Lowercase):** `## refund timeline (business days)`
- **Stage 2 (Punctuation Strip):** `   refund timeline  business days `
- **Stage 3 (Word Split):** `["refund", "timeline", "business", "days"]`
- **Stage 4 (Stopword Filter):** `["refund", "timeline", "business", "days"]` (none were stopwords or single characters)

### Example 2: Text containing Stopwords & Single Letters
- **Raw Input:** `I want you to be happy with a refund.`
- **Stage 1 (Lowercase):** `i want you to be happy with a refund.`
- **Stage 2 (Punctuation Strip):** `i want you to be happy with a refund `
- **Stage 3 (Word Split):** `["i", "want", "you", "to", "be", "happy", "with", "a", "refund"]`
- **Stage 4 (Stopword Filter):** `["want", "happy", "refund"]`
  - *Filtered out:* `i` (single char), `you` (stopword), `to` (stopword), `be` (stopword), `with` (stopword), `a` (single char & stopword).
