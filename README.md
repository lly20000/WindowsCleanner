# WindowsCleanner

A C# / .NET 9 Windows cleanup tool with a desktop UI. It scans common temporary-file locations, estimates how much space can be freed, and lets you clean only the items you choose.

## Current cleanup targets

- `%LOCALAPPDATA%\Temp`
- `%WINDIR%\Temp`
- `%WINDIR%\SoftwareDistribution\Download`
- `%WINDIR%\Prefetch`
- `%LOCALAPPDATA%\CrashDumps`

## Run

```powershell
dotnet run --project .\WindowsCleannerApp\WindowsCleannerApp.csproj
```

The app opens a WinForms window where you can:

- rescan common cleanup locations
- review file counts and estimated reclaimable space
- choose specific targets to clean
- confirm cleanup before deletion

## Notes

- Locked or unauthorized files are skipped and reported in the UI.
- Some system paths may clean more completely when the app is started as Administrator.
- The project keeps the cleanup logic separate from the UI, so adding more cleanup targets later is straightforward.
