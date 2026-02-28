using System.Text.RegularExpressions;

namespace RimSearcher.Core;

public static class FuzzyMatcher
{
    private static readonly Regex WordSplitRegex = new(@"[_\.\-\s]+", RegexOptions.Compiled);

    public static double CalculateFuzzyScore(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            return 0.0;

        string textLower = text.ToLowerInvariant();
        string queryLower = query.ToLowerInvariant();

        if (textLower == queryLower) return 100.0;
        if (textLower.StartsWith(queryLower)) return 90.0;

        int editDistance = LevenshteinDistance(textLower, queryLower);
        int queryLength = query.Length;
        int textLength = text.Length;

        if (editDistance <= 2)
        {
            double tolerance = queryLength <= 4 ? 0.5 : 0.3;
            if (editDistance <= queryLength * tolerance)
            {
                double typoScore = 95.0 - (editDistance * 5.0);
                if (Math.Abs(textLength - queryLength) <= 1) typoScore += 3.0;
                return Math.Min(typoScore, 95.0);
            }
        }

        if (IsCamelCaseMatch(text, query))
            return queryLength <= 5 ? 85.0 : 75.0;

        if (IsWordBoundaryMatch(text, query))
            return 80.0;

        int maxLength = Math.Max(textLength, queryLength);
        double similarity = 1.0 - (double)editDistance / maxLength;

        if (editDistance <= 3 && similarity >= 0.75) return 70.0 * similarity;
        if (similarity >= 0.6) return 55.0 * similarity;

        int substringIndex = textLower.IndexOf(queryLower);
        if (substringIndex >= 0)
        {
            double positionScore = 50.0 - (substringIndex * 2.0 / textLength * 10.0);
            double lengthRatio = (double)queryLength / textLength;
            positionScore += lengthRatio * 10.0;
            return Math.Max(30.0, Math.Min(positionScore, 50.0));
        }

        return 0.0;
    }

    private static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;

        int sourceLength = source.Length;
        int targetLength = target.Length;

        var previousRow = new int[targetLength + 1];
        var currentRow = new int[targetLength + 1];

        for (int j = 0; j <= targetLength; j++) previousRow[j] = j;

        for (int i = 1; i <= sourceLength; i++)
        {
            currentRow[0] = i;
            for (int j = 1; j <= targetLength; j++)
            {
                int cost = (source[i - 1] == target[j - 1]) ? 0 : 1;
                currentRow[j] = Math.Min(Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1), previousRow[j - 1] + cost);
            }
            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[targetLength];
    }

    private static bool IsWordBoundaryMatch(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return false;
        return SplitIntoWords(text).Any(word => word.StartsWith(query, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCamelCaseMatch(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return false;
        var initials = ExtractCamelCaseInitials(text);
        return initials.Equals(query, StringComparison.OrdinalIgnoreCase) || initials.StartsWith(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCamelCaseInitials(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return new string(SplitIntoWords(text).Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0])).ToArray());
    }

    public static List<string> SplitIntoWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();
        var parts = WordSplitRegex.Split(text);
        var result = new List<string>();

        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part)) result.AddRange(SplitCamelCase(part));
        }
        return result;
    }

    private static List<string> SplitCamelCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();
        var result = new List<string>();
        int wordStart = 0;

        for (int i = 1; i < text.Length; i++)
        {
            if ((char.IsUpper(text[i]) && char.IsLower(text[i - 1])) ||
                (i < text.Length - 1 && char.IsUpper(text[i]) && char.IsLower(text[i + 1]) && char.IsUpper(text[i - 1])))
            {
                result.Add(text.Substring(wordStart, i - wordStart));
                wordStart = i;
            }
        }

        if (wordStart < text.Length) result.Add(text.Substring(wordStart));
        return result.Where(w => !string.IsNullOrEmpty(w)).ToList();
    }

    public static List<string> GenerateNgrams(string text, int n, int maxCount = 50)
    {
        if (string.IsNullOrEmpty(text) || text.Length < n) return new List<string>();
        var ngrams = new List<string>();
        var lowerText = text.ToLowerInvariant();
        int limit = maxCount > 0 ? Math.Min(lowerText.Length - n + 1, maxCount) : lowerText.Length - n + 1;

        for (int i = 0; i < limit; i++) ngrams.Add(lowerText.Substring(i, n));
        return ngrams;
    }
}
