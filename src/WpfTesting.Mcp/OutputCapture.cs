using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace WpfTesting.Mcp;

public enum OutputSource
{
    Stdout,
    Stderr,
    Debug,
}

public sealed record OutputLine(long Seq, OutputSource Source, string Text);

/// <summary>
/// Rolling buffer of what the app under test writes: stdout, stderr and Debug/Trace output
/// (OutputDebugString). Safe to call from the reader threads and from tool calls.
/// </summary>
public sealed class OutputCapture(int capacity, ILogger logger) : IDisposable
{
    private readonly object _lock = new();
    private readonly LinkedList<OutputLine> _lines = new();
    private long _nextSeq = 1;
    private long _readCursor;
    private long _dropped;
    private DebugOutputListener? _debugListener;

    /// <summary>Why Debug/Trace output isn't being captured, or null when it is.</summary>
    public string? DebugCaptureProblem { get; private set; }

    public void Add(OutputSource source, string text)
    {
        lock (_lock)
        {
            _lines.AddLast(new OutputLine(_nextSeq++, source, text));
            while (_lines.Count > capacity)
            {
                _lines.RemoveFirst();
                _dropped++;
            }
        }
    }

    public void StartProcessReaders(Process process)
    {
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Add(OutputSource.Stdout, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Add(OutputSource.Stderr, e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>Starts listening for Debug.WriteLine/Trace output from one process. Records a reason when it can't.</summary>
    public void StartDebugCapture(int processId)
    {
        try
        {
            _debugListener = new DebugOutputListener(processId, text => Add(OutputSource.Debug, text), logger);
        }
        catch (Exception ex)
        {
            DebugCaptureProblem = ex is IOException or UnauthorizedAccessException
                ? "Debug.WriteLine/Trace output is not being captured because another debugger or tool (for example DebugView or Visual Studio) owns the Windows debug output channel."
                : $"Debug.WriteLine/Trace output is not being captured: {ex.Message}";
            logger.LogWarning(ex, "Could not start the debug output listener");
        }
    }

    /// <summary>Stops listening for Debug/Trace output. Already captured lines stay readable.</summary>
    public void StopDebugCapture()
    {
        Interlocked.Exchange(ref _debugListener, null)?.Dispose();
    }

    /// <summary>Reads lines not yet returned to the agent and advances the cursor.</summary>
    public OutputPage ReadNew(int maxLines, Regex? filter = null)
    {
        lock (_lock)
            return ReadAfter(_readCursor, maxLines, filter, advance: true);
    }

    /// <summary>Reads lines with a sequence number above <paramref name="afterSeq"/>. Moves the shared cursor to the end of what was returned.</summary>
    public OutputPage ReadSince(long afterSeq, int maxLines, Regex? filter = null)
    {
        lock (_lock)
            return ReadAfter(afterSeq, maxLines, filter, advance: true);
    }

    public void Dispose() => StopDebugCapture();

    private OutputPage ReadAfter(long afterSeq, int maxLines, Regex? filter, bool advance)
    {
        var matching = new List<OutputLine>();
        var remaining = 0;
        var lastSeen = afterSeq;
        foreach (var line in _lines)
        {
            if (line.Seq <= afterSeq)
                continue;
            if (filter is not null && !filter.IsMatch(line.Text))
            {
                if (matching.Count < maxLines)
                    lastSeen = line.Seq;
                continue;
            }
            if (matching.Count < maxLines)
            {
                matching.Add(line);
                lastSeen = line.Seq;
            }
            else
            {
                remaining++;
            }
        }

        // The oldest buffered line is the first thing we could have lost; tell the agent when it asked for something older.
        var oldest = _lines.First?.Value.Seq ?? _nextSeq;
        var lost = afterSeq + 1 < oldest ? oldest - afterSeq - 1 : 0;

        if (advance)
            _readCursor = Math.Max(_readCursor, remaining == 0 ? _nextSeq - 1 : lastSeen);
        return new OutputPage(matching, remaining, lastSeen, Math.Max(0, lost));
    }
}

public sealed record OutputPage(IReadOnlyList<OutputLine> Lines, int Remaining, long LastSeq, long Lost)
{
    public string Format()
    {
        var sb = new StringBuilder();
        if (Lost > 0)
            sb.AppendLine($"(Output buffer overflowed: {Lost} older line(s) are gone.)");
        foreach (var line in Lines)
            sb.AppendLine($"[{line.Seq}] [{line.Source.ToString().ToLowerInvariant()}] {line.Text}");
        if (Remaining > 0)
            sb.AppendLine($"... {Remaining} more line(s). Call get_app_output again to continue.");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Listens on the Windows debug output channel (the same one DebugView uses) and forwards what one process writes
/// with OutputDebugString, which is where Debug.WriteLine and Trace output end up. Only one listener can exist
/// per machine, so construction fails when something else already holds the channel.
/// </summary>
internal sealed class DebugOutputListener : IDisposable
{
    private const int BufferSize = 4096;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly EventWaitHandle _bufferReady;
    private readonly EventWaitHandle _dataReady;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();

    public DebugOutputListener(int processId, Action<string> onLine, ILogger logger)
    {
        _file = MemoryMappedFile.CreateNew("DBWIN_BUFFER", BufferSize);
        try
        {
            _view = _file.CreateViewAccessor();
            _bufferReady = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_BUFFER_READY", out var createdReady);
            _dataReady = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_DATA_READY", out var createdData);
            if (!createdReady || !createdData)
                throw new IOException("The debug output events already exist.");
        }
        catch
        {
            _file.Dispose();
            throw;
        }

        _thread = new Thread(() => Pump(processId, onLine, logger)) { IsBackground = true, Name = "dbwin-listener" };
        _thread.Start();
    }

    private void Pump(int processId, Action<string> onLine, ILogger logger)
    {
        var bytes = new byte[BufferSize - sizeof(int)];
        var pending = new StringBuilder();
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _bufferReady.Set();
                if (!_dataReady.WaitOne(200))
                    continue;

                var pid = _view.ReadInt32(0);
                if (pid != processId)
                    continue;

                _view.ReadArray(sizeof(int), bytes, 0, bytes.Length);
                var length = Array.IndexOf(bytes, (byte)0);
                var text = Encoding.Default.GetString(bytes, 0, length < 0 ? bytes.Length : length);

                // OutputDebugString messages usually end in a newline, but Debug.Write can split a line across calls.
                pending.Append(text);
                var all = pending.ToString();
                var lastBreak = all.LastIndexOf('\n');
                if (lastBreak < 0)
                    continue;
                foreach (var line in all[..lastBreak].Split('\n'))
                    onLine(line.TrimEnd('\r'));
                pending.Clear().Append(all[(lastBreak + 1)..]);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Debug output listener stopped");
        }
        finally
        {
            if (pending.Length > 0)
                onLine(pending.ToString());
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread.Join(1000);
        _bufferReady.Dispose();
        _dataReady.Dispose();
        _view.Dispose();
        _file.Dispose();
        _cts.Dispose();
    }
}
