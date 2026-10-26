using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;

namespace WpfTesting.Mcp;

/// <summary>
/// Owns the application under test and the UIA3 automation instance. Every tool call goes
/// through <see cref="Run{T}"/>, which serializes access so concurrent MCP requests never
/// drive the UI at the same time.
/// </summary>
public sealed class AppSession(SessionOptions options, ILogger<AppSession> logger) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AutomationElement> _refsById = new();
    private readonly Dictionary<string, string> _refsByRuntimeId = new();
    private int _nextRef;
    private UIA3Automation? _automation;
    private Application? _app;
    private bool _launchedByUs;

    public SessionOptions Options => options;

    public UIA3Automation Automation => _automation ??= new UIA3Automation();

    public ConditionFactory Cf => Automation.ConditionFactory;

    public bool HasApp => _app is { HasExited: false };

    public Application App => HasApp
        ? _app!
        : throw new McpException("No application is running. Call launch_app or attach_app first.");

    /// <summary>Runs a UI operation under the session lock, converting failures into tool errors the agent can read.</summary>
    public T Run<T>(Func<T> action)
    {
        lock (_gate)
        {
            try
            {
                return action();
            }
            catch (McpException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tool call failed");
                throw new McpException($"{ex.GetType().Name}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>Output of the app launched last (and still readable after it exits). Empty before the first launch or after attach_app.</summary>
    public OutputCapture Output { get; private set; } = new(options.OutputBufferLines, NullLogger.Instance);

    public Application Launch(ProcessStartInfo startInfo, TimeSpan timeout)
    {
        CloseCurrent(kill: true);

        // Start the process ourselves so stdout/stderr can be redirected, then hand it to FlaUI.
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = false;

        Output.Dispose();
        Output = new OutputCapture(options.OutputBufferLines, logger);

        var process = Process.Start(startInfo)
            ?? throw new McpException($"Could not start {startInfo.FileName}.");
        var capture = Output;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => capture.StopDebugCapture();
        capture.StartProcessReaders(process);
        capture.StartDebugCapture(process.Id);

        // Attach by id so FlaUI gets its own Process object. Its wait helpers close the Process they are given,
        // which would silently stop our output readers.
        var app = Application.Attach(process.Id);
        _app = app;
        _launchedByUs = true;
        app.WaitWhileMainHandleIsMissing(timeout);
        app.GetMainWindow(Automation, timeout);
        return app;
    }

    public Application Attach(string processNameOrId)
    {
        CloseCurrent(kill: false);
        Output.Dispose();
        Output = new OutputCapture(options.OutputBufferLines, logger);
        _app = int.TryParse(processNameOrId, out var pid)
            ? Application.Attach(pid)
            : Application.Attach(processNameOrId);
        _launchedByUs = false;
        return _app;
    }

    /// <summary>Closes (or kills) an app we launched; for an attached app, only detaches unless asked to close it.</summary>
    public string CloseCurrent(bool kill, bool closeAttached = false)
    {
        ClearRefs();
        if (_app is null)
            return "No application was running.";

        var app = _app;
        _app = null;
        try
        {
            if (_launchedByUs)
                Output.StopDebugCapture(); // keeps the captured lines readable
            if (app.HasExited)
                return "Application had already exited.";
            if (!_launchedByUs && !closeAttached)
                return "Detached from application (left running because it was attached, not launched).";
            if (kill)
            {
                app.Kill();
                return "Application killed.";
            }
            return app.Close(killIfCloseFails: true) ? "Application closed." : "Application did not close gracefully and was killed.";
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>All top-level elements owned by the app process: main window, dialogs, popups and menus.</summary>
    public IReadOnlyList<AutomationElement> GetTopLevelElements(string? windowTitle = null)
    {
        var app = App;
        var tops = Automation.GetDesktop().FindAllChildren(Cf.ByProcessId(app.ProcessId));
        return string.IsNullOrEmpty(windowTitle)
            ? tops
            : tops.Where(t => SafeName(t).Contains(windowTitle, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public AutomationElement GetMainWindow() => App.GetMainWindow(Automation, TimeSpan.FromSeconds(2))
        ?? throw new McpException("The application has no main window.");

    /// <summary>Finds every element matching the selector without retrying.</summary>
    public IReadOnlyList<AutomationElement> FindAll(Selector selector, string? windowTitle = null)
    {
        if (selector.Ref is not null)
            return ResolveRef(selector.Ref) is { } element ? [element] : [];

        var condition = BuildCondition(selector);
        var matches = new List<AutomationElement>();
        foreach (var top in GetTopLevelElements(windowTitle))
        {
            if (Matches(top, selector))
                matches.Add(top);
            matches.AddRange(top.FindAllDescendants(condition).Where(e => MatchesContains(e, selector)));
        }
        return matches;
    }

    /// <summary>Finds the element matching the selector, retrying until the timeout so agents get Playwright-style auto-waiting.</summary>
    public AutomationElement Find(string selectorText, string? windowTitle = null, TimeSpan? timeout = null)
    {
        var selector = Selector.Parse(selectorText);
        if (selector.Ref is not null)
        {
            return ResolveRef(selector.Ref)
                ?? throw new McpException($"Ref '@{selector.Ref}' is unknown or the element no longer exists. Call get_ui_tree again to get fresh refs.");
        }

        var deadline = Stopwatch.StartNew();
        var limit = timeout ?? options.FindTimeout;
        while (true)
        {
            var matches = FindAll(selector, windowTitle);
            if (matches.Count > selector.Index)
                return matches[selector.Index];
            if (deadline.Elapsed >= limit)
            {
                var found = matches.Count == 0 ? "No element matched" : $"Only {matches.Count} element(s) matched";
                var scope = windowTitle is null ? "" : $" in windows titled '*{windowTitle}*'";
                throw new McpException(
                    $"{found} selector '{selectorText}'{scope} within {limit.TotalSeconds:0.#}s. " +
                    "Use get_ui_tree or find_elements to see what is on screen.");
            }
            Thread.Sleep(150);
        }
    }

    public string RegisterRef(AutomationElement element)
    {
        var runtimeId = RuntimeIdOf(element);
        if (runtimeId is not null && _refsByRuntimeId.TryGetValue(runtimeId, out var existing))
        {
            _refsById[existing] = element;
            return existing;
        }

        var id = $"e{++_nextRef}";
        _refsById[id] = element;
        if (runtimeId is not null)
            _refsByRuntimeId[runtimeId] = id;
        return id;
    }

    /// <summary>Brings the top-level window containing the element to the foreground so mouse and keyboard input land on it.</summary>
    public void BringToFront(AutomationElement element)
    {
        try
        {
            var desktop = Automation.GetDesktop();
            var walker = Automation.TreeWalkerFactory.GetControlViewWalker();
            var current = element;
            while (walker.GetParent(current) is { } parent && !parent.Equals(desktop))
                current = parent;
            if (current.ControlType == FlaUI.Core.Definitions.ControlType.Window)
                current.AsWindow().SetForeground();
            Wait.UntilInputIsProcessed();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not bring window to front");
        }
    }

    /// <summary>
    /// Makes sure keyboard input goes to the app rather than, say, the agent's terminal. Keeps the current focus
    /// when it is already inside the app; otherwise activates the app's topmost window (desktop children are in z-order).
    /// </summary>
    public void EnsureAppHasFocus()
    {
        try
        {
            if (Automation.FocusedElement()?.Properties.ProcessId.ValueOrDefault == App.ProcessId)
                return;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read the focused element");
        }

        var top = GetTopLevelElements().FirstOrDefault(e => e.ControlType == FlaUI.Core.Definitions.ControlType.Window)
            ?? GetMainWindow();
        BringToFront(top);
    }

    public void ClearRefs()
    {
        _refsById.Clear();
        _refsByRuntimeId.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_app is not null)
                CloseCurrent(kill: true);
            _automation?.Dispose();
        }
    }

    private AutomationElement? ResolveRef(string id)
    {
        if (!_refsById.TryGetValue(id, out var element))
            return null;
        try
        {
            return element.IsAvailable ? element : null;
        }
        catch
        {
            return null;
        }
    }

    private ConditionBase BuildCondition(Selector selector)
    {
        var parts = new List<ConditionBase>();
        if (selector.AutomationId is not null)
            parts.Add(Cf.ByAutomationId(selector.AutomationId));
        if (selector.Name is not null)
            parts.Add(Cf.ByName(selector.Name));
        if (selector.ControlType is { } type)
            parts.Add(Cf.ByControlType(type));
        if (selector.ClassName is not null)
            parts.Add(Cf.ByClassName(selector.ClassName));
        return parts.Count switch
        {
            0 => TrueCondition.Default,
            1 => parts[0],
            _ => new AndCondition(parts.ToArray()),
        };
    }

    private static bool Matches(AutomationElement element, Selector selector)
    {
        if (selector.AutomationId is null && selector.Name is null && selector.ControlType is null && selector.ClassName is null && selector.NameContains is null)
            return false;
        try
        {
            return (selector.AutomationId is null || element.Properties.AutomationId.ValueOrDefault == selector.AutomationId)
                && (selector.Name is null || element.Properties.Name.ValueOrDefault == selector.Name)
                && (selector.ControlType is null || element.Properties.ControlType.ValueOrDefault == selector.ControlType)
                && (selector.ClassName is null || element.Properties.ClassName.ValueOrDefault == selector.ClassName)
                && MatchesContains(element, selector);
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesContains(AutomationElement element, Selector selector) =>
        selector.NameContains is null || SafeName(element).Contains(selector.NameContains, StringComparison.OrdinalIgnoreCase);

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Properties.Name.ValueOrDefault ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string? RuntimeIdOf(AutomationElement element)
    {
        try
        {
            return element.Properties.RuntimeId.ValueOrDefault is { Length: > 0 } id ? string.Join('.', id) : null;
        }
        catch
        {
            return null;
        }
    }
}
