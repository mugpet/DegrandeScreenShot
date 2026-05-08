---
name: restart-latest-build
description: 'Repo-specific rule for DegrandeScreenShot: after code changes, always stop the running app and restart the newest built app so the user tests the latest build.'
---

# Restart Latest Build

Use this skill whenever changes are made to the DegrandeScreenShot app and the user needs to verify behavior in the running desktop app.

## Required behavior

- Never leave an older `DegrandeScreenShot.App` process running when asking the user to test.
- Always stop any existing app instance before launching a new one.
- Prefer launching the latest `Release` build for user verification.
- Treat build-plus-restart as part of finishing the task, not an optional follow-up.

## Restart flow

```powershell
Get-Process DegrandeScreenShot.App -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build .\src\DegrandeScreenShot.App\DegrandeScreenShot.App.csproj -c Release
Start-Process .\src\DegrandeScreenShot.App\bin\Release\net9.0-windows\DegrandeScreenShot.App.exe
```

## Verification

Confirm that the running process path points to:

`src\\DegrandeScreenShot.App\\bin\\Release\\net9.0-windows\\DegrandeScreenShot.App.exe`

If the running process still points to `Debug`, the restart did not satisfy this skill and must be corrected before telling the user to test.
