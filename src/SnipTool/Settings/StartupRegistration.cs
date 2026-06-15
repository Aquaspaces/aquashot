using Microsoft.Win32;

namespace SnipTool.Settings;

public class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _name;
    private readonly string _exePath;

    public StartupRegistration(string name, string exePath)
    {
        _name = name;
        _exePath = exePath;
    }

    public bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return k?.GetValue(_name) is not null;
    }

    public void Enable()
    {
        using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
        k.SetValue(_name, $"\"{_exePath}\"");
    }

    public void Disable()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (k?.GetValue(_name) is not null) k.DeleteValue(_name);
    }
}
