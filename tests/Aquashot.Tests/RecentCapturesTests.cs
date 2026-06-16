using System;
using System.IO;
using FluentAssertions;
using Aquashot.History;
using Xunit;

namespace Aquashot.Tests;

public class RecentCapturesTests
{
    private static string Tmp() =>
        Path.Combine(Path.GetTempPath(), "aqua-recent-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Add_puts_newest_first_and_dedupes()
    {
        var p = Tmp();
        try
        {
            var r = new RecentCaptures(p, cap: 5);
            r.Add("a.png");
            r.Add("b.png");
            r.Add("a.png"); // moves a.png back to front, no duplicate
            r.Items.Should().Equal("a.png", "b.png");
        }
        finally { if (File.Exists(p)) File.Delete(p); }
    }

    [Fact]
    public void Caps_to_limit_keeping_newest()
    {
        var p = Tmp();
        try
        {
            var r = new RecentCaptures(p, cap: 3);
            foreach (var f in new[] { "1", "2", "3", "4", "5" }) r.Add(f);
            r.Items.Should().Equal("5", "4", "3");
        }
        finally { if (File.Exists(p)) File.Delete(p); }
    }

    [Fact]
    public void Persists_across_instances()
    {
        var p = Tmp();
        try
        {
            new RecentCaptures(p).Add("x.png");
            new RecentCaptures(p).Items.Should().Contain("x.png");
        }
        finally { if (File.Exists(p)) File.Delete(p); }
    }
}
