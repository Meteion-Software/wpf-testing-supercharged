using FlaUI.Core.WindowsAPI;
using ModelContextProtocol;

namespace WpfTesting.Mcp;

/// <summary>Parses key specs like "Enter", "Ctrl+S" or "Ctrl+Shift+Tab Enter" (space separates presses).</summary>
public static class KeyParser
{
    public const string Help =
        "Space-separated key presses; join modifiers with '+'. Examples: 'Enter', 'Ctrl+S', 'Alt+F4', 'Tab Tab Space', 'Ctrl+Shift+Z'. " +
        "Names: Ctrl, Shift, Alt, Win, Enter, Esc, Tab, Space, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Up, Down, Left, Right, F1-F24, A-Z, 0-9, or any VirtualKeyShort name.";

    public static IReadOnlyList<VirtualKeyShort[]> Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new McpException("Keys must not be empty. " + Help);

        return spec
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(chord => chord.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(ParseKey).ToArray())
            .ToList();
    }

    private static VirtualKeyShort ParseKey(string name)
    {
        var key = name.Trim().ToUpperInvariant();
        VirtualKeyShort? known = key switch
        {
            "CTRL" or "CONTROL" => VirtualKeyShort.CONTROL,
            "SHIFT" => VirtualKeyShort.SHIFT,
            "ALT" => VirtualKeyShort.ALT,
            "WIN" or "WINDOWS" or "META" => VirtualKeyShort.LWIN,
            "ENTER" or "RETURN" => VirtualKeyShort.RETURN,
            "ESC" or "ESCAPE" => VirtualKeyShort.ESCAPE,
            "TAB" => VirtualKeyShort.TAB,
            "SPACE" => VirtualKeyShort.SPACE,
            "BACKSPACE" or "BACK" => VirtualKeyShort.BACK,
            "DEL" or "DELETE" => VirtualKeyShort.DELETE,
            "INS" or "INSERT" => VirtualKeyShort.INSERT,
            "HOME" => VirtualKeyShort.HOME,
            "END" => VirtualKeyShort.END,
            "PAGEUP" or "PGUP" => VirtualKeyShort.PRIOR,
            "PAGEDOWN" or "PGDN" => VirtualKeyShort.NEXT,
            "UP" => VirtualKeyShort.UP,
            "DOWN" => VirtualKeyShort.DOWN,
            "LEFT" => VirtualKeyShort.LEFT,
            "RIGHT" => VirtualKeyShort.RIGHT,
            _ => null,
        };
        if (known is { } k)
            return k;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) && Enum.TryParse<VirtualKeyShort>("KEY_" + key, out var letter))
            return letter;
        if (Enum.TryParse<VirtualKeyShort>(key, ignoreCase: true, out var parsed))
            return parsed;
        throw new McpException($"Unknown key '{name}'. " + Help);
    }
}
