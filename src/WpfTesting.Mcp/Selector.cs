using FlaUI.Core.Definitions;
using ModelContextProtocol;

namespace WpfTesting.Mcp;

/// <summary>
/// Parsed element selector. Grammar:
/// <list type="bullet">
/// <item><c>LoginButton</c> or <c>#LoginButton</c>: AutomationId.</item>
/// <item><c>@e12</c>: element reference returned by get_ui_tree / find_elements.</item>
/// <item><c>key=value;key=value</c>: keys are id, name, text (name contains), type (ControlType), class, index (0-based).</item>
/// </list>
/// </summary>
public sealed record Selector(
    string Raw,
    string? Ref = null,
    string? AutomationId = null,
    string? Name = null,
    string? NameContains = null,
    ControlType? ControlType = null,
    string? ClassName = null,
    int Index = 0)
{
    public const string Help =
        "Element selector. 'LoginButton' or '#LoginButton' = AutomationId (preferred). " +
        "'@e12' = ref from get_ui_tree/find_elements. " +
        "Or 'key=value' pairs joined by ';' with keys id, name, text (name contains), type (ControlType e.g. Button), class, index (0-based), " +
        "e.g. 'type=Button;name=OK'.";

    public bool IsAutomationIdOnly =>
        AutomationId is not null && Name is null && NameContains is null && ControlType is null && ClassName is null && Index == 0;

    public static Selector Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new McpException("Selector must not be empty. " + Help);

        var s = raw.Trim();
        if (s.StartsWith('@'))
            return new Selector(s, Ref: s[1..]);
        if (!s.Contains('='))
            return new Selector(s, AutomationId: s.TrimStart('#'));

        var selector = new Selector(s);
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                throw new McpException($"Invalid selector part '{part}'. " + Help);
            var key = part[..eq].Trim().ToLowerInvariant();
            var value = part[(eq + 1)..].Trim().Trim('\'', '"');
            selector = key switch
            {
                "id" or "automationid" => selector with { AutomationId = value },
                "name" => selector with { Name = value },
                "text" or "name~" => selector with { NameContains = value },
                "type" or "controltype" => selector with { ControlType = ParseControlType(value) },
                "class" or "classname" => selector with { ClassName = value },
                "index" => selector with { Index = int.TryParse(value, out var i) && i >= 0 ? i : throw new McpException($"Invalid index '{value}'.") },
                _ => throw new McpException($"Unknown selector key '{key}'. " + Help),
            };
        }
        return selector;
    }

    private static ControlType ParseControlType(string value) =>
        Enum.TryParse<ControlType>(value, ignoreCase: true, out var type)
            ? type
            : throw new McpException($"Unknown control type '{value}'. Valid values: {string.Join(", ", Enum.GetNames<ControlType>())}.");
}
