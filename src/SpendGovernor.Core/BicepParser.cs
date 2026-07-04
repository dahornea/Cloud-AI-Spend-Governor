using System.Text.RegularExpressions;

namespace SpendGovernor.Core;

public sealed class BicepParser
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Compute/virtualMachines",
        "Microsoft.Web/serverfarms",
        "Microsoft.Web/sites",
        "Microsoft.Storage/storageAccounts",
        "Microsoft.Sql/servers/databases",
        "Microsoft.DBforPostgreSQL/flexibleServers",
        "Microsoft.ContainerService/managedClusters",
        "Microsoft.App/containerApps",
        "Microsoft.Cache/Redis"
    };

    public IReadOnlyList<CloudResourceEstimateInput> Parse(
        IEnumerable<RepositoryFile> files,
        string defaultRegion,
        int hoursPerMonth,
        string? branch = null)
    {
        var resources = new List<CloudResourceEstimateInput>();
        foreach (var file in files.Where(file => FileDiscovery.Detect(file.Path).Kind == RelevantFileKind.Bicep))
        {
            var path = ParserText.NormalizePath(file.Path);
            var values = LoadValues(file.Content);
            foreach (var block in FindResourceBlocks(file.Content))
            {
                resources.Add(CreateResource(path, block.SymbolicName, block.Type, block.Body, values, defaultRegion, hoursPerMonth, branch));
            }
        }

        return resources;
    }

    private static Dictionary<string, string> LoadValues(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = ParserText.RemoveComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var paramMatch = Regex.Match(line, "^param\\s+(?<name>[A-Za-z0-9_]+)\\s+[^=]+?=\\s*(?<value>.+)$", RegexOptions.IgnoreCase);
            if (paramMatch.Success)
            {
                values[paramMatch.Groups["name"].Value] = ResolveValue(paramMatch.Groups["value"].Value, values) ?? "";
                continue;
            }

            var varMatch = Regex.Match(line, "^var\\s+(?<name>[A-Za-z0-9_]+)\\s*=\\s*(?<value>.+)$", RegexOptions.IgnoreCase);
            if (varMatch.Success)
            {
                values[varMatch.Groups["name"].Value] = ResolveValue(varMatch.Groups["value"].Value, values) ?? "";
            }
        }

        return values;
    }

    private static IEnumerable<(string SymbolicName, string Type, string Body)> FindResourceBlocks(string content)
    {
        foreach (Match match in Regex.Matches(content, "resource\\s+(?<name>[A-Za-z0-9_]+)\\s+'(?<type>[^'@]+)(?:@[^']+)?'\\s*=\\s*\\{", RegexOptions.IgnoreCase))
        {
            var brace = content.IndexOf('{', match.Index);
            if (brace < 0)
            {
                continue;
            }

            var end = ParserText.FindMatchingBrace(content, brace);
            if (end < 0)
            {
                continue;
            }

            yield return (
                match.Groups["name"].Value,
                match.Groups["type"].Value,
                content[(brace + 1)..end]);
        }
    }

    private static CloudResourceEstimateInput CreateResource(
        string sourceFile,
        string symbolicName,
        string type,
        string body,
        IReadOnlyDictionary<string, string> values,
        string defaultRegion,
        int hoursPerMonth,
        string? branch)
    {
        var tags = ExtractTags(body, values);
        var resourceName = ResolveValue(ParserText.FindColonProperty(body, "name"), values) ?? symbolicName;
        var explicitLocation = ResolveValue(ParserText.FindColonProperty(body, "location"), values);
        var location = explicitLocation ?? defaultRegion;
        var skuBlock = ParserText.FindBicepObject(body, "sku");
        var propertiesBlock = ParserText.FindBicepObject(body, "properties");

        var resource = new CloudResourceEstimateInput
        {
            SourceType = ResourceSourceType.Bicep,
            SourceFile = sourceFile,
            Provider = "azure",
            ResourceType = type,
            ResourceName = resourceName,
            Region = location,
            Quantity = 1,
            HoursPerMonth = hoursPerMonth,
            Tags = new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase),
            Environment = ParserText.InferEnvironment(sourceFile, tags, branch),
            IsSupported = SupportedTypes.Contains(type),
            Raw =
            {
                ["bicepSymbol"] = symbolicName,
                ["bicepType"] = type
            }
        };

        if (!resource.IsSupported)
        {
            resource.Warnings.Add("Resource type is outside the Bicep MVP support list.");
            return resource;
        }

        if (explicitLocation is null)
        {
            resource.Warnings.Add($"Region was not set in Bicep; defaulted to {defaultRegion}.");
        }

        switch (type)
        {
            case "Microsoft.Compute/virtualMachines":
                var hardware = ParserText.FindBicepObject(propertiesBlock ?? body, "hardwareProfile");
                resource.Sku = ResolveValue(ParserText.FindColonProperty(hardware ?? "", "vmSize"), values);
                break;
            case "Microsoft.Web/serverfarms":
                resource.Sku = ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "name"), values);
                resource.Tier = ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "tier"), values);
                resource.Quantity = ResolveInt(ParserText.FindColonProperty(skuBlock ?? "", "capacity"), values) ?? 1;
                break;
            case "Microsoft.Web/sites":
                resource.Sku = ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "name"), values);
                resource.Warnings.Add("Web app cost depends on the referenced App Service Plan; include the plan in the PR for higher confidence.");
                break;
            case "Microsoft.Storage/storageAccounts":
                resource.Sku = ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "name"), values);
                resource.Tier = ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "tier"), values);
                resource.Capacity = ResolveDecimal(ParserText.FindColonProperty(propertiesBlock ?? "", "estimatedGb"), values);
                break;
            case "Microsoft.Sql/servers/databases":
                resource.Sku = ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "name"), values);
                resource.Tier = ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "tier"), values);
                resource.Capacity = ResolveDecimal(ParserText.FindColonProperty(propertiesBlock ?? "", "maxSizeGb"), values);
                break;
            case "Microsoft.DBforPostgreSQL/flexibleServers":
                resource.Sku = ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "name"), values);
                var storage = ParserText.FindBicepObject(propertiesBlock ?? "", "storage");
                var storageMb = ResolveDecimal(ParserText.FindColonProperty(storage ?? "", "storageSizeGB"), values);
                resource.Capacity = storageMb;
                break;
            case "Microsoft.ContainerService/managedClusters":
                var agentPoolProfiles = ParserText.FindBicepObject(propertiesBlock ?? "", "agentPoolProfiles");
                resource.Sku = ResolveValue(ParserText.FindColonProperty(agentPoolProfiles ?? "", "vmSize"), values);
                resource.Quantity = ResolveInt(ParserText.FindColonProperty(agentPoolProfiles ?? "", "count"), values) ?? 1;
                break;
            case "Microsoft.App/containerApps":
                resource.Sku = ResolveValue(ParserText.FindColonProperty(ParserText.FindBicepObject(propertiesBlock ?? "", "workloadProfile") ?? "", "name"), values);
                resource.Warnings.Add("Container App usage-based compute needs runtime assumptions; the MVP marks it unknown unless a workload profile has a catalog price.");
                break;
            case "Microsoft.Cache/Redis":
                var redisSku = ResolveValue(ParserText.FindColonProperty(propertiesBlock ?? skuBlock ?? "", "sku"), values)
                    ?? ResolveValue(ParserText.FindColonProperty(skuBlock ?? "", "name"), values);
                var family = ResolveValue(ParserText.FindColonProperty(propertiesBlock ?? "", "family"), values);
                var capacity = ResolveValue(ParserText.FindColonProperty(propertiesBlock ?? "", "capacity"), values);
                resource.Sku = string.Join('_', new[] { redisSku, family, capacity }.Where(part => !string.IsNullOrWhiteSpace(part)));
                resource.Capacity = ResolveDecimal(capacity, values);
                break;
        }

        if (string.IsNullOrWhiteSpace(resource.Sku))
        {
            resource.Warnings.Add("SKU or size was not found; pricing confidence will be unknown.");
        }

        return resource;
    }

    private static IReadOnlyDictionary<string, string> ExtractTags(string body, IReadOnlyDictionary<string, string> values)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tagBlock = ParserText.FindBicepObject(body, "tags");
        if (tagBlock is null)
        {
            return tags;
        }

        foreach (var rawLine in tagBlock.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = ParserText.RemoveComment(rawLine).Trim().TrimEnd(',');
            if (line.Length == 0)
            {
                continue;
            }

            var index = line.IndexOf(':', StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var key = SpendGovConfigParser.Unquote(line[..index].Trim());
            var value = ResolveValue(line[(index + 1)..], values);
            if (value is not null)
            {
                tags[key] = value;
            }
        }

        return tags;
    }

    private static string? ResolveValue(string? value, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim().TrimEnd(',');
        var unquoted = SpendGovConfigParser.Unquote(value);
        if (values.TryGetValue(unquoted, out var direct))
        {
            return direct;
        }

        foreach (var pair in values)
        {
            unquoted = unquoted.Replace("${" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return unquoted;
    }

    private static int? ResolveInt(string? value, IReadOnlyDictionary<string, string> values)
    {
        var resolved = ResolveValue(value, values);
        return int.TryParse(resolved, out var parsed) ? parsed : null;
    }

    private static decimal? ResolveDecimal(string? value, IReadOnlyDictionary<string, string> values)
    {
        var resolved = ResolveValue(value, values);
        return ParserText.ParseDecimalValue(resolved);
    }
}
