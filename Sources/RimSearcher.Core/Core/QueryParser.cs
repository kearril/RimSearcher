namespace RimSearcher.Core;


public class ParsedQuery
{
    public List<string> Keywords { get; set; } = new();
    public string? TypeFilter { get; set; }
    public string? MethodFilter { get; set; }
    public string? FieldFilter { get; set; }
    public string? DefFilter { get; set; }
}

public static class QueryParser
{
    public static ParsedQuery Parse(string rawQuery)
    {
        var result = new ParsedQuery();

        if (string.IsNullOrWhiteSpace(rawQuery))
            return result;
        
        var tokens = SplitQuery(rawQuery);

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;
            
            if (token.Contains(':'))
            {
                var parts = token.Split(':', 2);
                if (parts.Length == 2)
                {
                    var prefix = parts[0].ToLowerInvariant();
                    var value = parts[1];

                    switch (prefix)
                    {
                        case "method" or "m":
                            result.MethodFilter = value;
                            break;
                        case "type" or "t" or "class" or "c":
                            result.TypeFilter = value;
                            break;
                        case "field" or "f" or "property" or "p":
                            result.FieldFilter = value;
                            break;
                        case "def" or "d":
                            result.DefFilter = value;
                            break;
                        default:
                            result.Keywords.Add(token);
                            break;
                    }
                    continue;
                }
            }
            
            result.Keywords.Add(token);
        }

        return result;
    }
    
    private static List<string> SplitQuery(string query)
    {
        var tokens = new List<string>();
        var currentToken = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (currentToken.Length > 0)
                {
                    tokens.Add(currentToken.ToString());
                    currentToken.Clear();
                }
            }
            else
            {
                currentToken.Append(c);
            }
        }

        if (currentToken.Length > 0)
        {
            tokens.Add(currentToken.ToString());
        }

        return tokens;
    }
    
    public static string GetCombinedSearchTerm(ParsedQuery query)
    {
        var terms = new List<string>();

        if (!string.IsNullOrEmpty(query.TypeFilter))
            terms.Add(query.TypeFilter);
        if (!string.IsNullOrEmpty(query.MethodFilter))
            terms.Add(query.MethodFilter);
        if (!string.IsNullOrEmpty(query.FieldFilter))
            terms.Add(query.FieldFilter);
        if (!string.IsNullOrEmpty(query.DefFilter))
            terms.Add(query.DefFilter);

        terms.AddRange(query.Keywords);

        return string.Join(" ", terms);
    }
}
