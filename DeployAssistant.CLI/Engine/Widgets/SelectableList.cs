using System;

namespace DeployAssistant.CLI.Engine.Widgets;

internal sealed class SelectableList
{
    private int _itemCount;
    private int _viewportHeight;

    public SelectableList(int itemCount, int viewportHeight)
    {
        _itemCount = Math.Max(0, itemCount);
        _viewportHeight = Math.Max(1, viewportHeight);
    }

    public int ItemCount => _itemCount;
    public int ViewportHeight => _viewportHeight;
    public int SelectedIndex { get; private set; }
    public int ViewportTop { get; private set; }

    public void SetItemCount(int count)
    {
        _itemCount = Math.Max(0, count);
        if (SelectedIndex >= _itemCount) SelectedIndex = Math.Max(0, _itemCount - 1);
        ClampViewport();
    }

    public void SetViewportHeight(int height)
    {
        _viewportHeight = Math.Max(1, height);
        ClampViewport();
    }

    public void Handle(ConsoleKeyInfo key)
    {
        if (_itemCount == 0) return;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Move(-1);
                return;
            case ConsoleKey.DownArrow:
                Move(+1);
                return;
            case ConsoleKey.Home:
                SelectedIndex = 0;
                ClampViewport();
                return;
            case ConsoleKey.End:
                SelectedIndex = _itemCount - 1;
                ClampViewport();
                return;
            case ConsoleKey.PageDown:
                Move(_viewportHeight);
                return;
            case ConsoleKey.PageUp:
                Move(-_viewportHeight);
                return;
        }

        switch (key.KeyChar)
        {
            case 'd': Move(_viewportHeight / 2); return;
            case 'u': Move(-_viewportHeight / 2); return;
        }
    }

    private void Move(int delta)
    {
        SelectedIndex = Clamp(SelectedIndex + delta, 0, Math.Max(0, _itemCount - 1));
        ClampViewport();
    }

    private void ClampViewport()
    {
        if (SelectedIndex < ViewportTop)
            ViewportTop = SelectedIndex;
        else if (SelectedIndex >= ViewportTop + _viewportHeight)
            ViewportTop = SelectedIndex - _viewportHeight + 1;

        int maxTop = Math.Max(0, _itemCount - _viewportHeight);
        ViewportTop = Clamp(ViewportTop, 0, maxTop);
    }

    // Math.Clamp is .NET Standard 2.1+ / .NET Core 2.0+ — not available on net472. Polyfill.
    private static int Clamp(int value, int min, int max) =>
        value < min ? min : (value > max ? max : value);
}
