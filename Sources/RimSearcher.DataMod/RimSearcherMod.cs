using System.Diagnostics;
using UnityEngine;
using Verse;

namespace RimSearcher.DataMod;

/// <summary>
/// 提供 RimSearcher 设置页并启动 Def 数据库导出。
/// </summary>
public class RimSearcherMod : Mod
{
    private const float ButtonWidth = 200f;
    private const float ButtonHeight = 36f;
    private string _exportPath;

    public RimSearcherMod(ModContentPack content) : base(content)
    {
        _exportPath = Path.Combine(content.RootDir, "defs.db");
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        float y = inRect.y;

        Widgets.Label(new Rect(0f, y, inRect.width, 24f), "导出路径:");
        y += 26f;

        _exportPath = Widgets.TextField(new Rect(0f, y, inRect.width, 28f), _exportPath);
        y += 42f;

        if (Widgets.ButtonText(new Rect(0f, y, ButtonWidth, ButtonHeight), "在资源管理器中打开"))
            OpenInExplorer();
        y += ButtonHeight + 6f;

        if (Widgets.ButtonText(new Rect(0f, y, ButtonWidth, ButtonHeight), "导出 Def 数据库"))
        {
            var path = ResolveExportPath(_exportPath);
            Find.WindowStack.Add(new Dialog_ExportProgress(path));
        }
    }

    private static string ResolveExportPath(string exportPath) =>
        Directory.Exists(exportPath) ? Path.Combine(exportPath, "defs.db") : exportPath;

    private void OpenInExplorer()
    {
        try
        {
            var dir = Path.GetDirectoryName(_exportPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", "/select,\"" + _exportPath + "\"");
            else if (Directory.Exists(_exportPath))
                Process.Start("explorer.exe", _exportPath);
        }
        catch (Exception ex)
        {
            Verse.Log.Error($"[RimSearcher] 打开资源管理器失败: {ex}");
        }
    }

    public override string SettingsCategory() => "RimSearcher";
}
