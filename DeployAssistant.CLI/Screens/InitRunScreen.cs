using System;
using System.Collections.Generic;
using System.Threading;
using DeployAssistant.CLI.Engine;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class InitRunScreen : Screen
{
    private readonly string _path;
    private MetaDataManager? _mgr;
    private string? _error;

    public InitRunScreen(string path) { _path = path; }

    public override void OnEnter()
    {
        if (_mgr is not null || _error is not null) return;

        var mgr = ManagerFactory.Create();
        using var done = new ManualResetEventSlim(false);
        void OnState(MetaDataState s) { if (s == MetaDataState.Idle) done.Set(); }
        mgr.ManagerStateEventHandler += OnState;
        try
        {
            bool completedInTime = false;
            AnsiConsole.Status()
                .Spinner(Term.Unicode ? Spinner.Known.Dots : Spinner.Known.Ascii)
                .SpinnerStyle(Style.Parse("aqua"))
                .Start($"[aqua]Initializing project at[/] {Markup.Escape(Ui.FitPath(_path, Ui.Width - 30))}", _ =>
                {
                    mgr.RequestProjectInitialization(_path);
                    completedInTime = done.Wait(TimeSpan.FromMinutes(5));
                });

            if (!completedInTime)
            {
                _error = $"Initialization timed out after 5 minutes for {_path}.";
                return;
            }

            if (mgr.ProjectMetaData is null)
            {
                _error = $"Failed to initialize project at {_path}.";
                return;
            }

            _mgr = mgr;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            mgr.ManagerStateEventHandler -= OnState;
        }
    }

    public override string Title => "Initialize";

    public override IReadOnlyList<KeyHint> Hints =>
        _error is not null ? new[] { new KeyHint("any key", "back") } : Array.Empty<KeyHint>();

    public override void Render()
    {
        if (_error is not null)
            Ui.Box("Initialization failed", new[] { $"[red]{Markup.Escape(_error)}[/]" }, Color.Red, "red bold");
    }

    public override ScreenAction? AutoAdvance() =>
        _error is null && _mgr is not null
            ? new ScreenAction.Replace(new MainScreen(_mgr))
            : null;

    public override ScreenAction Handle(ConsoleKeyInfo key) =>
        _error is not null ? ScreenAction.PopAction : ScreenAction.StayAction;
}
