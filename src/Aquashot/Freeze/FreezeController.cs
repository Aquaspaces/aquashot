using System;
using System.Collections.Generic;
using System.Linq;
using Aquashot.Capture;

namespace Aquashot.Freeze;

// Holds a frozen snapshot of every monitor on top of the live desktop. Toggling off
// (or any window being dismissed) tears them all down.
public class FreezeController
{
    private readonly List<FreezeWindow> _windows = new();

    public bool IsActive => _windows.Count > 0;
    public event Action? Resumed;

    public void Freeze(IReadOnlyList<CapturedFrame> frames)
    {
        if (IsActive) return;
        bool firstHint = true;
        foreach (var f in frames)
        {
            var w = new FreezeWindow(f, showHint: firstHint);
            firstHint = false;
            w.Dismissed += Resume;
            _windows.Add(w);
            w.Show();
        }
    }

    public void Resume()
    {
        if (!IsActive) return;
        foreach (var w in _windows.ToList()) w.Close();
        _windows.Clear();
        Resumed?.Invoke();
    }
}
