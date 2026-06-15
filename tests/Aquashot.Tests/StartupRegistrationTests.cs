using FluentAssertions;
using Aquashot.Settings;
using Xunit;

namespace Aquashot.Tests;

public class StartupRegistrationTests
{
    [Fact]
    public void EnableThenDisable_TogglesIsEnabled()
    {
        var reg = new StartupRegistration("AquashotTest_" + Guid.NewGuid().ToString("N"), @"C:\fake\Aquashot.exe");
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
