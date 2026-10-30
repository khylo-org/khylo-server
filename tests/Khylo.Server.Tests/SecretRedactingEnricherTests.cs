using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Khylo.Server.Helpers;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Khylo.Server.Tests;

public class SecretRedactingEnricherTests
{
    private static LogEvent EventWith(string name, string value)
    {
        var template = new MessageTemplateParser().Parse("Request {" + name + "}");
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            new[] { new LogEventProperty(name, new ScalarValue(value)) });
    }

    private static string Rendered(LogEvent e) => e.RenderMessage(CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("?ApiKey=0123456789abcdef0123456789abcdef&static=true")]
    [InlineData("?api_key=0123456789abcdef0123456789abcdef")]
    [InlineData("/Videos/1/stream?foo=1&ApiKey=0123456789abcdef0123456789abcdef&bar=2")]
    [InlineData("MediaBrowser Client=\"x\", Token=\"0123456789abcdef0123456789abcdef\"")]
    [InlineData("X-Emby-Token: 0123456789abcdef0123456789abcdef")]
    public void Keys_AreRemovedFromLoggedText(string text)
    {
        var e = EventWith("QueryString", text);

        new SecretRedactingEnricher().Enrich(e, null!);

        Assert.DoesNotContain("0123456789abcdef", Rendered(e), StringComparison.Ordinal);
        Assert.Contains("[hidden]", Rendered(e), StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryText_IsLeftAlone()
    {
        var e = EventWith("Path", "/Items/abc/Images/Primary?tag=123&maxWidth=300");

        new SecretRedactingEnricher().Enrich(e, null!);

        Assert.Contains("tag=123&maxWidth=300", Rendered(e), StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_OfText_AreCleanedToo()
    {
        var template = new MessageTemplateParser().Parse("Urls {Urls}");
        var list = new SequenceValue(new LogEventPropertyValue[] { new ScalarValue("a?ApiKey=0123456789abcdef"), new ScalarValue("fine") });
        var e = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, template, new[] { new LogEventProperty("Urls", list) });

        new SecretRedactingEnricher().Enrich(e, null!);

        Assert.DoesNotContain("0123456789abcdef", Rendered(e), StringComparison.Ordinal);
        Assert.Contains("fine", Rendered(e), StringComparison.Ordinal);
    }
}
