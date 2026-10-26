using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace WpfTesting.Mcp.Tools;

[McpServerToolType]
public sealed class AppTools(AppSession session, StepRecorder recorder)
{
    private const int StartupOutputLines = 15;

    [McpServerTool(Name = "launch_app", Destructive = false), Description(
        "Launch the WPF application under test and wait for its main window. Closes any app this server previously launched. " +
        "Clears the recorded steps. Can build the project first: pass projectPath (or set WPF_MCP_PROJECT_PATH) to build in Debug " +
        "and launch the result, optionally with compile symbols such as AUTOMATED_TEST via defineConstants. " +
        "The app's stdout, stderr and Debug.WriteLine output is captured; read it with get_app_output.")]
    public string LaunchApp(
        [Description("Path to the app's .exe. When given, nothing is built.")] string? exePath = null,
        [Description("Path to the WPF project (.csproj, or a folder holding one) to build and launch. " +
                     "Defaults to WPF_MCP_PROJECT_PATH, then WPF_MCP_APP_PATH, then a WPF project found from the server's working directory.")] string? projectPath = null,
        [Description("Build configuration. Defaults to Debug.")] string configuration = "Debug",
        [Description("Compile symbols added to the build, for example [\"AUTOMATED_TEST\"], so the app can use #if AUTOMATED_TEST " +
                     "(skip authentication and so on). Added to the project's own symbols. Requires a build, and the Debug configuration.")] string[]? defineConstants = null,
        [Description("Launch the existing build output without building.")] bool noBuild = false,
        [Description("Allow defineConstants with a configuration other than Debug.")] bool allowNonDebug = false,
        [Description("Command-line arguments for the app.")] string? arguments = null,
        [Description("Working directory. Defaults to the exe's folder.")] string? workingDirectory = null,
        [Description("Seconds to wait for the main window.")] int timeoutSeconds = 30,
        [Description("Seconds to allow the build.")] int buildTimeoutSeconds = 300) => session.Run(() =>
    {
        var constants = (defineConstants ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
        foreach (var constant in constants)
            ProjectBuilder.ValidateConstant(constant);
        if (constants.Count > 0 && !configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase) && !allowNonDebug)
        {
            throw new McpException(
                $"defineConstants with the '{configuration}' configuration is refused so a test-only bypass can't end up in a non-Debug build. " +
                "Use Debug, or pass allowNonDebug=true if you really mean it.");
        }

        // An explicit exePath wins and means no build. Otherwise a project (given, configured or discovered) is built.
        string path;
        string? buildSummary = null;
        var projectSource = exePath is not null ? null
            : projectPath ?? session.Options.DefaultProjectPath
              ?? (session.Options.DefaultAppPath is null ? "" : null);
        if (projectSource is not null)
        {
            var project = ProjectBuilder.ResolveProject(projectSource.Length == 0 ? null : projectSource, Environment.CurrentDirectory);
            if (noBuild)
            {
                if (constants.Count > 0)
                    throw new McpException("defineConstants needs a build, but noBuild is set.");
                path = ProjectBuilder.FindBuiltExe(project, configuration);
            }
            else
            {
                session.CloseCurrent(kill: true); // a running app locks its own output files
                var build = ProjectBuilder.Build(project, configuration, constants, TimeSpan.FromSeconds(buildTimeoutSeconds));
                path = build.ExePath;
                buildSummary = build.Summary;
            }
        }
        else
        {
            if (constants.Count > 0)
                throw new McpException("defineConstants needs a build, but an exe path is being launched directly. Pass projectPath instead.");
            path = exePath ?? session.Options.DefaultAppPath
                ?? throw new McpException("No exePath or project given.");
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
                throw new McpException($"Executable not found: {path}. Pass projectPath to build it, or build it first (dotnet build).");
        }

        var startInfo = new ProcessStartInfo(path, arguments ?? "")
        {
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(path)!,
            UseShellExecute = false,
        };
        var app = session.Launch(startInfo, TimeSpan.FromSeconds(timeoutSeconds));
        var window = session.GetMainWindow();

        recorder.Clear();
        recorder.Comment($"Launch {Path.GetFileName(path)}{(string.IsNullOrEmpty(arguments) ? "" : " " + arguments)}");

        var sb = new StringBuilder();
        if (buildSummary is not null)
            sb.AppendLine(buildSummary);
        sb.AppendLine($"Launched {Path.GetFileName(path)} (pid {app.ProcessId}). Main window: \"{window.Name}\".");
        if (session.Output.DebugCaptureProblem is { } problem)
            sb.AppendLine(problem);
        var startup = session.Output.ReadNew(StartupOutputLines);
        if (startup.Lines.Count > 0)
        {
            sb.AppendLine("Startup output:");
            sb.AppendLine(startup.Format());
        }
        sb.Append("Next: get_ui_tree to see the controls.");
        return sb.ToString();
    });

    [McpServerTool(Name = "get_app_output", ReadOnly = true), Description(
        "Read what the app under test has written: stdout, stderr and Debug.WriteLine/Trace output, each line tagged with its source. " +
        "By default returns only lines not returned before. Still works after the app has exited or crashed, until the next launch_app.")]
    public string GetAppOutput(
        [Description("Return lines after this sequence number (the number in square brackets). 0 returns everything still buffered. Omit for new lines only.")] long? since = null,
        [Description("Case-insensitive regular expression; only matching lines are returned.")] string? filter = null,
        [Description("Maximum number of lines to return.")] int maxLines = 200) => session.Run(() =>
    {
        Regex? regex = null;
        if (!string.IsNullOrEmpty(filter))
        {
            try
            {
                regex = new Regex(filter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                throw new McpException($"Invalid filter regex: {ex.Message}");
            }
        }

        maxLines = Math.Clamp(maxLines, 1, 2000);
        var page = since is { } after
            ? session.Output.ReadSince(Math.Max(0, after), maxLines, regex)
            : session.Output.ReadNew(maxLines, regex);
        var text = page.Format();
        return text.Length > 0 ? text : "No new output.";
    });

    [McpServerTool(Name = "attach_app"), Description(
        "Attach to an already-running application by process id or process name (without .exe). " +
        "An attached app is left running when the session ends.")]
    public string AttachApp(
        [Description("Process id (e.g. '1234') or process name (e.g. 'MyApp').")] string process) => session.Run(() =>
    {
        var app = session.Attach(process);
        var window = session.GetMainWindow();
        return $"Attached to {app.Name} (pid {app.ProcessId}). Main window: \"{window.Name}\".";
    });

    [McpServerTool(Name = "close_app", Destructive = true), Description(
        "Close the application under test. Use at the end of a test so the next one starts fresh.")]
    public string CloseApp(
        [Description("Kill the process instead of closing its main window gracefully.")] bool kill = false,
        [Description("Also close an app that was attached rather than launched.")] bool closeAttached = false) =>
        session.Run(() => session.CloseCurrent(kill, closeAttached));

    [McpServerTool(Name = "list_windows", ReadOnly = true), Description(
        "List the app's top-level windows, including modal dialogs, message boxes, popups and open menus. " +
        "Use the title with the 'window' parameter of other tools to scope a search.")]
    public string ListWindows() => session.Run(() =>
    {
        var app = session.App;
        var sb = new StringBuilder($"{app.Name} (pid {app.ProcessId})\n");
        foreach (var top in session.GetTopLevelElements())
            sb.AppendLine("  " + ElementInfo.Summarize(top, session.RegisterRef(top)));
        return sb.ToString().TrimEnd();
    });
}
