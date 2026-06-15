using FluentAssertions;
using SnipTool.Settings;
using Xunit;

namespace SnipTool.Tests;

public class StartupRegistrationTests
{
    [Fact]
    public void EnableThenDisable_TogglesIsEnabled()
    {
        var reg = new StartupRegistration("SnipToolTest_" + Guid.NewGuid().ToString("N"), @"C:\fake\SnipTool.exe");
        try
        {
            reg.Enable();
            reg.IsEnabled().Should().BeTrue();
            reg.Disable();
            reg.IsEnabled().Should().BeFalse();
        }
        finally { reg.Disable(); }
    }
}
