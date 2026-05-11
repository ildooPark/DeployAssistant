using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using DeployAssistant.CLI.Engine;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class IntegrityRunScreen : Screen
{
    private readonly MetaDataManager _mgr;
    private List<ProjectFile> _result = new List<ProjectFile>();
    private bool _completed;

    public IntegrityRunScreen(MetaDataManager mgr) { _mgr = mgr; }

    public override void OnEnter()
    {
        if (_completed) return;

        var captured = new List<ProjectFile>();
        using var done = new ManualResetEventSlim(false);

        // Actual signature: Action<string, ObservableCollection<ProjectFile>>
        // First param is changeLog (string), second is the file collection.
        void OnComplete(string _, ObservableCollection<ProjectFile> files)
        {
            lock (captured)
            {
                captured.Clear();
                captured.AddRange(files);
            }
            done.Set();
        }

        void OnState(MetaDataState s)
        {
            if (s == MetaDataState.Idle) done.Set();
        }

        int totalKnown = 0;
        var progressLock = new object();
        ProgressTask? activeTask = null;

        void OnProgress(int completed, int total)
        {
            lock (progressLock)
            {
                if (activeTask is null) return;
                if (totalKnown == 0)
                {
                    totalKnown = total;
                    activeTask.MaxValue = total;
                }
                if (completed > activeTask.Value)
                    activeTask.Value = completed;
            }
        }

        _mgr.IntegrityCheckCompleteEventHandler += OnComplete;
        _mgr.ManagerStateEventHandler += OnState;
        _mgr.IntegrityProgressEventHandler += OnProgress;
        try
        {
            AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                })
                .Start(ctx =>
                {
                    lock (progressLock)
                    {
                        activeTask = ctx.AddTask("[cyan]Integrity check[/]", new ProgressTaskSettings { AutoStart = true, MaxValue = 1 });
                    }
                    _mgr.RequestProjectIntegrityCheck();
                    done.Wait(TimeSpan.FromMinutes(10));
                    lock (progressLock)
                    {
                        if (activeTask is not null && totalKnown > 0)
                            activeTask.Value = totalKnown; // ensure 100% on completion
                    }
                });

            lock (captured) { _result = new List<ProjectFile>(captured); }
            _completed = true;
        }
        finally
        {
            _mgr.IntegrityCheckCompleteEventHandler -= OnComplete;
            _mgr.ManagerStateEventHandler -= OnState;
            _mgr.IntegrityProgressEventHandler -= OnProgress;
        }
    }

    public override void Render()
    {
        // Progress owns the console during OnEnter; AutoAdvance fires immediately after.
    }

    public override ScreenAction? AutoAdvance() =>
        new ScreenAction.Replace(new IntegrityResultScreen(_mgr, _result));

    public override ScreenAction Handle(ConsoleKeyInfo key) => ScreenAction.StayAction;
}
