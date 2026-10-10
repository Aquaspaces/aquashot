using Aquashot.Settings;
using FluentAssertions;

namespace Aquashot.Tests;

public class SettingsWindowTests
{
    [Theory]
    [InlineData("NaN", 2)]
    [InlineData("Infinity", 2)]
    [InlineData("-Infinity", 2)]
    [InlineData("1e999", 2)]
    [InlineData("0.4", 0.4)]
    [InlineData("20", 10)]
    public void NumericSettings_KeepFiniteValues(string text, double expected)
    {
        var parse = typeof(SettingsWindow).GetMethod("ParseClampD",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        parse.Invoke(null, new object[] { text, 2.0, 0.0, 10.0 }).Should().Be(expected);
    }
}
