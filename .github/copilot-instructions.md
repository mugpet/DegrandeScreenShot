# DegrandeScreenShot Repo Instructions

- After any code change intended for user testing, always stop any running `DegrandeScreenShot.App` process before launching the app again.
- Always restart the latest built app so the user is testing the newest build, not a stale running instance.
- Prefer a `Release` build for user-facing verification unless a task explicitly requires `Debug`.
- Do not tell the user to test without restarting the app first; restart it yourself.
- Verified restart flow for this repo:

```powershell
Get-Process DegrandeScreenShot.App -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build .\src\DegrandeScreenShot.App\DegrandeScreenShot.App.csproj -c Release
Start-Process .\src\DegrandeScreenShot.App\bin\Release\net9.0-windows\DegrandeScreenShot.App.exe
```
