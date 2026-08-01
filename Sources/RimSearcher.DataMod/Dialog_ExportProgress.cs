using System;
using System.Threading;
using UnityEngine;
using Verse;

namespace RimSearcher.DataMod;

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
                progress: (current, total) =>
                {
                    _current = current;
                    _total = total;
                });
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Verse.Log.Error($"[RimSearcher] Export failed: {ex}");
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
        Widgets.Label(new Rect(ContentMargin, y, width, 36f), "RimSearcher.DialogTitle".Translate());
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
                    Widgets.Label(new Rect(ContentMargin, y, width, 22f), "RimSearcher.Elapsed".Translate(FormatTime(elapsed)));
                }
                else
                {
                    var eta = TimeSpan.FromTicks((long)(elapsed.Ticks / pct - elapsed.Ticks));
                    Widgets.Label(new Rect(ContentMargin, y, width, 22f), "RimSearcher.ElapsedEta".Translate(FormatTime(elapsed), FormatTime(eta)));
                }
                y += 28f;
            }
            else
            {
                y += 6f;
            }
        }

        if (done)
        {
            Widgets.Label(new Rect(ContentMargin, y, width, 28f), _error != null
                ? "RimSearcher.ExportFailed".Translate()
                : "RimSearcher.ExportDone".Translate());
            y += 40f;

            if (_error != null)
            {
                Widgets.Label(new Rect(ContentMargin, y, width, 24f), "RimSearcher.Error".Translate(_error));
                y += 32f;
            }

            float buttonX = (inRect.width - CloseButtonWidth) / 2f;
            if (Widgets.ButtonText(new Rect(buttonX, y, CloseButtonWidth, 42f), "RimSearcher.Close".Translate()))
            {
                Close();
            }
        }
        else
        {
            // 进度满但线程未结束 = 索引构建/提交阶段，柔和蓝提示区别于导出中。
            bool finishing = _total > 0 && _current >= _total;
            var originalColor = GUI.color;
            if (finishing)
                GUI.color = ColorLibrary.BabyBlue;
            Widgets.Label(new Rect(ContentMargin, y, width, 28f),
                (finishing ? "RimSearcher.BuildingIndex" : "RimSearcher.ExportingWarning").Translate());
            GUI.color = originalColor;
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
