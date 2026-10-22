param([Parameter(Mandatory=$true)][string]$GameDirectory, [Parameter(Mandatory=$true)][string]$AnchorChainDirectory)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Write-Output 'Starting the separate game-Mono verification process.'
$taskRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'prepare-game-fixture.ps1') -GameDirectory $GameDirectory
$taskFixture = Join-Path $taskRoot 'artifacts\game-fixture'
$taskManaged = Join-Path $GameDirectory 'Sea Power_Data\Managed'
$taskRuntime = Join-Path $GameDirectory 'MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll'
$taskTests = Join-Path $taskRoot 'tests\bin\Release\netstandard2.1\QuitToDesktop.Tests.dll'
foreach ($taskPath in @($taskRuntime, $taskTests)) { if (-not (Test-Path $taskPath)) { throw "Missing Mono test dependency: $taskPath" } }
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$taskNativeSource = @"
using System;
using System.Runtime.InteropServices;
public static class QuitToDesktopTestsNativeMono {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDllDirectory(string path);
    [DllImport(@"$taskRuntime", CallingConvention=CallingConvention.Cdecl)]
    public static extern void mono_set_assemblies_path(string path);
    [DllImport(@"$taskRuntime", CallingConvention=CallingConvention.Cdecl)]
    public static extern void mono_set_dirs(string assemblyPath, string configPath);
    [DllImport(@"$taskRuntime", CallingConvention=CallingConvention.Cdecl)]
    public static extern int mono_main(int argc, IntPtr argv);
}
"@
Add-Type -TypeDefinition $taskNativeSource
if (-not [QuitToDesktopTestsNativeMono]::SetDllDirectory((Join-Path $GameDirectory 'Sea Power_Data\Plugins\x86_64'))) {
    throw "Could not configure the test process native-library search path: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
}
$taskSearch = @($taskFixture, $taskManaged, (Join-Path $GameDirectory 'BepInEx\core'), (Split-Path $taskTests -Parent), $AnchorChainDirectory) -join ';'
[Environment]::SetEnvironmentVariable('MONO_PATH', $taskSearch)
[QuitToDesktopTestsNativeMono]::mono_set_dirs($taskManaged, (Join-Path $GameDirectory 'MonoBleedingEdge\etc'))
[QuitToDesktopTestsNativeMono]::mono_set_assemblies_path($taskSearch)
$taskArguments = @('QuitToDesktopTests', '--runtime=v4.0.30319', $taskTests, 'game', $GameDirectory, $AnchorChainDirectory)
$taskStrings = @($taskArguments | ForEach-Object { [Runtime.InteropServices.Marshal]::StringToHGlobalAnsi($_) })
$taskVector = [Runtime.InteropServices.Marshal]::AllocHGlobal([IntPtr]::Size * ($taskStrings.Count + 1))
try {
    for ($taskIndex=0; $taskIndex -lt $taskStrings.Count; $taskIndex++) {
        [Runtime.InteropServices.Marshal]::WriteIntPtr($taskVector, [IntPtr]::Size * $taskIndex, $taskStrings[$taskIndex])
    }
    [Runtime.InteropServices.Marshal]::WriteIntPtr($taskVector, [IntPtr]::Size * $taskStrings.Count, [IntPtr]::Zero)
    Write-Output 'Testing with the game Mono runtime in a separate process; the running game is not attached.'
    $taskResult = [QuitToDesktopTestsNativeMono]::mono_main($taskStrings.Count, $taskVector)
    if ($taskResult -ne 0) { throw "Mono tests failed with exit code $taskResult." }
} finally {
    foreach ($taskPointer in $taskStrings) { [Runtime.InteropServices.Marshal]::FreeHGlobal($taskPointer) }
    [Runtime.InteropServices.Marshal]::FreeHGlobal($taskVector)
}
