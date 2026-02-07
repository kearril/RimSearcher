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
        catch { return new Dictionary<string, string?>(); }
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
        if (!File.Exists(filePath)) return "文件不存在";
        
        // 限制处理文件的大小，防止在解析极大型源码文件时消耗过量内存。
        if (new FileInfo(filePath).Length > MaxFileSize) return $"文件过大 (超过 2MB)，已跳过解析。";
        
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

            string kind = type switch {
                ClassDeclarationSyntax => "Class",
                StructDeclarationSyntax => "Struct",
                InterfaceDeclarationSyntax => "Interface",
                RecordDeclarationSyntax => "Record",
                _ => "Type"
            };

            sb.AppendLine($"{kind}: {fullName} {type.TypeParameterList}");
            foreach (var prop in type.Members.OfType<PropertyDeclarationSyntax>()) sb.AppendLine($"  Property: {prop.Type} {prop.Identifier.Text}");
            foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
            {
                var fieldName = string.Join(", ", field.Declaration.Variables.Select(v => v.Identifier.Text));
                sb.AppendLine($"  Field: {field.Declaration.Type} {fieldName}");
            }
            foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
            {
                var parameters = string.Join(", ", method.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.Text}"));
                sb.AppendLine($"  Method: {method.ReturnType} {method.Identifier.Text}({parameters})");
            }
            sb.AppendLine();
        }

        return sb.Length > 0 ? sb.ToString() : (targetTypeName != null ? $"在文件中未找到类型: {targetTypeName}" : "文件中未找到任何类型定义。");
    }

    public static async Task<string> GetMethodBodyAsync(string filePath, string methodName, string? typeName = null)
    {
        if (!File.Exists(filePath)) return "文件不存在";
        if (new FileInfo(filePath).Length > MaxFileSize) return $"文件过大 (超过 2MB)，已跳过读取。";
        
        string code;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            code = await reader.ReadToEndAsync();
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        var matches = methods.Where(m => m.Identifier.Text.Equals(methodName, StringComparison.OrdinalIgnoreCase));
        
        if (!string.IsNullOrEmpty(typeName))
        {
            matches = matches.Where(m => 
            {
                var parentType = m.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                return parentType != null && (
                    parentType.Identifier.Text.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                    GetFullTypeName(parentType).Equals(typeName, StringComparison.OrdinalIgnoreCase)
                );
            });
        }

        var resultList = matches.ToList();
        if (resultList.Count == 0) return $"未找到方法: {(string.IsNullOrEmpty(typeName) ? "" : typeName + ".")}{methodName}";
        
        if (resultList.Count == 1)
        {
            var m = resultList[0];
            var lineSpan = m.GetLocation().GetLineSpan();
            return $"// File: {filePath}\n// Starts at line: {lineSpan.StartLinePosition.Line + 1}\n{m.ToFullString()}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"/* 发现 {resultList.Count} 个匹配的方法重载或冲突 */");
        foreach (var m in resultList)
        {
            var lineSpan = m.GetLocation().GetLineSpan();
            var parentType = m.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            sb.AppendLine($"// Type: {(parentType != null ? GetFullTypeName(parentType) : "Unknown")}");
            sb.AppendLine($"// Starts at line: {lineSpan.StartLinePosition.Line + 1}");
            sb.AppendLine(m.ToFullString());
            sb.AppendLine("\n// --- 下一个匹配 ---\n");
        }
        return sb.ToString();
    }
}