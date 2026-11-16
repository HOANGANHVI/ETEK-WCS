using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using WCS.Api.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Logging;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc;

namespace WCS.Tests.Logging;

/// <summary>CR-007 logging core: secret masking, repeat throttling, PLC event classification and the PLC -> application log bridge.</summary>
public class LoggingCoreTests
{
    // ------------------------------------------------------------------------------------------------------------- sanitizer
    [Theory]
    [InlineData("Host=db;Port=5432;Database=wcs;Username=wcs;Password=S3cr3t!x;Pooling=true", "S3cr3t!x")]
    [InlineData("{\"password\":\"hunter2hunter\",\"user\":\"a\"}", "hunter2hunter")]
    [InlineData("login failed token=abc.def.ghi extra", "abc.def.ghi")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("Cookie: wcs.auth=CfDJ8Zsecretcookievalue", "CfDJ8Zsecretcookievalue")]
    [InlineData("POSTGRES_PASSWORD=SuperLongRandom123 was empty", "SuperLongRandom123")]
    [InlineData("ConnectionString = 'Host=x;Password=pw12345'", "pw12345")]
    public void Sanitizer_masks_secret_values_but_keeps_the_key_names(string input, string secret)
    {
        var output = LogSanitizer.Redact(input)!;
        Assert.DoesNotContain(secret, output);
        Assert.Contains(LogSanitizer.Mask, output);
        Assert.False(LogSanitizer.LooksSecret(output));
    }

    [Fact]
    public void Sanitizer_leaves_ordinary_text_alone_and_handles_null_and_long_input()
    {
        const string plain = "PLC-01 Faulted - Khong ket noi duoc toi PLC. no response within 500 ms, end code 0x4031";
        Assert.Equal(plain, LogSanitizer.Redact(plain));
        Assert.Null(LogSanitizer.Redact(null)); Assert.Equal("", LogSanitizer.Redact(""));
        var big = new string('x', 20_000);
        Assert.True(LogSanitizer.Redact(big, 100)!.Length < 200);
    }

    // ------------------------------------------------------------------------------------------------------------- throttling
    private sealed class Collect : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent e) => Events.Add(e);
    }

    private static LogEvent Event(LogEventLevel level, string template, Exception? ex = null, params (string, object)[] props)
    {
        var parser = new MessageTemplateParser();
        return new LogEvent(DateTimeOffset.UtcNow, level, ex, parser.Parse(template),
            props.Select(p => new LogEventProperty(p.Item1, new ScalarValue(p.Item2))).Append(new LogEventProperty("SourceContext", new ScalarValue("T"))));
    }

    [Fact]
    public void Repeated_errors_are_throttled_and_the_next_one_reports_how_many_were_suppressed()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var sink = new Collect(); var t = new ThrottlingSink(sink, TimeSpan.FromSeconds(60), () => now);
        var boom = new InvalidOperationException("database is down");

        for (var i = 0; i < 5; i++) { t.Emit(Event(LogEventLevel.Error, "Scheduler tick failed", boom)); now = now.AddSeconds(1); }
        Assert.Single(sink.Events);                                                           // first passes, 4 suppressed

        now = now.AddSeconds(60);
        t.Emit(Event(LogEventLevel.Error, "Scheduler tick failed", boom));
        Assert.Equal(2, sink.Events.Count);
        Assert.Equal(4, ((ScalarValue)sink.Events[1].Properties["SuppressedRepeats"]).Value);
        Assert.Contains("suppressed", sink.Events[1].RenderMessage());

        t.Emit(Event(LogEventLevel.Error, "Scheduler tick failed", new InvalidOperationException("a different failure")));
        Assert.Equal(3, sink.Events.Count);                                                   // different cause is never hidden
    }

    [Fact]
    public void Information_plc_events_and_noexception_distinct_messages_are_not_throttled()
    {
        var sink = new Collect(); var t = new ThrottlingSink(sink, TimeSpan.FromSeconds(60), () => DateTimeOffset.UnixEpoch);
        for (var i = 0; i < 20; i++) t.Emit(Event(LogEventLevel.Information, "Request"));
        for (var i = 0; i < 6; i++) t.Emit(Event(LogEventLevel.Error, "PLC {PlcId} {EventCode}", null, ("Category", "PLC"), ("PlcId", "PLC-01"), ("EventCode", "WRITE_AMBIGUOUS")));
        for (var i = 0; i < 3; i++) t.Emit(Event(LogEventLevel.Warning, "Station request {Key} rejected", null, ("Key", "k" + i)));
        Assert.Equal(20 + 6 + 3, sink.Events.Count);   // distinct rendered messages are never hidden
    }

    [Fact]
    public void A_failing_inner_sink_never_breaks_the_caller_and_composite_continues_with_the_other_sinks()
    {
        var good = new Collect();
        var composite = new CompositeSink([new Boom(), good]);
        composite.Emit(Event(LogEventLevel.Error, "x"));
        Assert.Single(good.Events);
        var t = new ThrottlingSink(new Boom(), TimeSpan.FromSeconds(1));
        t.Emit(Event(LogEventLevel.Error, "x"));    // must not throw
    }

    private sealed class Boom : ILogEventSink { public void Emit(LogEvent logEvent) => throw new InvalidOperationException("sink failed"); }

    [Fact]
    public void Request_log_level_does_not_report_handled_4xx_as_errors_and_hides_probes()
    {
        static HttpContext Ctx(string path, int status) { var c = new DefaultHttpContext(); c.Request.Path = path; c.Response.StatusCode = status; return c; }
        Assert.Equal(LogEventLevel.Information, LogSetup.RequestLevel(Ctx("/api/auth/login", 401), 1, null));
        Assert.Equal(LogEventLevel.Information, LogSetup.RequestLevel(Ctx("/api/tasks", 409), 1, null));
        Assert.Equal(LogEventLevel.Error, LogSetup.RequestLevel(Ctx("/api/tasks", 500), 1, null));
        Assert.Equal(LogEventLevel.Error, LogSetup.RequestLevel(Ctx("/api/tasks", 200), 1, new Exception("x")));
        Assert.Equal(LogEventLevel.Debug, LogSetup.RequestLevel(Ctx("/healthz", 200), 1, null));
        Assert.Equal(LogEventLevel.Debug, LogSetup.RequestLevel(Ctx("/healthz", 503), 1, null));   // DB down is reported by the monitor, not by every probe
    }

    // ------------------------------------------------------------------------------------------------------------- PLC classification
    [Fact]
    public void Enricher_extracts_state_change_reconnect_timeout_and_end_code()
    {
        var down = PlcEventEnricher.Build("PLC-01", "STATE", "PLC-01: Faulted - Mat ket noi PLC.", "192.168.10.101", 5002, "Connected", null);
        Assert.Equal("Connected", down.PreviousState); Assert.Equal("Faulted", down.NewState); Assert.Equal("Mat ket noi PLC.", down.Reason);
        Assert.Equal("192.168.10.101", down.Ip); Assert.Equal(5002, down.Port);
        Assert.Contains("Connected -> Faulted", down.Context()); Assert.Contains("192.168.10.101:5002", down.Context());

        var up = PlcEventEnricher.Build("PLC-01", "STATE", "PLC-01: Connected", null, null, "Faulted", 42.4);
        Assert.Equal("RECONNECTED", up.Kind); Assert.Contains("42 s", up.Reason);

        var timeout = PlcEventEnricher.Build("PLC-02", "READ_BAD_B1", "2 IO point doc loi: no response within 500 ms", null, null, null, null);
        Assert.Equal("TIMEOUT", timeout.Kind);

        var endCode = PlcEventEnricher.Build("PLC-02", "READ_BAD_B1", "1 IO point doc loi: end code 0x4031", null, null, null, null);
        Assert.Equal("END_CODE", endCode.Kind); Assert.Equal("0x4031", endCode.EndCode);

        Assert.Equal("WRITE_AMBIGUOUS", PlcEventEnricher.Build("PLC-03", "WRITE_AMBIGUOUS", "X1/Request_Ack: ...", null, null, null, null).Kind);
        Assert.Equal("CONNECTION", PlcEventEnricher.Build("PLC-04", "LINK_LOST", "Mat ket noi: reset", null, null, null, null).Kind);
        Assert.True(PlcEventEnricher.Build("PLC-04", "RELOAD", "reloaded", null, null, null, null).IsEmpty);
    }

    // ------------------------------------------------------------------------------------------------------------- PLC -> application log bridge
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text, Dictionary<string, object?> Scope)> Lines { get; } = [];
        private Dictionary<string, object?> _scope = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            _scope = state is IEnumerable<KeyValuePair<string, object?>> kv ? kv.ToDictionary(x => x.Key, x => x.Value) : [];
            return new Noop();
        }
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception), _scope));
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-01-01T00:00:00Z"); }

    [Fact]
    public void Plc_events_reach_the_application_log_with_category_state_change_and_cause()
    {
        var logger = new CapturingLogger<PlcEventDispatcher>(); var clock = new FixedClock();
        using var d = new PlcEventDispatcher(new ServiceCollection().BuildServiceProvider(), clock, logger);

        d.Log(PlcLogLevel.Info, "PLC-01", null, "STATE", "PLC-01: Connected");
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        d.Log(PlcLogLevel.Warning, "PLC-01", null, "STATE", "PLC-01: Faulted - Mat ket noi PLC.");
        d.Log(PlcLogLevel.Warning, "PLC-01", "A1", "READ_BAD_A1", "2 IO point doc loi: no response within 500 ms");
        clock.UtcNow = clock.UtcNow.AddSeconds(12);
        d.Log(PlcLogLevel.Info, "PLC-01", null, "STATE", "PLC-01: Connected");
        d.Log(PlcLogLevel.Error, "PLC-02", "B1", "WRITE_AMBIGUOUS", "B1/Request_Ack: khong nhan duoc xac nhan");

        Assert.Equal(5, logger.Lines.Count);
        Assert.All(logger.Lines, l => Assert.Equal("PLC", l.Scope["Category"]));
        var fault = logger.Lines[1];
        Assert.Equal(LogLevel.Warning, fault.Level); Assert.Contains("Connected -> Faulted", fault.Text); Assert.Equal("Connected", fault.Scope["PreviousState"]); Assert.Equal("Faulted", fault.Scope["NewState"]);
        Assert.Equal("TIMEOUT", logger.Lines[2].Scope["Kind"]); Assert.Equal("A1", logger.Lines[2].Scope["Station"]);
        Assert.Contains("RECONNECTED", logger.Lines[3].Text); Assert.Contains("12 s", logger.Lines[3].Text);
        Assert.Equal(LogLevel.Error, logger.Lines[4].Level); Assert.Equal("WRITE_AMBIGUOUS", logger.Lines[4].Scope["EventCode"]);
    }

    [Fact]
    public void A_failing_application_logger_never_breaks_the_plc_event_path()
    {
        using var d = new PlcEventDispatcher(new ServiceCollection().BuildServiceProvider(), new FixedClock(), new ThrowingLogger());
        d.Log(PlcLogLevel.Error, "PLC-01", null, "WORKER_CRASH", "boom");     // must not throw
        d.Publish("PlcStatusChanged", new { plcId = "PLC-01" });
    }

    private sealed class ThrowingLogger : ILogger<PlcEventDispatcher>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => throw new InvalidOperationException("scope failed");
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("log failed");
    }
}
