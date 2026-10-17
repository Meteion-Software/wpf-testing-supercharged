---
name: add-flaui-to-project
description: Add the correct FlaUI NuGet package to a .NET UI test project, using FlaUI.UIA2 for WinForms apps and FlaUI.UIA3 for everything else. Use when the user asks to add FlaUI, set up UI automation, or create a UI test project for a Windows desktop app.
---

# Add FlaUI to Project

Add FlaUI to the test project, choosing the UI Automation backend based on the app under test.

## Steps

1. **Find the test project.** Use the project the user names. If they don't name one, look for a `.csproj` that references a test framework (xUnit, NUnit, MSTest). If there is none or more than one candidate, ask the user rather than guessing.
2. **Find the app under test.** This is the project the UI tests will launch. Ask if it isn't clear.
3. **Detect the UI framework of the app under test.** Read its `.csproj`:
   - `<UseWindowsForms>true</UseWindowsForms>` means WinForms. Use **FlaUI.UIA2**.
   - Anything else (`<UseWPF>true</UseWPF>`, WinUI, or unknown) means use **FlaUI.UIA3**.
   - If the project sets both `UseWindowsForms` and `UseWPF`, don't pick silently. Ask the user.
4. **Check for existing FlaUI packages** in the test project. If the other backend is already referenced, tell the user instead of adding a second one. UIA2 and UIA3 should not both be referenced.
5. **Add the package** with the .NET CLI from the test project's directory:

   ```powershell
   dotnet add package FlaUI.UIA3   # or FlaUI.UIA2 for WinForms
   ```

   The backend package brings in `FlaUI.Core` transitively, so don't add it separately.
6. **Check the target framework.** The test project needs a Windows target framework (e.g. `net8.0-windows`). If it doesn't have one, tell the user and don't change it without asking.
7. **Report** which backend was chosen, why, and the package version that was installed.

## Backend usage

Tell the user which automation class to construct, since it differs by backend:

```csharp
using var automation = new UIA3Automation(); // FlaUI.UIA3
using var automation = new UIA2Automation(); // FlaUI.UIA2 (WinForms)
```

Don't scaffold test code. That's the job of the other skills.
