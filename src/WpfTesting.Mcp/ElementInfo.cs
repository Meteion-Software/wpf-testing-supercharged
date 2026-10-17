using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WpfTesting.Mcp;

/// <summary>Reads element state and formats it as compact text for agents.</summary>
public static class ElementInfo
{
    /// <summary>The text a user would see: Value pattern, then Text pattern, then Name.</summary>
    public static string GetText(AutomationElement element)
    {
        var patterns = element.Patterns;
        if (patterns.Value.TryGetPattern(out var value) && value.Value.ValueOrDefault is { } v)
            return v;
        if (patterns.Text.TryGetPattern(out var text))
        {
            try
            {
                return text.DocumentRange.GetText(-1).TrimEnd('\r', '\n');
            }
            catch
            {
                // Fall through to Name.
            }
        }
        return element.Properties.Name.ValueOrDefault ?? "";
    }

    public static ToggleState? GetToggleState(AutomationElement element) =>
        element.Patterns.Toggle.TryGetPattern(out var toggle) ? toggle.ToggleState.ValueOrDefault : null;

    public static bool? GetIsSelected(AutomationElement element) =>
        element.Patterns.SelectionItem.TryGetPattern(out var item) ? item.IsSelected.ValueOrDefault : null;

    public static ExpandCollapseState? GetExpandState(AutomationElement element) =>
        element.Patterns.ExpandCollapse.TryGetPattern(out var expand) ? expand.ExpandCollapseState.ValueOrDefault : null;

    public static bool IsVisible(AutomationElement element) =>
        element.IsAvailable && !element.Properties.IsOffscreen.ValueOrDefault;

    /// <summary>One line: <c>[e12] Button #LoginButton "Log in" (disabled)</c>.</summary>
    public static string Summarize(AutomationElement element, string? reference)
    {
        var sb = new StringBuilder();
        if (reference is not null)
            sb.Append('[').Append(reference).Append("] ");

        var props = element.Properties;
        sb.Append(props.ControlType.ValueOrDefault);

        var id = props.AutomationId.ValueOrDefault;
        if (!string.IsNullOrEmpty(id))
            sb.Append(" #").Append(id);

        var name = props.Name.ValueOrDefault;
        if (!string.IsNullOrEmpty(name))
            sb.Append(" \"").Append(Truncate(name, 80)).Append('"');

        if (element.Patterns.Value.TryGetPattern(out var value))
        {
            var v = value.Value.ValueOrDefault;
            if (!string.IsNullOrEmpty(v) && v != name)
                sb.Append(" value=\"").Append(Truncate(v, 80)).Append('"');
        }

        var flags = new List<string>();
        if (!props.IsEnabled.ValueOrDefault)
            flags.Add("disabled");
        if (props.IsOffscreen.ValueOrDefault)
            flags.Add("offscreen");
        if (GetToggleState(element) is { } toggle)
            flags.Add(toggle switch { ToggleState.On => "checked", ToggleState.Off => "unchecked", _ => "indeterminate" });
        if (GetIsSelected(element) == true)
            flags.Add("selected");
        switch (GetExpandState(element))
        {
            case ExpandCollapseState.Expanded: flags.Add("expanded"); break;
            case ExpandCollapseState.Collapsed: flags.Add("collapsed"); break;
        }
        if (props.HasKeyboardFocus.ValueOrDefault)
            flags.Add("focused");
        if (flags.Count > 0)
            sb.Append(" (").Append(string.Join(", ", flags)).Append(')');

        return sb.ToString();
    }

    /// <summary>Full property and pattern dump for get_element.</summary>
    public static string Describe(AutomationElement element, string reference)
    {
        var props = element.Properties;
        var rect = props.BoundingRectangle.ValueOrDefault;
        var sb = new StringBuilder();
        sb.AppendLine(Summarize(element, reference));
        sb.AppendLine($"automationId: {props.AutomationId.ValueOrDefault}");
        sb.AppendLine($"name: {props.Name.ValueOrDefault}");
        sb.AppendLine($"controlType: {props.ControlType.ValueOrDefault}");
        sb.AppendLine($"className: {props.ClassName.ValueOrDefault}");
        sb.AppendLine($"frameworkId: {props.FrameworkId.ValueOrDefault}");
        sb.AppendLine($"text: {GetText(element)}");
        sb.AppendLine($"enabled: {props.IsEnabled.ValueOrDefault}");
        sb.AppendLine($"offscreen: {props.IsOffscreen.ValueOrDefault}");
        sb.AppendLine($"hasKeyboardFocus: {props.HasKeyboardFocus.ValueOrDefault}");
        sb.AppendLine($"bounds: x={rect.X} y={rect.Y} w={rect.Width} h={rect.Height}");
        if (props.HelpText.ValueOrDefault is { Length: > 0 } help)
            sb.AppendLine($"helpText: {help}");
        if (props.ItemStatus.ValueOrDefault is { Length: > 0 } status)
            sb.AppendLine($"itemStatus: {status}");

        var p = element.Patterns;
        if (p.Value.TryGetPattern(out var value))
            sb.AppendLine($"value: {value.Value.ValueOrDefault} (readOnly: {value.IsReadOnly.ValueOrDefault})");
        if (p.RangeValue.TryGetPattern(out var range))
            sb.AppendLine($"rangeValue: {range.Value.ValueOrDefault} (min {range.Minimum.ValueOrDefault}, max {range.Maximum.ValueOrDefault})");
        if (GetToggleState(element) is { } toggle)
            sb.AppendLine($"toggleState: {toggle}");
        if (GetIsSelected(element) is { } selected)
            sb.AppendLine($"isSelected: {selected}");
        if (GetExpandState(element) is { } expand)
            sb.AppendLine($"expandCollapseState: {expand}");
        if (p.Selection.TryGetPattern(out var selection))
        {
            var items = selection.Selection.ValueOrDefault ?? [];
            sb.AppendLine($"selectedItems: [{string.Join(", ", items.Select(i => $"\"{GetText(i)}\""))}]");
        }
        if (p.Grid.TryGetPattern(out var grid))
            sb.AppendLine($"grid: {grid.RowCount.ValueOrDefault} rows x {grid.ColumnCount.ValueOrDefault} columns");

        var supported = new List<string>();
        if (p.Invoke.IsSupported) supported.Add("Invoke");
        if (p.Value.IsSupported) supported.Add("Value");
        if (p.Toggle.IsSupported) supported.Add("Toggle");
        if (p.SelectionItem.IsSupported) supported.Add("SelectionItem");
        if (p.Selection.IsSupported) supported.Add("Selection");
        if (p.ExpandCollapse.IsSupported) supported.Add("ExpandCollapse");
        if (p.RangeValue.IsSupported) supported.Add("RangeValue");
        if (p.Text.IsSupported) supported.Add("Text");
        if (p.Grid.IsSupported) supported.Add("Grid");
        if (p.Table.IsSupported) supported.Add("Table");
        if (p.Scroll.IsSupported) supported.Add("Scroll");
        if (p.ScrollItem.IsSupported) supported.Add("ScrollItem");
        if (p.Window.IsSupported) supported.Add("Window");
        sb.AppendLine($"patterns: {string.Join(", ", supported)}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Indented outline of the subtree with a ref on every node.</summary>
    public static string Tree(AppSession session, AutomationElement root, int maxDepth, bool includeOffscreen, int maxNodes)
    {
        var sb = new StringBuilder();
        var count = 0;
        var truncated = false;

        void Walk(AutomationElement element, int depth)
        {
            if (count >= maxNodes)
            {
                truncated = true;
                return;
            }
            count++;
            sb.Append(' ', depth * 2).AppendLine(Summarize(element, session.RegisterRef(element)));
            if (depth >= maxDepth)
                return;

            AutomationElement[] children;
            try
            {
                children = element.FindAllChildren();
            }
            catch
            {
                return;
            }
            foreach (var child in children)
            {
                if (!includeOffscreen && IsHiddenSafe(child))
                    continue;
                Walk(child, depth + 1);
            }
        }

        Walk(root, 0);
        if (truncated)
            sb.AppendLine($"... truncated at {maxNodes} nodes. Narrow with the 'root' parameter or lower maxDepth.");
        return sb.ToString().TrimEnd();
    }

    private static bool IsHiddenSafe(AutomationElement element)
    {
        try
        {
            return element.Properties.IsOffscreen.ValueOrDefault;
        }
        catch
        {
            return true;
        }
    }

    private static string Truncate(string s, int max) =>
        (s.Length <= max ? s : s[..max] + "…").Replace("\r", "").Replace("\n", "\\n");
}
