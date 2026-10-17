using System.Text;
using FlaUI.Core.AutomationElements;

namespace WpfTesting.Mcp;

/// <summary>
/// Records successful actions as FlaUI C# so an exploratory agent session can be turned into an
/// xUnit test. Elements are addressed by AutomationId, matching the driver-object conventions.
/// </summary>
public sealed class StepRecorder
{
    private readonly object _gate = new();
    private readonly List<string> _steps = [];

    public void Comment(string text) => Add($"// {text}");

    public void Action(AutomationElement element, string call) => Add($"{Locate(element)}{call};");

    public void Raw(string line) => Add(line);

    public void Clear()
    {
        lock (_gate)
            _steps.Clear();
    }

    public string Render()
    {
        lock (_gate)
        {
            if (_steps.Count == 0)
                return "No steps recorded yet.";

            var sb = new StringBuilder();
            sb.AppendLine("// Recorded by the WPF testing MCP server. Assumes these members in the test class:");
            sb.AppendLine("//   private readonly Window _window;   // main window from app.GetMainWindow(automation)");
            sb.AppendLine("//   private AutomationElement Find(string automationId) =>");
            sb.AppendLine("//       Retry.WhileNull(() => _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),");
            sb.AppendLine("//           TimeSpan.FromSeconds(5), throwOnTimeout: true).Result!;");
            sb.AppendLine("// If a driver exists for the view, replace Find(\"X\").AsY() with the driver's X property.");
            sb.AppendLine();
            foreach (var step in _steps)
                sb.AppendLine(step);
            return sb.ToString().TrimEnd();
        }
    }

    public static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    /// <summary>The C# expression that finds the element, including the As*() cast.</summary>
    public static string Locate(AutomationElement element)
    {
        var id = element.Properties.AutomationId.ValueOrDefault;
        if (!string.IsNullOrEmpty(id))
            return $"Find({Literal(id)})";

        var name = element.Properties.Name.ValueOrDefault ?? "";
        var type = element.Properties.ControlType.ValueOrDefault;
        return $"/* TODO: no AutomationId, run add-automation-ids */ _window.FindFirstDescendant(cf => cf.ByControlType(ControlType.{type}).And(cf.ByName({Literal(name)})))";
    }

    private void Add(string line)
    {
        lock (_gate)
            _steps.Add(line);
    }
}
