using System;
using System.Collections.Generic;
using System.Text.Json;
using ConsoleAppFramework;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Models;
using RimSearcher.Cli.Queries;

namespace RimSearcher.Cli.Commands;

internal static class DefCommands
{
    public static void Register(ConsoleApp.ConsoleAppBuilder app, DefRepository repository, JsonOutput output)
    {
        app.Add("get", ([Argument] string defName, string? type = null, bool brief = false, string? field = null) =>
        {
            // 参数互斥：--brief 与 --field 都是提取视图，同时给出为参数错误。
            if (brief && field != null)
            {
                Console.Error.WriteLine("Error: --brief and --field are mutually exclusive");
                Environment.Exit(ExitCodes.Error);
            }

            if (type == null)
            {
                var types = repository.FindTypes(defName);
                if (types.Count == 0)
                {
                    Console.Error.WriteLine($"Error: no Def found with defName '{defName}'");
                    Environment.Exit(ExitCodes.NotFound);
                }
                if (types.Count > 1)
                {
                    Console.Error.WriteLine($"Error: '{defName}' matches multiple Def types. Specify --type:");
                    foreach (var candidateType in types)
                        Console.Error.WriteLine($"  {candidateType}");
                    Environment.Exit(ExitCodes.NotFound);
                }
                type = types[0];
            }

            if (brief)
            {
                var source = repository.GetBriefSource(defName, type!);
                if (source == null)
                {
                    Console.Error.WriteLine($"Error: no Def found with defName '{defName}' and type '{type}'");
                    Environment.Exit(ExitCodes.NotFound);
                }

                // 统一提取所有 *Class 桥接字段：不过滤 def_type、不限嵌套深度，
                // 规则仅为"属性名以 Class 结尾且值为字符串"（排除 useGraphicClass 这类布尔陷阱）。
                using var document = JsonDocument.Parse(source.FullData);
                var classNames = new List<string>();
                CollectClassFields(document.RootElement, classNames);

                var distinctClasses = classNames
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                if (distinctClasses.Length == 0)
                    Console.Error.WriteLine($"Hint: no *Class fields found; try 'fields {defName} --type {type}'");

                output.Write(new BriefDef(
                    source.DefName, source.DefType, source.Label, source.ModName,
                    source.PackageId,
                    distinctClasses));
                return;
            }

            if (field != null)
            {
                var source = repository.GetBriefSource(defName, type!);
                if (source == null)
                {
                    Console.Error.WriteLine($"Error: no Def found with defName '{defName}' and type '{type}'");
                    Environment.Exit(ExitCodes.NotFound);
                }

                using var document = JsonDocument.Parse(source.FullData);
                var status = JsonFieldNavigator.TryNavigate(document.RootElement, field, out var value);
                if (status == JsonFieldNavigator.NavigateStatus.MalformedPath)
                {
                    Console.Error.WriteLine($"Error: malformed field path '{field}' (expected format: a.b[0].c)");
                    Environment.Exit(ExitCodes.Error);
                }
                if (status == JsonFieldNavigator.NavigateStatus.NotFound)
                {
                    Console.Error.WriteLine($"Error: field '{field}' not found in '{defName}' (type '{type}')");
                    Environment.Exit(ExitCodes.NotFound);
                }
                Console.WriteLine(value.GetRawText());
                return;
            }

            var fullData = repository.GetFullData(defName, type!);
            if (fullData == null)
            {
                Console.Error.WriteLine($"Error: no Def found with defName '{defName}' and type '{type}'");
                Environment.Exit(ExitCodes.NotFound);
            }
            Console.WriteLine(fullData);
        });
    }

    /// <summary>
    /// 递归收集 JSON 中所有"属性名以 Class 结尾且值为字符串"的字段值，
    /// 作为 Def 通往 C# 类型的桥接线索（thingClass/compClass/workerClass/hediffClass…）。
    /// </summary>
    private static void CollectClassFields(JsonElement element, List<string> classNames)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.EndsWith("Class", StringComparison.Ordinal)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var className = property.Value.GetString();
                        if (!string.IsNullOrEmpty(className))
                            classNames.Add(className);
                    }
                    CollectClassFields(property.Value, classNames);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectClassFields(item, classNames);
                break;
        }
    }
}
