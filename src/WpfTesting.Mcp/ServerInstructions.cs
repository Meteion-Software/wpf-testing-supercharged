namespace WpfTesting.Mcp;

internal static class ServerInstructions
{
    public const string Text =
        """
        Drives a WPF desktop application through UI Automation (FlaUI UIA3) so you can run UI tests.

        Typical test flow:
        1. launch_app (exePath can be omitted when WPF_MCP_APP_PATH is set).
        2. get_ui_tree to see the controls. Each line shows [ref] ControlType #AutomationId "Name" and state.
        3. Act with set_text, click/invoke, select_item, set_checked, press_keys. Every action reports the element's new
           state and any dialog that opened.
        4. Check outcomes with assert_element (returns PASS/FAIL) or wait_for when something loads asynchronously.
        5. screenshot to capture evidence of a failure.
        6. close_app at the end so the next test starts clean.

        Selectors: prefer AutomationIds ('LoginButton'); the app's XAML has them from the add-automation-ids skill.
        Fall back to '@ref' from get_ui_tree, or 'type=Button;name=OK' for elements without an id (e.g. MessageBox buttons).
        Lookups search every top-level window of the app, including dialogs and popups, and retry briefly until found.
        Prefer invoke over click for buttons and menu items; it does not depend on mouse position or window focus.

        Report each test as PASS or FAIL with the assert_element output. get_recorded_steps returns the session as FlaUI C#
        that can be turned into an xUnit test using the project's driver objects.
        """;
}
