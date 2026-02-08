using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace RimSearcher.Core;

public static class RoslynHelper
{
    private const long MaxFileSize = 2 * 1024 * 1024; // 2MB

    public static Dictionary<string, string?> GetClassInheritanceMap(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileSize) return new Dictionary<string, string?>();

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var code = reader.ReadToEnd();

            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetCompilationUnitRoot();

            return root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Select(t =>
                {
                    var fullName = GetFullTypeName(t);
                    var baseType = t.BaseList?.Types.FirstOrDefault()?.ToString();
                    return new { FullName = fullName, Base = baseType };
                })
                .GroupBy(x => x.FullName)
                .ToDictionary(g => g.Key, g => g.First().Base);
        }
        catch
        {
            return new Dictionary<string, string?>();
        }
    }

    private static string GetFullTypeName(TypeDeclarationSyntax typeDeclaration)
    {
        var nameStack = new Stack<string>();
        nameStack.Push(typeDeclaration.Identifier.Text);
        var parent = typeDeclaration.Parent;
        while (parent != null)
        {
            if (parent is TypeDeclarationSyntax p) nameStack.Push(p.Identifier.Text);
            else if (parent is NamespaceDeclarationSyntax ns) nameStack.Push(ns.Name.ToString());
            else if (parent is FileScopedNamespaceDeclarationSyntax fns) nameStack.Push(fns.Name.ToString());
            parent = parent.Parent;
        }

        return string.Join(".", nameStack);
    }

    public static async Task<string> GetClassOutlineAsync(string filePath, string? targetTypeName = null)
    {
        if (!File.Exists(filePath)) return "File not found.";

        // Limit file size to prevent excessive memory usage during parsing.
        if (new FileInfo(filePath).Length > MaxFileSize) return "File too large (over 2MB), skipping parsing.";

        string code;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            code = await reader.ReadToEndAsync();
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();

        var sb = new StringBuilder();
        var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

        foreach (var type in types)
        {
            var fullName = GetFullTypeName(type);
            if (!string.IsNullOrEmpty(targetTypeName) &&
                !fullName.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase) &&
                !type.Identifier.Text.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string kind = type switch
            {
                ClassDeclarationSyntax => "Class",
                StructDeclarationSyntax => "Struct",
                InterfaceDeclarationSyntax => "Interface",
                RecordDeclarationSyntax => "Record",
                _ => "Type"
            };

            sb.AppendLine($"{kind}: {fullName} {type.TypeParameterList}");
            foreach (var prop in type.Members.OfType<PropertyDeclarationSyntax>())
                sb.AppendLine($"  Property: {prop.Type} {prop.Identifier.Text}");
            foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
            {
                var fieldName = string.Join(", ", field.Declaration.Variables.Select(v => v.Identifier.Text));
                sb.AppendLine($"  Field: {field.Declaration.Type} {fieldName}");
            }

            foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
            {
                var parameters = string.Join(", ",
                    method.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.Text}"));
                sb.AppendLine($"  Method: {method.ReturnType} {method.Identifier.Text}({parameters})");
            }

            sb.AppendLine();
        }

        return sb.Length > 0
            ? sb.ToString()
            : (targetTypeName != null
                ? $"Type not found in file: {targetTypeName}"
                : "No type definitions found in file.");
    }

    public static async Task<string> GetMethodBodyAsync(string filePath, string methodName, string? typeName = null)
    {
        if (!File.Exists(filePath)) return "File not found.";
        if (new FileInfo(filePath).Length > MaxFileSize) return "File too large (over 2MB), skipping parsing.";

        string code;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            code = await reader.ReadToEndAsync();
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        var matches = methods.Where(m => m.Identifier.Text.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrEmpty(typeName))
        {
            matches = matches.Where(m =>
            {
                var parentType = m.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                return parentType != null && (
                    parentType.Identifier.Text.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                    GetFullTypeName(parentType).Equals(typeName, StringComparison.OrdinalIgnoreCase)
                );
            }).ToList();
        }

        if (matches.Count == 0)
        {
            var availableMethods = methods.Select(m => m.Identifier.Text).Distinct().OrderBy(n => n).ToList();
            var sbErr = new StringBuilder();
            sbErr.AppendLine($"Method '{methodName}' not found.");
            if (availableMethods.Count > 0)
            {
                sbErr.AppendLine("\nAvailable methods in this file:");
                foreach (var mName in availableMethods) sbErr.AppendLine($"- {mName}");
                sbErr.AppendLine("\nHint: Choose one of the method names above and call 'read_code' again.");
            }

            return sbErr.ToString();
        }

        if (matches.Count == 1)
        {
            var m = matches[0];
            var lineSpan = m.GetLocation().GetLineSpan();
            return $"// File: {filePath}\n// Starts at line: {lineSpan.StartLinePosition.Line + 1}\n{m.ToFullString()}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"/* Found {matches.Count} matching method overloads or conflicts */");
        foreach (var m in matches)
        {
            var lineSpan = m.GetLocation().GetLineSpan();
            var parentType = m.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            sb.AppendLine($"// Type: {(parentType != null ? GetFullTypeName(parentType) : "Unknown")}");
            sb.AppendLine($"// Starts at line: {lineSpan.StartLinePosition.Line + 1}");
            sb.AppendLine(m.ToFullString());
            sb.AppendLine("\n// --- NEXT MATCH ---\n");
        }

        return sb.ToString();
    }
}