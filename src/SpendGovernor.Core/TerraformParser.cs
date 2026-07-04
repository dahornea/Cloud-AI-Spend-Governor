using System.Text.RegularExpressions;

namespace SpendGovernor.Core;

public sealed class TerraformParser
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "azurerm_linux_virtual_machine",
        "azurerm_windows_virtual_machine",
        "azurerm_virtual_machine_scale_set",
        "azurerm_service_plan",
        "azurerm_linux_web_app",
        "azurerm_windows_web_app",
        "azurerm_storage_account",
        "azurerm_mssql_database",
        "azurerm_postgresql_flexible_server",
        "azurerm_kubernetes_cluster",
        "azurerm_kubernetes_cluster_node_pool",
        "azurerm_container_app",
        "azurerm_redis_cache"
    };

    public IReadOnlyList<CloudResourceEstimateInput> Parse(
        IEnumerable<RepositoryFile> files,
        string defaultRegion,
        int hoursPerMonth,
        string? branch = null)
    {
        var normalizedFiles = files
            .Select(file => new RepositoryFile(ParserText.NormalizePath(file.Path), file.Content))
            .ToArray();
        var variables = LoadVariables(normalizedFiles);
        var resources = new List<CloudResourceEstimateInput>();

        foreach (var file in normalizedFiles.Where(file => FileDiscovery.Detect(file.Path).Kind == RelevantFileKind.Terraform))
        {
            foreach (var block in FindResourceBlocks(file.Content))
            {
                var input = CreateResource(file.Path, block.Type, block.Name, block.Body, variables, defaultRegion, hoursPerMonth, branch);
                resources.Add(input);
            }
        }

        return resources;
    }

    private static Dictionary<string, string> LoadVariables(IEnumerable<RepositoryFile> files)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (FileDiscovery.Detect(file.Path).Kind == RelevantFileKind.Terraform)
            {
                foreach (Match match in Regex.Matches(file.Content, "variable\\s+\"(?<name>[^\"]+)\"\\s*\\{", RegexOptions.IgnoreCase))
                {
                    var brace = file.Content.IndexOf('{', match.Index);
                    if (brace < 0)
                    {
                        continue;
                    }

                    var end = ParserText.FindMatchingBrace(file.Content, brace);
                    if (end < 0)
                    {
                        continue;
                    }

                    var body = file.Content[(brace + 1)..end];
                    var defaultValue = ParserText.FindAssignment(body, "default");
                    if (defaultValue is not null)
                    {
                        values[match.Groups["name"].Value] = ResolveValue(defaultValue, values) ?? SpendGovConfigParser.Unquote(defaultValue);
                    }
                }
            }

            if (FileDiscovery.Detect(file.Path).Kind == RelevantFileKind.TerraformVars)
            {
                foreach (var line in file.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                {
                    var cleaned = ParserText.RemoveComment(line).Trim();
                    if (cleaned.Length == 0)
                    {
                        continue;
                    }

                    var index = cleaned.IndexOf('=', StringComparison.Ordinal);
                    if (index <= 0)
                    {
                        continue;
                    }

                    var key = cleaned[..index].Trim();
                    var value = cleaned[(index + 1)..].Trim().TrimEnd(',');
                    values[key] = ResolveValue(value, values) ?? SpendGovConfigParser.Unquote(value);
                }
            }
        }

        return values;
    }

    private static IEnumerable<(string Type, string Name, string Body)> FindResourceBlocks(string content)
    {
        foreach (Match match in Regex.Matches(content, "resource\\s+\"(?<type>[^\"]+)\"\\s+\"(?<name>[^\"]+)\"\\s*\\{", RegexOptions.IgnoreCase))
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
                match.Groups["type"].Value,
                match.Groups["name"].Value,
                content[(brace + 1)..end]);
        }
    }

    private static CloudResourceEstimateInput CreateResource(
        string sourceFile,
        string type,
        string name,
        string body,
        IReadOnlyDictionary<string, string> variables,
        string defaultRegion,
        int hoursPerMonth,
        string? branch)
    {
        var tags = ExtractTags(body, variables);
        var explicitLocation = ResolveValue(ParserText.FindAssignment(body, "location"), variables)
            ?? ResolveValue(ParserText.FindAssignment(body, "region"), variables);
        var location = explicitLocation ?? defaultRegion;

        var resource = new CloudResourceEstimateInput
        {
            SourceType = ResourceSourceType.Terraform,
            SourceFile = sourceFile,
            Provider = "azure",
            ResourceType = type,
            ResourceName = name,
            Region = location,
            Quantity = ResolveInt(ParserText.FindAssignment(body, "count"), variables) ?? 1,
            HoursPerMonth = hoursPerMonth,
            Tags = new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase),
            Environment = ParserText.InferEnvironment(sourceFile, tags, branch),
            IsSupported = SupportedTypes.Contains(type),
            Raw =
            {
                ["terraformType"] = type,
                ["terraformName"] = name
            }
        };

        if (!resource.IsSupported)
        {
            resource.Warnings.Add("Resource type is outside the Terraform MVP support list.");
            return resource;
        }

        if (explicitLocation is null)
        {
            resource.Warnings.Add($"Region was not set in Terraform; defaulted to {defaultRegion}.");
        }

        switch (type)
        {
            case "azurerm_linux_virtual_machine":
            case "azurerm_windows_virtual_machine":
                resource.Sku = ResolveValue(ParserText.FindAssignment(body, "size"), variables);
                break;
            case "azurerm_virtual_machine_scale_set":
                resource.Sku = ResolveValue(ParserText.FindAssignment(body, "sku"), variables)
                    ?? ResolveValue(ParserText.FindAssignment(ParserText.FindTerraformBlock(body, "sku") ?? "", "name"), variables);
                resource.Quantity = ResolveInt(ParserText.FindAssignment(body, "instances"), variables)
                    ?? ResolveInt(ParserText.FindAssignment(ParserText.FindTerraformBlock(body, "sku") ?? "", "capacity"), variables)
                    ?? resource.Quantity;
                break;
            case "azurerm_service_plan":
                resource.Sku = ResolveValue(ParserText.FindAssignment(body, "sku_name"), variables);
                resource.Tier = ResolveValue(ParserText.FindAssignment(body, "os_type"), variables);
                break;
            case "azurerm_linux_web_app":
            case "azurerm_windows_web_app":
                resource.Sku = ResolveValue(ParserText.FindAssignment(body, "sku_name"), variables);
                resource.Warnings.Add("Web app cost depends on the referenced App Service Plan; include the plan in the PR for higher confidence.");
                break;
            case "azurerm_storage_account":
                var tier = ResolveValue(ParserText.FindAssignment(body, "account_tier"), variables);
                var replication = ResolveValue(ParserText.FindAssignment(body, "account_replication_type"), variables);
                resource.Tier = tier;
                resource.Sku = string.Join('_', new[] { tier, replication }.Where(part => !string.IsNullOrWhiteSpace(part)));
                resource.Capacity = ResolveDecimal(ParserText.FindAssignment(body, "estimated_gb"), variables);
                break;
            case "azurerm_mssql_database":
                resource.Sku = ResolveValue(ParserText.FindAssignment(body, "sku_name"), variables);
                resource.Capacity = ResolveDecimal(ParserText.FindAssignment(body, "max_size_gb"), variables);
                break;
            case "azurerm_postgresql_flexible_server":
                resource.Sku = ResolveValue(ParserText.FindAssignment(body, "sku_name"), variables);
                var storageMb = ResolveDecimal(ParserText.FindAssignment(body, "storage_mb"), variables);
                resource.Capacity = storageMb is null ? null : decimal.Round(storageMb.Value / 1024m, 2);
                break;
            case "azurerm_kubernetes_cluster":
                var nodePool = ParserText.FindTerraformBlock(body, "default_node_pool");
                resource.Sku = ResolveValue(ParserText.FindAssignment(nodePool ?? "", "vm_size"), variables);
                resource.Quantity = ResolveInt(ParserText.FindAssignment(nodePool ?? "", "node_count"), variables) ?? resource.Quantity;
                break;
            case "azurerm_kubernetes_cluster_node_pool":
                resource.Sku = ResolveValue(ParserText.FindAssignment(body, "vm_size"), variables);
                resource.Quantity = ResolveInt(ParserText.FindAssignment(body, "node_count"), variables) ?? resource.Quantity;
                break;
            case "azurerm_container_app":
                resource.Sku = ResolveValue(ParserText.FindAssignment(body, "workload_profile_name"), variables);
                resource.Warnings.Add("Container App usage-based compute needs runtime assumptions; the MVP marks it unknown unless a workload profile has a catalog price.");
                break;
            case "azurerm_redis_cache":
                var sku = ResolveValue(ParserText.FindAssignment(body, "sku_name"), variables);
                var family = ResolveValue(ParserText.FindAssignment(body, "family"), variables);
                var capacity = ResolveValue(ParserText.FindAssignment(body, "capacity"), variables);
                resource.Sku = string.Join('_', new[] { sku, family, capacity }.Where(part => !string.IsNullOrWhiteSpace(part)));
                resource.Capacity = ResolveDecimal(ParserText.FindAssignment(body, "capacity"), variables);
                break;
        }

        if (string.IsNullOrWhiteSpace(resource.Sku))
        {
            resource.Warnings.Add("SKU or size was not found; pricing confidence will be unknown.");
        }

        return resource;
    }

    private static IReadOnlyDictionary<string, string> ExtractTags(string body, IReadOnlyDictionary<string, string> variables)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var block = FindMapAssignment(body, "tags");

        foreach (var pair in ParserText.ParseTerraformMap(block))
        {
            tags[pair.Key] = ResolveValue(pair.Value, variables) ?? pair.Value;
        }

        return tags;
    }

    private static string? FindMapAssignment(string body, string key)
    {
        var pattern = key + " =";
        var index = body.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            if (index > 0 && (char.IsLetterOrDigit(body[index - 1]) || body[index - 1] == '_'))
            {
                index = body.IndexOf(pattern, index + pattern.Length, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var brace = body.IndexOf('{', index);
            if (brace < 0)
            {
                return ParserText.FindAssignment(body, key);
            }

            var end = ParserText.FindMatchingBrace(body, brace);
            return end < 0 ? null : body[brace..(end + 1)];
        }

        var block = ParserText.FindTerraformBlock(body, key);
        return block is null ? null : "{" + block + "}";
    }

    private static string? ResolveValue(string? value, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim().TrimEnd(',');
        var unquoted = SpendGovConfigParser.Unquote(value);
        if (unquoted.StartsWith("var.", StringComparison.OrdinalIgnoreCase))
        {
            var variableName = unquoted[4..];
            return variables.TryGetValue(variableName, out var variableValue) ? variableValue : null;
        }

        foreach (var pair in variables)
        {
            unquoted = unquoted.Replace("${var." + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return unquoted;
    }

    private static int? ResolveInt(string? value, IReadOnlyDictionary<string, string> variables)
    {
        var resolved = ResolveValue(value, variables);
        return int.TryParse(resolved, out var parsed) ? parsed : null;
    }

    private static decimal? ResolveDecimal(string? value, IReadOnlyDictionary<string, string> variables)
    {
        var resolved = ResolveValue(value, variables);
        return ParserText.ParseDecimalValue(resolved);
    }
}
