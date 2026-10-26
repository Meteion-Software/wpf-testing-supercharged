---
name: test-drive-app
description: Test drive a WPF app through the wpf-testing MCP server by following steps the user gives. Use when the user asks to test drive, try out, smoke test, or manually test a WPF app or screen, or to run through a flow in the running app. Builds the current UI project in Debug, launches it, performs each step, and reports PASS or FAIL.
---

# Test Drive App

Run the user's steps against the real app using the `wpf-testing` MCP tools, and report what happened.

## Steps

1. **Get the steps.** Follow the steps the user gave. If they gave none, or a step is too vague to act on, ask for the steps. Don't invent a flow or explore the app on your own.
2. **Launch with `launch_app`.** It builds the WPF project in Debug and launches the result, so don't run `dotnet build` yourself or look for the exe.
   - With no arguments it uses `WPF_MCP_PROJECT_PATH` if set, otherwise it finds the WPF project (`UseWPF` and `WinExe`) from the working directory. If it reports more than one project, ask the user which one, then pass `projectPath`.
   - If the app uses compile symbols for test-only behavior, such as skipping authentication with `#if AUTOMATED_TEST`, pass them in `defineConstants`. Use a symbol the user named, or one you can see in the app's source. Don't guess symbol names.
   - Pass `exePath` only when the user points you at an exe they built themselves.
   - If the build fails, report the compiler errors and stop.
3. **Look** with `get_ui_tree` so you can address controls by AutomationId.
4. **Perform each step** with the matching tool (`set_text`, `invoke`, `click`, `select_item`, `set_checked`, `press_keys`). Prefer `invoke` over `click` for buttons and menu items. Read the result of every action: it reports the element's new state and any window that opened.
5. **Check outcomes** only where the user stated an expectation, using `assert_element`, or `wait_for` for async loads. Don't add assertions the user didn't ask for. If a step has no stated expectation, report what the action returned.
6. **On a failure**, take a `screenshot` and read `get_app_output` for errors the app logged, then stop and report. Don't keep going past a failed step unless the user said to.
7. **Close** with `close_app`, whether the run passed or failed.

## Report

List each step with PASS or FAIL and the relevant tool output. For a failure, include the screenshot path, what you saw instead, and any relevant lines from the app's output. End by offering to export the session as an xUnit test with `get_recorded_steps` and the `write-xunit-3-tests` skill.

## When something is missing

- **Controls have no AutomationId:** use `type=Button;name=OK` style selectors for now, and tell the user the `add-automation-ids` skill would make the run more reliable.
- **The MCP server isn't connected:** say so and point to the plugin install instructions in the repo README. Don't try to drive the app another way.
