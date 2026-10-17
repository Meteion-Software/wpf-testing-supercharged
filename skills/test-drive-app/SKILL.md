---
name: test-drive-app
description: Test drive a WPF app through the wpf-testing MCP server by following steps the user gives. Use when the user asks to test drive, try out, smoke test, or manually test a WPF app or screen, or to run through a flow in the running app. Finds the built exe of the current UI project, launches it, performs each step, and reports PASS or FAIL.
---

# Test Drive App

Run the user's steps against the real app using the `wpf-testing` MCP tools, and report what happened.

## Steps

1. **Get the steps.** Follow the steps the user gave. If they gave none, or a step is too vague to act on, ask for the steps. Don't invent a flow or explore the app on your own.
2. **Find the exe** for the current UI project, unless `launch_app` already has `WPF_MCP_APP_PATH` set or the user gave a path:
   - Find the UI project: a `.csproj` with `<UseWPF>true</UseWPF>` and `<OutputType>WinExe</OutputType>`, preferring the one nearest the current directory or the files being discussed.
   - If more than one project matches, ask which one.
   - Look for the built exe at `bin/<Configuration>/<TargetFramework>/<AssemblyName>.exe` under that project. Use the most recently built one if several exist. `AssemblyName` defaults to the project file name.
   - If no exe exists, say the app isn't built and ask before running `dotnet build`.
3. **Launch** with `launch_app`, passing `exePath` if you found one.
4. **Look** with `get_ui_tree` so you can address controls by AutomationId.
5. **Perform each step** with the matching tool (`set_text`, `invoke`, `click`, `select_item`, `set_checked`, `press_keys`). Prefer `invoke` over `click` for buttons and menu items. Read the result of every action: it reports the element's new state and any window that opened.
6. **Check outcomes** only where the user stated an expectation, using `assert_element`, or `wait_for` for async loads. Don't add assertions the user didn't ask for. If a step has no stated expectation, report what the action returned.
7. **On a failure**, take a `screenshot`, then stop and report. Don't keep going past a failed step unless the user said to.
8. **Close** with `close_app`, whether the run passed or failed.

## Report

List each step with PASS or FAIL and the relevant tool output. For a failure, include the screenshot path and what you saw instead. End by offering to export the session as an xUnit test with `get_recorded_steps` and the `write-xunit-3-tests` skill.

## When something is missing

- **Controls have no AutomationId:** use `type=Button;name=OK` style selectors for now, and tell the user the `add-automation-ids` skill would make the run more reliable.
- **The MCP server isn't connected:** say so and point to the plugin install instructions in the repo README. Don't try to drive the app another way.
