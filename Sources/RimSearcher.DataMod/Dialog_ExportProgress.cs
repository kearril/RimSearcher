using System;
using System.Threading;
using UnityEngine;
using Verse;

namespace RimSearcher.DataMod;

/// <summary>
/// 在后台线程生成数据库期间显示导出进度。
/// </summary>
public class Dialog_ExportProgress : Window
{
    private const float ContentMargin = 20f;
    private const float CloseButtonWidth = 160f;

    private readonly string _dbPath;
    private readonly Thread _thread;
    private readonly long _startTicks;

    // 以下字段由后台导出线程写入、主线程每帧读取，需 volatile 保证可见性。
    private volatile int _current;
    private volatile int _total;
    private volatile string _status = "准备中...";
    private volatile string? _error;
    private long _endTicks;

    public override Vector2 InitialSize => new(560f, 330f);

    public Dialog_ExportProgress(string dbPath)
    {
        _dbPath = dbPath;
        _startTicks = DateTime.UtcNow.Ticks;
        doCloseButton = false;
        doCloseX = false;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnAccept = false;
        closeOnCancel = false;
        closeOnClickedOutside = false;

        _thread = new Thread(RunExport)
        {
            IsBackground = true,
            Name = "RimSearcherExport"
        };
        _thread.Start();
    }

    private void RunExport()
    {
        try
        {
            DefExporter.Export(_dbPath,
                log: msg => Verse.Log.Message($"[RimSearcher] {msg}"),
                progress: (current, total, status) =>
                {
                    _current = current;
                    _total = total;
                    _status = status;
                });
            _status = "导出完成!";
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _status = "导出失败";
            Verse.Log.Error($"[RimSearcher] 导出失败: {ex}");
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        bool done = !_thread.IsAlive;
        if (done && _endTicks == 0)
            _endTicks = DateTime.UtcNow.Ticks;

        // 导出期间吞掉键盘事件，防止按键泄漏到游戏。
        if (!done && Event.current != null && Event.current.isKey)
        {
            Event.current.Use();
        }

        float width = inRect.width - ContentMargin * 2f;
        float y = inRect.y + ContentMargin;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(ContentMargin, y, width, 36f), "导出 Def 数据库");
        Text.Font = GameFont.Small;
        y += 48f;

        if (_total > 0)
        {
            float pct = Mathf.Clamp01((float)_current / _total);
            Widgets.Label(new Rect(ContentMargin, y, width, 24f), $"{_current:N0} / {_total:N0} ({pct:P0})");
            y += 30f;
            Widgets.FillableBar(new Rect(ContentMargin, y, width, 36f), pct);
            y += 44f;

            if (_current > 0)
            {
                var elapsed = GetElapsed();
                if (done)
                {
                    // 导出已结束：只显示实际导出用时，不再显示预计剩余。
                    Widgets.Label(new Rect(ContentMargin, y, width, 22f), $"已用: {FormatTime(elapsed)}");
                }
                else
                {
                    var eta = TimeSpan.FromTicks((long)(elapsed.Ticks / pct - elapsed.Ticks));
                    Widgets.Label(new Rect(ContentMargin, y, width, 22f), $"已用: {FormatTime(elapsed)}  预计剩余: {FormatTime(eta)}");
                }
                y += 28f;
            }
            else
            {
                y += 6f;
            }
        }

        Widgets.Label(new Rect(ContentMargin, y, width, 28f), _status);
        y += 40f;

        if (done)
        {
            if (_error != null)
            {
                Widgets.Label(new Rect(ContentMargin, y, width, 24f), $"错误: {_error}");
                y += 32f;
            }

            float buttonX = (inRect.width - CloseButtonWidth) / 2f;
            if (Widgets.ButtonText(new Rect(buttonX, y, CloseButtonWidth, 42f), "关闭"))
            {
                Close();
            }
        }
        else
        {
            Widgets.Label(new Rect(ContentMargin, y, width, 24f), "正在导出，请勿关闭此窗口...");
        }
    }

    private TimeSpan GetElapsed()
    {
        long elapsedTicks = _endTicks != 0
            ? _endTicks - _startTicks
            : DateTime.UtcNow.Ticks - _startTicks;
        return TimeSpan.FromTicks(elapsedTicks);
    }

    private static string FormatTime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Seconds}s";
    }
}
