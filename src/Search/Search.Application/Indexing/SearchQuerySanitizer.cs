namespace Haworks.Search.Application.Indexing;

/// <summary>
/// Cheap input cleanup for search queries. Strips control characters,
/// collapses internal whitespace, trims, and caps total terms — protects
/// Meilisearch from malformed inputs and obviously hostile queries
/// without trying to be a security sanitizer (Meilisearch is an
/// internally trusted service and the query is parameter, not SQL).
/// </summary>
public static class SearchQuerySanitizer
{
    private const int MaxTerms = 30;

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        // Strip control chars, collapse runs of whitespace.
        var cleaned = new System.Text.StringBuilder(raw.Length);
        var inWhitespace = false;
        foreach (var ch in raw)
        {
            if (char.IsControl(ch))
            {
                continue;
            }
            if (char.IsWhiteSpace(ch))
            {
                if (!inWhitespace && cleaned.Length > 0) cleaned.Append(' ');
                inWhitespace = true;
            }
            else
            {
                cleaned.Append(ch);
                inWhitespace = false;
            }
        }

        var trimmed = cleaned.ToString().Trim();
        if (trimmed.Length == 0) return "";

        var terms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > MaxTerms)
        {
            terms = terms[..MaxTerms];
        }
        return string.Join(' ', terms);
    }
}
