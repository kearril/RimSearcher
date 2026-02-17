using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace RimSearcher.Core;

public static class RoslynHelper
{
    private const long MaxFileSize = 10 * 1024 * 1024;

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

    /// <summary>
    /// Parses a C# file once and extracts both inheritance map and all members.
    /// Avoids the double-parse overhead of calling GetClassInheritanceMap + ExtractAllMembers separately.
    /// </summary>
    public static (Dictionary<string, string?> Inheritance, List<(string TypeName, string MemberName, string MemberType)> Members)
        GetClassInfoCombined(string path)
    {
        var emptyInheritance = new Dictionary<string, string?>();
        var emptyMembers = new List<(string, string, string)>();

        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxFileSize)
                return (emptyInheritance, emptyMembers);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var code = reader.ReadToEnd();

            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetCompilationUnitRoot();

            var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();

            // Extract inheritance
            var inheritance = types
                .Select(t => new { FullName = GetFullTypeName(t), Base = t.BaseList?.Types.FirstOrDefault()?.ToString() })
                .GroupBy(x => x.FullName)
                .ToDictionary(g => g.Key, g => g.First().Base);

            // Extract members
            var members = new List<(string TypeName, string MemberName, string MemberType)>();
            foreach (var type in types)
            {
                var typeName = GetFullTypeName(type);
                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                    members.Add((typeName, method.Identifier.Text, "Method"));
                foreach (var prop in type.Members.OfType<PropertyDeclarationSyntax>())
                    members.Add((typeName, prop.Identifier.Text, "Property"));
                foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
                    foreach (var variable in field.Declaration.Variables)
                        members.Add((typeName, variable.Identifier.Text, "Field"));
                foreach (var evt in type.Members.OfType<EventFieldDeclarationSyntax>())
                    foreach (var variable in evt.Declaration.Variables)
                        members.Add((typeName, variable.Identifier.Text, "Event"));
            }

            return (inheritance, members);
        }
        catch
        {
            return (emptyInheritance, emptyMembers);
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
        if (new FileInfo(filePath).Length > MaxFileSize) return "File too large, skipping parsing.";

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

        return sb.Length > 0 ? sb.ToString() : "No matching types found.";
    }

    public static async Task<string> GetMethodBodyAsync(string filePath, string methodName, string? typeName = null)
    {
        if (!File.Exists(filePath)) return "File not found.";
        if (new FileInfo(filePath).Length > MaxFileSize) return "File too large, skipping parsing.";

        string code;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            code = await reader.ReadToEndAsync();
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        var matches = methods.Where(m => m.Identifier.Text.Equals(methodName, StringComparison.OrdinalIgnoreCase)).ToList();

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

        if (matches.Count == 0) return $"Method '{methodName}' not found.";

        if (matches.Count == 1)
        {
            var m = matches[0];
            var lineSpan = m.GetLocation().GetLineSpan();
            return $"// File: {filePath}\n// Starts at line: {lineSpan.StartLinePosition.Line + 1}\n{m.ToFullString()}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"/* Found {matches.Count} matching method overloads */");
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

    public static async Task<string> GetMemberBodyAsync(string filePath, string memberName, string? typeName = null)
    {
        if (!File.Exists(filePath)) return "File not found.";
        if (new FileInfo(filePath).Length > MaxFileSize) return "File too large, skipping parsing.";

        string code;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            code = await reader.ReadToEndAsync();
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();

        bool TypeFilter(SyntaxNode node)
        {
            if (string.IsNullOrEmpty(typeName)) return true;
            var parentType = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            return parentType != null && (
                parentType.Identifier.Text.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                GetFullTypeName(parentType).Equals(typeName, StringComparison.OrdinalIgnoreCase));
        }

        var candidates = new List<(SyntaxNode Node, string Kind)>();

        // Methods
        foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                     .Where(m => m.Identifier.Text.Equals(memberName, StringComparison.OrdinalIgnoreCase) && TypeFilter(m)))
            candidates.Add((m, "Method"));

        // Properties
        foreach (var p in root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
                     .Where(p => p.Identifier.Text.Equals(memberName, StringComparison.OrdinalIgnoreCase) && TypeFilter(p)))
            candidates.Add((p, "Property"));

        // Constructors (match by class name or ".ctor")
        foreach (var c in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
                     .Where(c => (c.Identifier.Text.Equals(memberName, StringComparison.OrdinalIgnoreCase) ||
                                  memberName.Equals(".ctor", StringComparison.OrdinalIgnoreCase)) && TypeFilter(c)))
            candidates.Add((c, "Constructor"));

        // Indexers (match by "this" or "Indexer")
        if (memberName.Equals("this", StringComparison.OrdinalIgnoreCase) ||
            memberName.Equals("indexer", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var idx in root.DescendantNodes().OfType<IndexerDeclarationSyntax>().Where(TypeFilter))
                candidates.Add((idx, "Indexer"));
        }

        // Operators
        foreach (var op in root.DescendantNodes().OfType<OperatorDeclarationSyntax>()
                     .Where(o => o.OperatorToken.Text.Equals(memberName, StringComparison.OrdinalIgnoreCase) && TypeFilter(o)))
            candidates.Add((op, "Operator"));

        if (candidates.Count == 0) return $"Member '{memberName}' not found.";

        if (candidates.Count == 1)
        {
            var (node, kind) = candidates[0];
            var lineSpan = node.GetLocation().GetLineSpan();
            return $"// File: {filePath}\n// {kind}, starts at line: {lineSpan.StartLinePosition.Line + 1}\n{node.ToFullString()}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"/* Found {candidates.Count} matching members */");
        foreach (var (node, kind) in candidates)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            var parentType = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            sb.AppendLine($"// {kind} in {(parentType != null ? GetFullTypeName(parentType) : "Unknown")}");
            sb.AppendLine($"// Starts at line: {lineSpan.StartLinePosition.Line + 1}");
            sb.AppendLine(node.ToFullString());
            sb.AppendLine("\n// --- NEXT MATCH ---\n");
        }
        return sb.ToString();
    }

    public static async Task<string> GetClassBodyAsync(string filePath, string className)
    {
        if (!File.Exists(filePath)) return "File not found.";
        if (new FileInfo(filePath).Length > MaxFileSize) return "File too large, skipping parsing.";

        string code;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            code = await reader.ReadToEndAsync();
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();

        var typeMatch = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t =>
                t.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase) ||
                GetFullTypeName(t).Equals(className, StringComparison.OrdinalIgnoreCase));

        if (typeMatch == null) return $"Class '{className}' not found.";

        var lineSpan = typeMatch.GetLocation().GetLineSpan();
        return $"// File: {filePath}\n// Starts at line: {lineSpan.StartLinePosition.Line + 1}\n{typeMatch.ToFullString()}";
    }

    public static List<(string TypeName, string MemberName, string MemberType)> ExtractAllMembers(string filePath)
    {
        var result = new List<(string TypeName, string MemberName, string MemberType)>();
        try
        {
            if (!File.Exists(filePath) || new FileInfo(filePath).Length > MaxFileSize) return result;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var code = reader.ReadToEnd();

            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetCompilationUnitRoot();

            foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeName = GetFullTypeName(type);
                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                    result.Add((typeName, method.Identifier.Text, "Method"));
                foreach (var prop in type.Members.OfType<PropertyDeclarationSyntax>())
                    result.Add((typeName, prop.Identifier.Text, "Property"));
                foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var variable in field.Declaration.Variables)
                        result.Add((typeName, variable.Identifier.Text, "Field"));
                }
                foreach (var evt in type.Members.OfType<EventFieldDeclarationSyntax>())
                {
                    foreach (var variable in evt.Declaration.Variables)
                        result.Add((typeName, variable.Identifier.Text, "Event"));
                }
            }
        }
        catch { }
        return result;
    }
}