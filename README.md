# VR Privacy

Small Windows GUI for one temporary SudoVDA virtual monitor.

It reuses the SudoVDA driver installed by Apollo, creates an app-owned display, optionally makes it primary, routes newly opened top-level windows to it, and restores the original display layout when stopped.

## Requirements

- Windows 10/11 x64
- .NET 10 Desktop Runtime or SDK
- Installed SudoVDA protocol 0.2+ (Apollo installation satisfies this)

The app does not install, update, reload, or remove the driver. Apollo may remain running.

## Build and run

```powershell
dotnet build src\VrPrivacy\VrPrivacy.csproj -c Release
dotnet run --project src\VrPrivacy\VrPrivacy.csproj
```

UI controls:

- `Resolution preset`: choose `Copy primary`, a discovered/common resolution, or `Custom`.
- `Width` / `Height`: freely typed integer dimensions. Valid ranges are 640–7680 and 480–4320.
- `Refresh rate`: choose a supported integer rate from the dropdown.
- `Make primary`: temporarily makes the virtual monitor primary. Enabled by default for fullscreen-game compatibility.
- `Route new windows`: moves eligible top-level windows first shown after start. Enabled by default.
- `Start` / `Stop`: creates or removes the app-owned monitor.

Selecting a preset populates Width and Height. Editing either dimension selects `Custom`. Form choices persist per user in `HKCU\Software\VRPrivacy`; `Copy primary` rereads the primary mode on the next launch.

Closing the app performs the same cleanup as `Stop`.

## Verification

Form, protocol, and temporary-registry checks (no display topology mutation):

```powershell
dotnet run --project src\VrPrivacy\VrPrivacy.csproj -- --self-test
```

Live lifecycle smoke test:

```powershell
dotnet run --project src\VrPrivacy\VrPrivacy.csproj -- --smoke-test
```

The live test briefly creates a copied-mode monitor, routes a separate test window, makes the virtual monitor primary, removes it, and waits until the exact original topology returns. Displays and taskbar may move briefly.

## POC limits

- One virtual monitor.
- Existing windows are not moved when routing starts.
- Secure desktop, shell UI, elevated windows, and anti-cheat-protected games are not guaranteed to move.
- Normal stop/exit restores the captured topology. A hard process kill can leave the monitor until SudoVDA watchdog cleanup or the next explicit removal; Apollo sharing the same driver may affect watchdog timing.
- The monitor GUID belongs only to this app. Foreign Apollo/SudoVDA monitors are never removed.
