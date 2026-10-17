using System.ComponentModel;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace WpfTesting.Mcp.Tools;

[McpServerToolType]
public sealed class InteractionTools(AppSession session, StepRecorder recorder)
{
    private const string WindowHelp = "Only search top-level windows whose title contains this text.";

    [McpServerTool(Name = "click"), Description(
        "Click an element with the real mouse, after bringing its window to the front. Waits for the element to appear. " +
        "Reports any window or dialog that opened as a result.")]
    public string Click(
        [Description(Selector.Help)] string selector,
        [Description("left, right or double.")] string button = "left",
        [Description(WindowHelp)] string? window = null) => Act(selector, window, element =>
    {
        session.BringToFront(element);
        switch (button.ToLowerInvariant())
        {
            case "left":
                element.Click();
                recorder.Action(element, ".Click()");
                break;
            case "right":
                element.RightClick();
                recorder.Action(element, ".RightClick()");
                break;
            case "double":
                element.DoubleClick();
                recorder.Action(element, ".DoubleClick()");
                break;
            default:
                throw new McpException($"Unknown button '{button}'. Use left, right or double.");
        }
        return $"Clicked ({button})";
    });

    [McpServerTool(Name = "invoke"), Description(
        "Activate an element through UI Automation (Invoke, Toggle or SelectionItem pattern) without moving the mouse. " +
        "More reliable than click for buttons, menu items, hyperlinks and checkboxes, and works when the window is covered.")]
    public string Invoke(
        [Description(Selector.Help)] string selector,
        [Description(WindowHelp)] string? window = null) => Act(selector, window, element =>
    {
        var p = element.Patterns;
        if (p.Invoke.TryGetPattern(out var invoke))
        {
            invoke.Invoke();
            recorder.Action(element, ".AsButton().Invoke()");
            return "Invoked";
        }
        if (p.Toggle.TryGetPattern(out var toggle))
        {
            toggle.Toggle();
            recorder.Action(element, ".AsCheckBox().Toggle()");
            return "Toggled";
        }
        if (p.SelectionItem.TryGetPattern(out var item))
        {
            item.Select();
            recorder.Action(element, ".Patterns.SelectionItem.Pattern.Select()");
            return "Selected";
        }
        if (p.ExpandCollapse.TryGetPattern(out var expand))
        {
            expand.Expand();
            recorder.Action(element, ".Patterns.ExpandCollapse.Pattern.Expand()");
            return "Expanded";
        }
        throw new McpException("Element supports no Invoke, Toggle, SelectionItem or ExpandCollapse pattern. Use click instead.");
    });

    [McpServerTool(Name = "set_text"), Description(
        "Replace the text of a TextBox, PasswordBox, editable ComboBox or similar. " +
        "Uses the Value pattern when possible, otherwise focuses the element, selects all and types.")]
    public string SetText(
        [Description(Selector.Help)] string selector,
        [Description("The new text. Empty string clears the field.")] string text,
        [Description("auto (Value pattern, falling back to keyboard), value, or keyboard. Use keyboard when the app reacts to key events or binds with UpdateSourceTrigger=PropertyChanged and value doesn't trigger it.")] string mode = "auto",
        [Description("Press Tab afterwards so LostFocus bindings and validation run.")] bool tabOut = false,
        [Description(WindowHelp)] string? window = null) => Act(selector, window, element =>
    {
        var canUseValue = element.Patterns.Value.TryGetPattern(out var value) && !value.IsReadOnly.ValueOrDefault;
        var useValue = mode.ToLowerInvariant() switch
        {
            "auto" => canUseValue,
            "value" => canUseValue ? true : throw new McpException("Element has no writable Value pattern. Use mode 'keyboard'."),
            "keyboard" => false,
            _ => throw new McpException($"Unknown mode '{mode}'. Use auto, value or keyboard."),
        };

        if (useValue)
        {
            value!.SetValue(text);
        }
        else
        {
            session.BringToFront(element);
            element.Focus();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            if (text.Length == 0)
                Keyboard.Type(VirtualKeyShort.DELETE);
            else
                Keyboard.Type(text);
        }
        recorder.Action(element, $".AsTextBox().Enter({StepRecorder.Literal(text)})");

        if (tabOut)
        {
            if (useValue)
            {
                session.BringToFront(element);
                element.Focus();
            }
            Keyboard.Type(VirtualKeyShort.TAB);
            recorder.Raw("Keyboard.Type(VirtualKeyShort.TAB);");
        }
        return $"Set text via {(useValue ? "Value pattern" : "keyboard")}";
    });

    [McpServerTool(Name = "type_text"), Description(
        "Type literal text with the keyboard into the focused element, or into the given element after focusing it. Does not clear existing text.")]
    public string TypeText(
        [Description("Text to type.")] string text,
        [Description("Optional element to focus first. " + Selector.Help)] string? selector = null,
        [Description(WindowHelp)] string? window = null) => ActOptional(selector, window, element =>
    {
        if (element is not null)
        {
            session.BringToFront(element);
            element.Focus();
            recorder.Action(element, ".Focus()");
        }
        else
        {
            session.EnsureAppHasFocus();
        }
        Keyboard.Type(text);
        recorder.Raw($"Keyboard.Type({StepRecorder.Literal(text)});");
        return $"Typed {text.Length} character(s)";
    });

    [McpServerTool(Name = "press_keys"), Description(
        "Press keys or shortcuts, optionally after focusing an element. " + KeyParser.Help)]
    public string PressKeys(
        [Description("Keys to press, e.g. 'Ctrl+S' or 'Tab Tab Enter'.")] string keys,
        [Description("Optional element to focus first. " + Selector.Help)] string? selector = null,
        [Description(WindowHelp)] string? window = null) => ActOptional(selector, window, element =>
    {
        var chords = KeyParser.Parse(keys);
        if (element is not null)
        {
            session.BringToFront(element);
            element.Focus();
            recorder.Action(element, ".Focus()");
        }
        else
        {
            session.EnsureAppHasFocus();
        }

        foreach (var chord in chords)
        {
            var args = string.Join(", ", chord.Select(k => $"VirtualKeyShort.{k}"));
            if (chord.Length == 1)
            {
                Keyboard.Type(chord[0]);
                recorder.Raw($"Keyboard.Type({args});");
            }
            else
            {
                Keyboard.TypeSimultaneously(chord);
                recorder.Raw($"Keyboard.TypeSimultaneously({args});");
            }
            Wait.UntilInputIsProcessed();
        }
        return $"Pressed {keys}";
    });

    [McpServerTool(Name = "set_checked"), Description(
        "Set a CheckBox, ToggleButton or RadioButton to a specific state. Idempotent: does nothing if already in that state.")]
    public string SetChecked(
        [Description(Selector.Help)] string selector,
        [Description("on, off or indeterminate.")] string state = "on",
        [Description(WindowHelp)] string? window = null) => Act(selector, window, element =>
    {
        var target = state.ToLowerInvariant() switch
        {
            "on" or "true" or "checked" => ToggleState.On,
            "off" or "false" or "unchecked" => ToggleState.Off,
            "indeterminate" or "null" => ToggleState.Indeterminate,
            _ => throw new McpException($"Unknown state '{state}'. Use on, off or indeterminate."),
        };

        if (element.Patterns.Toggle.TryGetPattern(out var toggle))
        {
            for (var i = 0; i < 3 && toggle.ToggleState.Value != target; i++)
                toggle.Toggle();
            if (toggle.ToggleState.Value != target)
                throw new McpException($"Could not reach state {target}; the control is at {toggle.ToggleState.Value}. It may not be three-state.");
            var isChecked = target switch { ToggleState.On => "true", ToggleState.Off => "false", _ => "null" };
            recorder.Action(element, $".AsCheckBox().IsChecked = {isChecked}");
            return $"State is {target}";
        }
        if (element.Patterns.SelectionItem.TryGetPattern(out var radio))
        {
            if (target != ToggleState.On)
                throw new McpException("A RadioButton can only be turned on; select a different option to turn this one off.");
            radio.Select();
            recorder.Action(element, ".AsRadioButton().IsChecked = true");
            return "Selected";
        }
        throw new McpException("Element supports neither Toggle nor SelectionItem pattern.");
    });

    [McpServerTool(Name = "select_item"), Description(
        "Select an item in a ComboBox, ListBox, ListView, DataGrid, TreeView or TabControl, by its text or index. " +
        "For DataGrid rows, the text can match any cell in the row.")]
    public string SelectItem(
        [Description("The container. " + Selector.Help)] string selector,
        [Description("Item text to match (exact first, then contains). Omit to use index.")] string? item = null,
        [Description("0-based item index, used when item is omitted.")] int? index = null,
        [Description("Add to the existing selection instead of replacing it (multi-select lists).")] bool addToSelection = false,
        [Description(WindowHelp)] string? window = null) => Act(selector, window, element =>
    {
        if (item is null && index is null)
            throw new McpException("Provide item or index.");
        var arg = item is not null ? StepRecorder.Literal(item) : index!.Value.ToString();

        switch (element.Properties.ControlType.ValueOrDefault)
        {
            case ControlType.ComboBox:
            {
                var combo = element.AsComboBox();
                var selected = item is not null ? combo.Select(item) : combo.Select(index!.Value);
                combo.Collapse();
                recorder.Action(element, $".AsComboBox().Select({arg})");
                return $"Selected \"{selected?.Text}\"";
            }
            case ControlType.Tab:
            {
                var tab = element.AsTab();
                var selected = item is not null ? tab.SelectTabItem(item) : tab.SelectTabItem(index!.Value);
                recorder.Action(element, $".AsTab().SelectTabItem({arg})");
                return $"Selected tab \"{selected?.Name}\"";
            }
        }

        var candidates = SelectableItems(element);
        if (candidates.Count == 0)
            throw new McpException("Container has no selectable items (no children with the SelectionItem pattern). Try click on the item instead.");

        var target = item is null
            ? (index < candidates.Count ? candidates[index.Value] : throw new McpException($"Index {index} is out of range; {candidates.Count} item(s) available."))
            : MatchItem(candidates, item)
              ?? throw new McpException($"No item matches \"{item}\". Items: {string.Join(", ", candidates.Take(20).Select(c => $"\"{ElementInfo.GetText(c)}\""))}");

        if (target.Patterns.ScrollItem.TryGetPattern(out var scroll))
            scroll.ScrollIntoView();
        var selectionItem = target.Patterns.SelectionItem.Pattern;
        if (addToSelection)
            selectionItem.AddToSelection();
        else
            selectionItem.Select();

        recorder.Action(element, $".AsListBox().{(addToSelection ? "AddToSelection" : "Select")}({arg})");
        return $"Selected \"{ElementInfo.GetText(target)}\"";
    });

    [McpServerTool(Name = "expand_collapse"), Description(
        "Expand or collapse a TreeViewItem, Expander, ComboBox or MenuItem.")]
    public string ExpandCollapse(
        [Description(Selector.Help)] string selector,
        [Description("true to expand, false to collapse.")] bool expand = true,
        [Description(WindowHelp)] string? window = null) => Act(selector, window, element =>
    {
        if (!element.Patterns.ExpandCollapse.TryGetPattern(out var pattern))
            throw new McpException("Element does not support the ExpandCollapse pattern.");
        if (expand)
            pattern.Expand();
        else
            pattern.Collapse();
        recorder.Action(element, $".Patterns.ExpandCollapse.Pattern.{(expand ? "Expand" : "Collapse")}()");
        return expand ? "Expanded" : "Collapsed";
    });

    [McpServerTool(Name = "focus"), Description("Give keyboard focus to an element and bring its window to the front.")]
    public string Focus(
        [Description(Selector.Help)] string selector,
        [Description(WindowHelp)] string? window = null) => Act(selector, window, element =>
    {
        session.BringToFront(element);
        element.Focus();
        recorder.Action(element, ".Focus()");
        return "Focused";
    });

    private string Act(string selector, string? window, Func<AutomationElement, string> action) =>
        ActOptional(selector, window, element => action(element!));

    /// <summary>Runs an action, then reports the element's new state and any windows that appeared or closed.</summary>
    private string ActOptional(string? selector, string? window, Func<AutomationElement?, string> action) => session.Run(() =>
    {
        var before = WindowHandles();
        var element = selector is null ? null : session.Find(selector, window);
        var result = action(element);
        Wait.UntilInputIsProcessed();

        var sb = new StringBuilder(result);
        if (!session.HasApp)
            return sb.Append(". The application has exited.").ToString();

        if (element is not null)
        {
            try
            {
                if (element.IsAvailable)
                    sb.Append(". Now: ").Append(ElementInfo.Summarize(element, session.RegisterRef(element)));
            }
            catch
            {
                // Element went away (e.g. its dialog closed); not an error.
            }
        }

        var after = session.GetTopLevelElements();
        foreach (var top in after.Where(t => !before.Contains(Handle(t))))
            sb.Append("\nNew window: ").Append(ElementInfo.Summarize(top, session.RegisterRef(top)));
        var closed = before.Count - after.Count(t => before.Contains(Handle(t)));
        if (closed > 0)
            sb.Append($"\n{closed} window(s) closed.");
        return sb.ToString();
    });

    private HashSet<IntPtr> WindowHandles() =>
        session.HasApp ? session.GetTopLevelElements().Select(Handle).ToHashSet() : [];

    private static IntPtr Handle(AutomationElement element)
    {
        try
        {
            return element.Properties.NativeWindowHandle.ValueOrDefault;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static List<AutomationElement> SelectableItems(AutomationElement container)
    {
        var children = container.FindAllChildren().Where(c => c.Patterns.SelectionItem.IsSupported).ToList();
        if (children.Count > 0)
            return children;
        // TreeView items and grouped lists nest their items deeper.
        return container.FindAllDescendants().Where(c => c.Patterns.SelectionItem.IsSupported).ToList();
    }

    private static AutomationElement? MatchItem(List<AutomationElement> items, string text)
    {
        bool Exact(AutomationElement e) =>
            string.Equals(ElementInfo.GetText(e), text, StringComparison.OrdinalIgnoreCase)
            || e.FindFirstDescendant(cf => cf.ByName(text)) is not null;

        return items.FirstOrDefault(Exact)
            ?? items.FirstOrDefault(e => ElementInfo.GetText(e).Contains(text, StringComparison.OrdinalIgnoreCase));
    }
}
