using System.ComponentModel;
using System.Text;
using FlaUI.Core.AutomationElements;
using ModelContextProtocol.Server;

namespace WpfTesting.Mcp.Tools;

[McpServerToolType]
public sealed class InspectionTools(AppSession session)
{
    [McpServerTool(Name = "get_ui_tree", ReadOnly = true), Description(
        "Snapshot the UI as an indented outline. Each line is '[ref] ControlType #AutomationId \"Name\" value=... (state)'. " +
        "Target elements by AutomationId (preferred, stable) or by '@ref' (valid for this session only). Call again after the UI changes.")]
    public string GetUiTree(
        [Description("Optional selector for the subtree root. Defaults to all of the app's top-level windows. " + Selector.Help)] string? root = null,
        [Description("Only include top-level windows whose title contains this text.")] string? window = null,
        [Description("Maximum depth below the root.")] int maxDepth = 12,
        [Description("Include offscreen elements (collapsed, scrolled out of view, hidden tabs).")] bool includeOffscreen = false,
        [Description("Stop after this many nodes.")] int maxNodes = 800) => session.Run(() =>
    {
        IEnumerable<AutomationElement> roots = root is null
            ? session.GetTopLevelElements(window)
            : [session.Find(root, window)];

        var sb = new StringBuilder();
        foreach (var r in roots)
            sb.AppendLine(ElementInfo.Tree(session, r, maxDepth, includeOffscreen, maxNodes));
        return sb.Length == 0 ? "No windows found." : sb.ToString().TrimEnd();
    });

    [McpServerTool(Name = "find_elements", ReadOnly = true), Description(
        "List all elements matching a selector, without waiting. Useful for checking how many matches exist or finding elements by type or text.")]
    public string FindElements(
        [Description(Selector.Help)] string selector,
        [Description("Only search top-level windows whose title contains this text.")] string? window = null,
        [Description("Maximum results to return.")] int max = 50) => session.Run(() =>
    {
        var matches = session.FindAll(Selector.Parse(selector), window);
        if (matches.Count == 0)
            return $"No elements match '{selector}'.";

        var sb = new StringBuilder($"{matches.Count} match(es) for '{selector}':\n");
        for (var i = 0; i < Math.Min(max, matches.Count); i++)
            sb.AppendLine($"  {i}: " + ElementInfo.Summarize(matches[i], session.RegisterRef(matches[i])));
        if (matches.Count > max)
            sb.AppendLine($"  ... {matches.Count - max} more");
        return sb.ToString().TrimEnd();
    });

    [McpServerTool(Name = "get_element", ReadOnly = true), Description(
        "Get all properties of one element: text, value, enabled, toggle/selection/expand state, bounds and supported UIA patterns. " +
        "Waits for the element to appear.")]
    public string GetElement(
        [Description(Selector.Help)] string selector,
        [Description("Only search top-level windows whose title contains this text.")] string? window = null) => session.Run(() =>
    {
        var element = session.Find(selector, window);
        return ElementInfo.Describe(element, session.RegisterRef(element));
    });

    [McpServerTool(Name = "get_text", ReadOnly = true), Description(
        "Get the visible text of an element (TextBox value, TextBlock/Label content, button caption, etc.).")]
    public string GetText(
        [Description(Selector.Help)] string selector,
        [Description("Only search top-level windows whose title contains this text.")] string? window = null) =>
        session.Run(() => ElementInfo.GetText(session.Find(selector, window)));

    [McpServerTool(Name = "read_grid", ReadOnly = true), Description(
        "Read a DataGrid, ListView or other Grid-pattern control as a Markdown table. " +
        "WPF virtualizes rows, so only realized (near-visible) rows may be returned; scroll to read more.")]
    public string ReadGrid(
        [Description(Selector.Help)] string selector,
        [Description("Only search top-level windows whose title contains this text.")] string? window = null,
        [Description("Maximum rows to read.")] int maxRows = 50) => session.Run(() =>
    {
        var element = session.Find(selector, window);
        if (!element.Patterns.Grid.IsSupported)
            return ReadAsList(element, maxRows);

        var grid = element.AsGrid();
        var headers = grid.Header?.Columns.Select(c => c.Text).ToArray() ?? [];
        var rows = grid.Rows.Take(maxRows).Select(r => r.Cells.Select(CellText).ToArray()).ToList();
        var columns = Math.Max(headers.Length, rows.Count == 0 ? 0 : rows.Max(r => r.Length));
        if (headers.Length < columns)
            headers = [.. headers, .. Enumerable.Range(headers.Length, columns - headers.Length).Select(i => $"Column{i}")];

        var sb = new StringBuilder();
        sb.AppendLine($"{grid.RowCount} row(s) total, showing {rows.Count}.");
        sb.AppendLine("| # | " + string.Join(" | ", headers.Select(Escape)) + " |");
        sb.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", columns)));
        for (var i = 0; i < rows.Count; i++)
            sb.AppendLine($"| {i} | " + string.Join(" | ", rows[i].Select(Escape)) + " |");
        return sb.ToString().TrimEnd();
    });

    private static string ReadAsList(AutomationElement element, int maxRows)
    {
        var items = element.FindAllChildren().Take(maxRows).ToList();
        var sb = new StringBuilder($"Element has no Grid pattern; listing {items.Count} child item(s):\n");
        for (var i = 0; i < items.Count; i++)
            sb.AppendLine($"{i}: {ItemText(items[i])}");
        return sb.ToString().TrimEnd();
    }

    private static string CellText(GridCell cell)
    {
        var text = ElementInfo.GetText(cell);
        return string.IsNullOrEmpty(text) ? ItemText(cell) : text;
    }

    /// <summary>Joins the names of an item's descendant text elements, for templated rows and cells.</summary>
    private static string ItemText(AutomationElement item)
    {
        var texts = item.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)
                .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)))
            .Select(ElementInfo.GetText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        return texts.Count > 0 ? string.Join(" / ", texts) : item.Properties.Name.ValueOrDefault ?? "";
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}
