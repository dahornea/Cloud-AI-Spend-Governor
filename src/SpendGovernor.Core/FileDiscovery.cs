namespace SpendGovernor.Core;

public static class FileDiscovery
{
    public static DetectedFile Detect(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        var lower = normalized.ToLowerInvariant();
        var kind = lower switch
        {
            ".spendgov.yml" => RelevantFileKind.SpendGovConfig,
            ".spendgov.yaml" => RelevantFileKind.SpendGovConfig,
            "ai-spend.yml" => RelevantFileKind.AiSpendConfig,
            "ai-spend.yaml" => RelevantFileKind.AiSpendConfig,
            _ when fileName.Equals(".spendgov.yml", StringComparison.OrdinalIgnoreCase) => RelevantFileKind.SpendGovConfig,
            _ when fileName.Equals(".spendgov.yaml", StringComparison.OrdinalIgnoreCase) => RelevantFileKind.SpendGovConfig,
            _ when fileName.Equals("ai-spend.yml", StringComparison.OrdinalIgnoreCase) => RelevantFileKind.AiSpendConfig,
            _ when fileName.Equals("ai-spend.yaml", StringComparison.OrdinalIgnoreCase) => RelevantFileKind.AiSpendConfig,
            _ when lower.EndsWith(".tfvars", StringComparison.Ordinal) => RelevantFileKind.TerraformVars,
            _ when lower.EndsWith(".tf", StringComparison.Ordinal) => RelevantFileKind.Terraform,
            _ when lower.EndsWith(".bicepparam", StringComparison.Ordinal) => RelevantFileKind.BicepParam,
            _ when lower.EndsWith(".bicep", StringComparison.Ordinal) => RelevantFileKind.Bicep,
            _ => RelevantFileKind.Other
        };

        return new DetectedFile(normalized, kind);
    }

    public static IReadOnlyList<DetectedFile> DetectMany(IEnumerable<string> paths)
    {
        return paths.Select(Detect).Where(file => file.IsRelevant).ToArray();
    }

    public static bool HasRelevantFiles(IEnumerable<string> paths)
    {
        return paths.Any(path => Detect(path).IsRelevant);
    }
}

internal static class ParserText
{
    public static string NormalizePath(string path) => path.Replace('\\', '/');

    public static string? FindAssignment(string body, string key)
    {
        foreach (var rawLine in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = RemoveComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var index = line.IndexOf('=', StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var candidate = line[..index].Trim();
            if (candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(index + 1)..].Trim().TrimEnd(',');
            }
        }

        return null;
    }

    public static string? FindColonProperty(string body, string key)
    {
        foreach (var rawLine in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = RemoveComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var index = line.IndexOf(':', StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var candidate = line[..index].Trim();
            if (candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(index + 1)..].Trim().TrimEnd(',');
            }
        }

        return null;
    }

    public static string? FindTerraformBlock(string body, string blockName)
    {
        var token = blockName + " ";
        var index = body.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            if (index > 0 && (char.IsLetterOrDigit(body[index - 1]) || body[index - 1] == '_'))
            {
                index = body.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var brace = body.IndexOf('{', index);
            if (brace < 0)
            {
                return null;
            }

            var end = FindMatchingBrace(body, brace);
            return end < 0 ? null : body[(brace + 1)..end];
        }

        return null;
    }

    public static string? FindBicepObject(string body, string objectName)
    {
        var token = objectName + ":";
        var index = body.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            if (index > 0 && (char.IsLetterOrDigit(body[index - 1]) || body[index - 1] == '_'))
            {
                index = body.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var brace = body.IndexOf('{', index);
            if (brace < 0)
            {
                return null;
            }

            var end = FindMatchingBrace(body, brace);
            return end < 0 ? null : body[(brace + 1)..end];
        }

        return null;
    }

    public static int FindMatchingBrace(string text, int openingBrace)
    {
        var depth = 0;
        var inString = false;
        var stringQuote = '\0';

        for (var i = openingBrace; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == stringQuote && (i == 0 || text[i - 1] != '\\'))
                {
                    inString = false;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                inString = true;
                stringQuote = c;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    public static IReadOnlyDictionary<string, string> ParseTerraformMap(string? raw)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return values;
        }

        var body = raw;
        if (raw.TrimStart().StartsWith('{'))
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            body = start >= 0 && end > start ? raw[(start + 1)..end] : raw;
        }

        foreach (var line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var cleaned = RemoveComment(line).Trim().TrimEnd(',');
            if (cleaned.Length == 0)
            {
                continue;
            }

            var separator = cleaned.Contains('=', StringComparison.Ordinal) ? '=' : ':';
            var index = cleaned.IndexOf(separator);
            if (index < 0)
            {
                continue;
            }

            var key = SpendGovConfigParser.Unquote(cleaned[..index].Trim());
            var value = SpendGovConfigParser.Unquote(cleaned[(index + 1)..].Trim());
            values[key] = value;
        }

        return values;
    }

    public static string RemoveComment(string line)
    {
        var inQuote = false;
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuote)
            {
                if (c == quote && (i == 0 || line[i - 1] != '\\'))
                {
                    inQuote = false;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                inQuote = true;
                quote = c;
                continue;
            }

            if (c == '#')
            {
                return line[..i];
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                return line[..i];
            }
        }

        return line;
    }

    public static string? InferEnvironment(string path, IReadOnlyDictionary<string, string> tags, string? branch = null)
    {
        foreach (var key in new[] { "environment", "env", "stage" })
        {
            if (tags.TryGetValue(key, out var tagValue) && !string.IsNullOrWhiteSpace(tagValue))
            {
                return tagValue;
            }
        }

        var haystack = (NormalizePath(path) + "/" + branch).ToLowerInvariant();
        if (haystack.Contains("prod", StringComparison.Ordinal))
        {
            return "production";
        }

        if (haystack.Contains("staging", StringComparison.Ordinal) || haystack.Contains("stage", StringComparison.Ordinal))
        {
            return "staging";
        }

        if (haystack.Contains("qa", StringComparison.Ordinal))
        {
            return "qa";
        }

        if (haystack.Contains("dev", StringComparison.Ordinal))
        {
            return "dev";
        }

        return null;
    }

    public static decimal? ParseDecimalValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = SpendGovConfigParser.Unquote(value.Trim());
        return decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
