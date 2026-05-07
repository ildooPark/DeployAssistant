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

        _mgr.IntegrityCheckCompleteEventHandler += OnComplete;
        _mgr.ManagerStateEventHandler += OnState;
        try
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start("[cyan]Running integrity check…[/]", _ =>
                {
                    _mgr.RequestProjectIntegrityCheck();
                    done.Wait(TimeSpan.FromMinutes(10));
                });

            lock (captured) { _result = new List<ProjectFile>(captured); }
            _completed = true;
        }
        finally
        {
            _mgr.IntegrityCheckCompleteEventHandler -= OnComplete;
            _mgr.ManagerStateEventHandler -= OnState;
        }
    }

    public override void Render()
    {
        // Status owns the console during OnEnter; AutoAdvance fires immediately after.
    }

    public override ScreenAction? AutoAdvance() =>
        new ScreenAction.Replace(new IntegrityResultScreen(_result));

    public override ScreenAction Handle(ConsoleKeyInfo key) => ScreenAction.StayAction;
}
