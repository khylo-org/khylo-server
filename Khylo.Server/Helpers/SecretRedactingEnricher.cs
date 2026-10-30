using System.Linq;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Khylo.Server.Helpers;

/// <summary>
/// Keeps people's access keys out of the log: any key or token that appears in a logged address or header is
/// replaced before the line is written.
/// </summary>
public sealed partial class SecretRedactingEnricher : ILogEventEnricher
{
    // ApiKey=..., api_key=..., token=..., X-Emby-Token: ..., MediaBrowser Token="..."
    [GeneratedRegex("""(?<name>api[_-]?key|access[_-]?token|token)(?<sep>\s*[=:]\s*|%3D)(?<quote>["']?)(?<value>[^&\s"',;]{6,})""", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            var cleaned = Clean(property.Value);
            if (!ReferenceEquals(cleaned, property.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, cleaned));
            }
        }
    }

    private static LogEventPropertyValue Clean(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text } when SecretPattern().IsMatch(text):
                return new ScalarValue(SecretPattern().Replace(text, "${name}${sep}${quote}[hidden]"));
            case SequenceValue sequence:
                var items = sequence.Elements.Select(Clean).ToList();
                return items.Zip(sequence.Elements, (a, b) => ReferenceEquals(a, b)).All(same => same) ? value : new SequenceValue(items);
            default:
                return value;
        }
    }
}
