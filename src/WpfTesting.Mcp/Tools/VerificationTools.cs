using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WpfTesting.Mcp.Tools;

[McpServerToolType]
public sealed class VerificationTools(AppSession session, StepRecorder recorder)
{
    private const string WindowHelp = "Only search top-level windows whose title contains this text.";

    [McpServerTool(Name = "wait_for", ReadOnly = true), Description(
        "Wait until an element reaches a state. Use after actions that load data, open dialogs or run in the background. " +
        "Fails with the element's current state on timeout.")]
    public string WaitFor(
        [Description(Selector.Help)] string selector,
        [Description("exists, gone, visible, hidden, enabled, disabled, text (equals value), textContains (contains value), checked, unchecked, selected.")] string state = "visible",
        [Description("Expected text for the text and textContains states.")] string? value = null,
        [Description("Seconds to wait.")] double timeoutSeconds = 10,
        [Description(WindowHelp)] string? window = null) => session.Run(() =>
    {
        var parsed = Selector.Parse(selector);
        var stopwatch = Stopwatch.StartNew();
        string lastState;
        while (true)
        {
            var element = session.FindAll(parsed, window).Skip(parsed.Index).FirstOrDefault();
            var (met, current) = Check(element, state, value);
            lastState = current;
            if (met)
            {
                recorder.Comment($"Waited for '{selector}' to be {state}{(value is null ? "" : $" \"{value}\"")}");
                return $"'{selector}' is {state} after {stopwatch.ElapsedMilliseconds} ms. {current}";
            }
            if (stopwatch.Elapsed.TotalSeconds >= timeoutSeconds)
                break;
            Thread.Sleep(200);
        }
        throw new McpException($"Timed out after {timeoutSeconds}s waiting for '{selector}' to be {state}{(value is null ? "" : $" \"{value}\"")}. Current: {lastState}");
    });

    [McpServerTool(Name = "assert_element", ReadOnly = true), Description(
        "Verify a test expectation about an element. Retries until every expectation holds or the timeout passes, then returns " +
        "'PASS' or 'FAIL' with details. Only set the expectations you want checked. Passing assertions are recorded as xUnit Assert calls. " +
        "On FAIL, a screenshot of the app's topmost window is attached and saved under WPF_MCP_ARTIFACTS_DIR.")]
    public CallToolResult AssertElement(
        [Description(Selector.Help)] string selector,
        [Description("Expect the element to exist (true) or not exist (false).")] bool? exists = null,
        [Description("Expected exact text (TextBox value, TextBlock text, etc.).")] string? text = null,
        [Description("Expected substring of the text.")] string? textContains = null,
        [Description("Expected enabled state.")] bool? enabled = null,
        [Description("Expected visibility (not offscreen).")] bool? visible = null,
        [Description("Expected checked state of a CheckBox/ToggleButton/RadioButton.")] bool? isChecked = null,
        [Description("Expected selected state of a list item, tab or row.")] bool? selected = null,
        [Description("Seconds to keep retrying before reporting FAIL.")] double timeoutSeconds = 3,
        [Description("Attach and save a screenshot when the assertion fails.")] bool screenshotOnFail = true,
        [Description(WindowHelp)] string? window = null) => session.Run(() =>
    {
        var parsed = Selector.Parse(selector);
        var stopwatch = Stopwatch.StartNew();
        List<string> failures;
        AutomationElement? element;
        while (true)
        {
            element = session.FindAll(parsed, window).Skip(parsed.Index).FirstOrDefault();
            failures = Evaluate(element, exists, text, textContains, enabled, visible, isChecked, selected);
            if (failures.Count == 0 || stopwatch.Elapsed.TotalSeconds >= timeoutSeconds)
                break;
            Thread.Sleep(200);
        }

        if (failures.Count > 0)
        {
            var report = $"FAIL: '{selector}'\n  - " + string.Join("\n  - ", failures);
            return screenshotOnFail ? WithFailureScreenshot(report, selector) : TextResult(report);
        }

        RecordAssertions(element, exists, text, textContains, enabled, visible, isChecked, selected);
        var summary = element is null ? "element does not exist" : ElementInfo.Summarize(element, session.RegisterRef(element));
        return TextResult($"PASS: '{selector}' ({summary})");
    });

    [McpServerTool(Name = "screenshot", ReadOnly = true), Description(
        "Capture a PNG of an element, a window, or the app's main window. Use to visually check layout or to attach evidence of a failure.")]
    public CallToolResult Screenshot(
        [Description("Element to capture. Defaults to the main window, or the window matching 'window'. " + Selector.Help)] string? selector = null,
        [Description(WindowHelp)] string? window = null,
        [Description("Optional file path to also save the PNG to. Relative paths go under WPF_MCP_ARTIFACTS_DIR.")] string? savePath = null) => session.Run(() =>
    {
        var target = selector is not null
            ? session.Find(selector, window)
            : window is not null
                ? session.GetTopLevelElements(window).FirstOrDefault() ?? throw new McpException($"No window title contains '{window}'.")
                : session.GetMainWindow();

        var (bytes, note) = CapturePng(target, savePath);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = note }, ImageContentBlock.FromBytes(bytes, "image/png")],
        };
    });

    [McpServerTool(Name = "get_recorded_steps", ReadOnly = true), Description(
        "Return the actions and passing assertions performed since launch_app as FlaUI C# statements, addressed by AutomationId. " +
        "Use this to turn an exploratory session into an xUnit test (see the write-xunit-3-tests and scaffold-driver-object skills).")]
    public string GetRecordedSteps(
        [Description("Clear the recording after returning it.")] bool clear = false)
    {
        var steps = recorder.Render();
        if (clear)
            recorder.Clear();
        return steps;
    }

    /// <summary>Captures the element as PNG, optionally saving it to disk, and describes what was captured.</summary>
    private (byte[] Png, string Note) CapturePng(AutomationElement target, string? savePath)
    {
        session.BringToFront(target);
        using var image = Capture.Element(target);
        using var stream = new MemoryStream();
        image.Bitmap.Save(stream, ImageFormat.Png);
        var bytes = stream.ToArray();

        var note = $"Captured {ElementInfo.Summarize(target, session.RegisterRef(target))} ({image.Bitmap.Width}x{image.Bitmap.Height}).";
        if (savePath is not null)
        {
            var path = Path.GetFullPath(Path.IsPathRooted(savePath) ? savePath : Path.Combine(session.Options.ArtifactsDirectory, savePath));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            note += $" Saved to {path}.";
        }
        return (bytes, note);
    }

    /// <summary>
    /// Attaches a screenshot of the app's topmost window (a dialog if one is open) to a FAIL report.
    /// A capture error is noted in the report rather than hiding the assertion result.
    /// </summary>
    private CallToolResult WithFailureScreenshot(string report, string selector)
    {
        try
        {
            var target = session.GetTopLevelElements().FirstOrDefault(e => e.ControlType == ControlType.Window)
                ?? session.GetMainWindow();
            var name = $"fail-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{SafeFileName(selector)}.png";
            var (bytes, note) = CapturePng(target, name);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"{report}\n{note}" }, ImageContentBlock.FromBytes(bytes, "image/png")],
            };
        }
        catch (Exception ex)
        {
            return TextResult($"{report}\n(Failure screenshot not captured: {ex.Message})");
        }
    }

    private static CallToolResult TextResult(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static string SafeFileName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['@', '=', ';', '#', ' ']).ToHashSet();
        var safe = new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('_');
        return safe.Length > 40 ? safe[..40] : safe;
    }

    private static (bool Met, string Current) Check(AutomationElement? element, string state, string? value)
    {
        if (element is null)
            return (state.Equals("gone", StringComparison.OrdinalIgnoreCase), "element does not exist");

        string current;
        try
        {
            current = ElementInfo.Summarize(element, null);
        }
        catch
        {
            return (state.Equals("gone", StringComparison.OrdinalIgnoreCase), "element is no longer available");
        }

        var met = state.ToLowerInvariant() switch
        {
            "exists" => true,
            "gone" => false,
            "visible" => ElementInfo.IsVisible(element),
            "hidden" => !ElementInfo.IsVisible(element),
            "enabled" => element.Properties.IsEnabled.ValueOrDefault,
            "disabled" => !element.Properties.IsEnabled.ValueOrDefault,
            "text" => ElementInfo.GetText(element) == (value ?? throw new McpException("The text state requires value.")),
            "textcontains" => ElementInfo.GetText(element).Contains(value ?? throw new McpException("The textContains state requires value."), StringComparison.Ordinal),
            "checked" => ElementInfo.GetToggleState(element) == ToggleState.On || ElementInfo.GetIsSelected(element) == true,
            "unchecked" => ElementInfo.GetToggleState(element) == ToggleState.Off || ElementInfo.GetIsSelected(element) == false,
            "selected" => ElementInfo.GetIsSelected(element) == true,
            _ => throw new McpException($"Unknown state '{state}'."),
        };
        return (met, current);
    }

    private static List<string> Evaluate(
        AutomationElement? element, bool? exists, string? text, string? textContains,
        bool? enabled, bool? visible, bool? isChecked, bool? selected)
    {
        var failures = new List<string>();
        if (element is null)
        {
            if (exists != false)
                failures.Add("expected element to exist, but it was not found");
            return failures;
        }
        if (exists == false)
        {
            failures.Add($"expected element not to exist, but found {ElementInfo.Summarize(element, null)}");
            return failures;
        }

        var actualText = text is not null || textContains is not null ? ElementInfo.GetText(element) : null;
        if (text is not null && actualText != text)
            failures.Add($"text: expected \"{text}\", actual \"{actualText}\"");
        if (textContains is not null && !actualText!.Contains(textContains, StringComparison.Ordinal))
            failures.Add($"text: expected to contain \"{textContains}\", actual \"{actualText}\"");
        if (enabled is { } e && element.Properties.IsEnabled.ValueOrDefault != e)
            failures.Add($"enabled: expected {e}, actual {!e}");
        if (visible is { } v && ElementInfo.IsVisible(element) != v)
            failures.Add($"visible: expected {v}, actual {!v}");
        if (isChecked is { } c)
        {
            var actual = ElementInfo.GetToggleState(element) is { } t ? t == ToggleState.On : ElementInfo.GetIsSelected(element);
            if (actual is null)
                failures.Add("checked: element supports neither Toggle nor SelectionItem");
            else if (actual != c)
                failures.Add($"checked: expected {c}, actual {actual}");
        }
        if (selected is { } s)
        {
            var actual = ElementInfo.GetIsSelected(element);
            if (actual is null)
                failures.Add("selected: element does not support SelectionItem");
            else if (actual != s)
                failures.Add($"selected: expected {s}, actual {actual}");
        }
        return failures;
    }

    private void RecordAssertions(
        AutomationElement? element, bool? exists, string? text, string? textContains,
        bool? enabled, bool? visible, bool? isChecked, bool? selected)
    {
        if (element is null)
        {
            recorder.Comment("Assert: element does not exist (use Retry.WhileNotNull with FindFirstDescendant in the test)");
            return;
        }

        var locate = StepRecorder.Locate(element);
        if (exists == true)
            recorder.Raw($"Assert.NotNull({locate});");
        if (text is not null)
            recorder.Raw($"Assert.Equal({StepRecorder.Literal(text)}, {TextExpression(element, locate)});");
        if (textContains is not null)
            recorder.Raw($"Assert.Contains({StepRecorder.Literal(textContains)}, {TextExpression(element, locate)});");
        if (enabled is { } e)
            recorder.Raw($"Assert.{(e ? "True" : "False")}({locate}.IsEnabled);");
        if (visible is { } v)
            recorder.Raw($"Assert.{(v ? "False" : "True")}({locate}.IsOffscreen);");
        if (isChecked is { } c)
            recorder.Raw(element.Patterns.Toggle.IsSupported
                ? $"Assert.Equal({(c ? "true" : "false")}, {locate}.AsCheckBox().IsChecked);"
                : $"Assert.{(c ? "True" : "False")}({locate}.AsRadioButton().IsChecked);");
        if (selected is { } s)
            recorder.Raw($"Assert.{(s ? "True" : "False")}({locate}.Patterns.SelectionItem.Pattern.IsSelected.Value);");
    }

    private static string TextExpression(AutomationElement element, string locate) =>
        element.Patterns.Value.IsSupported ? $"{locate}.AsTextBox().Text" : $"{locate}.Name";
}
