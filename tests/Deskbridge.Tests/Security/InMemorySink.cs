using System.Collections.Concurrent;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Deskbridge.Tests.Security;

/// <summary>
/// Serilog in-memory sink used by <see cref="PasswordLeakTests"/> and
/// <see cref="ErrorSanitizationTests"/> to capture log events and assert the absence
/// of credential / exception-message fragments.
///
/// <para>
/// Each captured <see cref="LogEvent"/> is also rendered to a flat string via a
/// <c>MessageTemplateTextFormatter</c> so the tests can regex/substring-match the same
/// text Serilog's real file sink would emit. Both the raw events and the flattened
/// text are available to assertions.
/// </para>
/// </summary>
public sealed class InMemorySink : ILogEventSink
{
    private readonly ConcurrentBag<LogEvent> _events = new();
    private readonly ConcurrentBag<string> _rendered = new();
    private readonly ITextFormatter _formatter =
        new MessageTemplateTextFormatter("{Level:u3} {Message:lj} {Exception}", null);

    public IReadOnlyCollection<LogEvent> Events => _events;
    public IReadOnlyCollection<string> Rendered => _rendered;

    public void Emit(LogEvent logEvent)
    {
        _events.Add(logEvent);
        using var sw = new StringWriter();
        _formatter.Format(logEvent, sw);
        _rendered.Add(sw.ToString());
    }

    /// <summary>
    /// True if any rendered log event (message + exception) contains the given substring.
    /// Case-sensitive; password values are case-sensitive too.
    /// </summary>
    public bool ContainsText(string needle) =>
        _rendered.Any(r => r.Contains(needle, StringComparison.Ordinal));
}
