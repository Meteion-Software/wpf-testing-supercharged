using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace WpfTesting.Mcp.Tools;

[McpServerToolType]
public sealed class AppTools(AppSession session, StepRecorder recorder)
{
    [McpServerTool(Name = "launch_app", Destructive = false), Description(
        "Launch the WPF application under test and wait for its main window. Closes any app this server previously launched. " +
        "Clears the recorded steps.")]
    public string LaunchApp(
        [Description("Path to the app's .exe. Optional when WPF_MCP_APP_PATH is configured.")] string? exePath = null,
        [Description("Command-line arguments for the app.")] string? arguments = null,
        [Description("Working directory. Defaults to the exe's folder.")] string? workingDirectory = null,
        [Description("Seconds to wait for the main window.")] int timeoutSeconds = 30) => session.Run(() =>
    {
        var path = exePath ?? session.Options.DefaultAppPath
            ?? throw new McpException("No exePath given and WPF_MCP_APP_PATH is not set.");
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new McpException($"Executable not found: {path}. Build the app first (dotnet build).");

        var startInfo = new ProcessStartInfo(path, arguments ?? "")
        {
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(path)!,
            UseShellExecute = false,
        };
        var app = session.Launch(startInfo, TimeSpan.FromSeconds(timeoutSeconds));
        var window = session.GetMainWindow();

        recorder.Clear();
        recorder.Comment($"Launch {Path.GetFileName(path)}{(string.IsNullOrEmpty(arguments) ? "" : " " + arguments)}");
        return $"Launched {Path.GetFileName(path)} (pid {app.ProcessId}). Main window: \"{window.Name}\".\n" +
               "Next: get_ui_tree to see the controls.";
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
