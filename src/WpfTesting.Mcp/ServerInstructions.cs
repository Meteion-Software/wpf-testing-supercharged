namespace WpfTesting.Mcp;

internal static class ServerInstructions
{
    public const string Text =
        """
        Drives a WPF desktop application through UI Automation (FlaUI UIA3) so you can run UI tests.

        When the user asks to test, test drive, try out or smoke test the app, use this flow with the steps they give you.
        Don't ask what they mean. Ask only if they gave no steps, or if launch_app can't tell which project to build.

        Typical test flow:
        1. launch_app. It builds the WPF project in Debug and launches it, so don't run dotnet build yourself. The project
           comes from projectPath, WPF_MCP_PROJECT_PATH, or is found from the working directory. Pass defineConstants
           (for example ["AUTOMATED_TEST"]) when the app uses compile symbols for test-only behavior such as skipping
           sign-in. Pass exePath to launch an exe you already built, without building.
        2. get_ui_tree to see the controls. Each line shows [ref] ControlType #AutomationId "Name" and state.
        3. Act with set_text, click/invoke, select_item, set_checked, press_keys. Every action reports the element's new
           state and any dialog that opened.
        4. Check outcomes with assert_element (returns PASS/FAIL) or wait_for when something loads asynchronously.
        5. screenshot to capture evidence of a failure, and get_app_output to read the app's stdout, stderr and
           Debug.WriteLine output (it still works after a crash).
        6. close_app at the end so the next test starts clean.

        Selectors: prefer AutomationIds ('LoginButton'); the app's XAML has them from the add-automation-ids skill.
        Fall back to '@ref' from get_ui_tree, or 'type=Button;name=OK' for elements without an id (e.g. MessageBox buttons).
        Lookups search every top-level window of the app, including dialogs and popups, and retry briefly until found.
        Prefer invoke over click for buttons and menu items; it does not depend on mouse position or window focus.

        Report each test as PASS or FAIL with the assert_element output. get_recorded_steps returns the session as FlaUI C#
        that can be turned into an xUnit test using the project's driver objects.
        """;
}
