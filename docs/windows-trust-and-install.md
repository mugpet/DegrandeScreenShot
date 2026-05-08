# Windows trust and installability

Windows and company-managed PCs decide whether an app is trusted based on code signing, reputation, and device policy. An unsigned app can still be safe, but SmartScreen, Defender, AppLocker, WDAC, Intune, or endpoint protection may block it.

## Best path: sign releases

Use a code-signing certificate that is trusted by the target machine. For company PCs, the most reliable option is usually one of these:

- An OV or EV code-signing certificate from a public certificate authority.
- A company-issued code-signing certificate whose root is already trusted by corporate devices.
- An internal certificate that IT deploys to trusted publishers through policy.

The release workflow signs the published app and the installer when these GitHub repository secrets are present:

- `WINDOWS_CODESIGN_CERTIFICATE_BASE64`: base64 content of a `.pfx` code-signing certificate.
- `WINDOWS_CODESIGN_CERTIFICATE_PASSWORD`: password for that `.pfx` file.

Create the base64 value locally with PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\to\codesign.pfx")) | Set-Clipboard
```

After the secrets are configured, pushing a `vX.Y.Z` tag builds signed release artifacts. The portable zip contains a signed app executable, and the installer executable is signed too.

## If the company PC still blocks it

If company policy requires approved software, signing may not be enough by itself. Ask IT to allow one of these, in order of preference:

- The certificate publisher used to sign Degrande ScreenShot.
- The signed installer from the GitHub release.
- The portable zip or app executable hash for a specific release.

## Runnable fallback

If installers are blocked but portable apps are allowed, use the release portable zip instead of the installer. Extract it under a user-writable folder such as `%LOCALAPPDATA%\Programs\Degrande ScreenShot` and run `DegrandeScreenShot.App.exe`.

If Windows marks the downloaded zip as internet-originated and policy allows unblocking, right-click the zip, open **Properties**, select **Unblock**, then extract it. The PowerShell equivalent is:

```powershell
Unblock-File .\DegrandeScreenShot-0.2.1-win-x64-portable.zip
```

If `Unblock-File` or the executable is blocked by policy, only IT can approve it for that device.