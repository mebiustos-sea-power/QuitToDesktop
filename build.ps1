param(
    [string]$GameDirectory = '',
    [string]$AnchorChainDirectory = '',
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
Write-Output 'Building Quit to Desktop.'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$taskDotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $taskDotnet)) { throw 'A .NET SDK is required.' }
$taskLocal = Join-Path $PSScriptRoot 'Directory.Build.local.props'
if (Test-Path $taskLocal) {
    [xml]$taskPaths = Get-Content $taskLocal
    if (-not $GameDirectory) { $GameDirectory = [string]$taskPaths.Project.PropertyGroup.SeaPowerDir }
    if (-not $AnchorChainDirectory) { $AnchorChainDirectory = [string]$taskPaths.Project.PropertyGroup.AnchorChainDir }
}
if (-not (Test-Path (Join-Path $GameDirectory 'Sea Power.exe'))) { throw 'Set the game directory in Directory.Build.local.props or pass -GameDirectory.' }
if (-not (Test-Path (Join-Path $AnchorChainDirectory 'AnchorChain.dll'))) { throw 'Set AnchorChainDir or pass -AnchorChainDirectory.' }
$taskHome = Join-Path $PSScriptRoot 'artifacts\dotnet-home'
New-Item -ItemType Directory -Force $taskHome | Out-Null

function Invoke-TaskProcess([string]$TaskExecutable, [string[]]$TaskArguments) {
    $taskStart = New-Object System.Diagnostics.ProcessStartInfo
    $taskStart.FileName = $TaskExecutable
    $taskStart.Arguments = ($TaskArguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $taskStart.UseShellExecute = $false
    $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardOutput = $true
    $taskStart.RedirectStandardError = $true
    $taskStart.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $taskStart.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $taskStart.EnvironmentVariables['DOTNET_CLI_HOME'] = $taskHome
    $taskStart.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $taskStart.EnvironmentVariables['DOTNET_CLI_UI_LANGUAGE'] = 'en'
    $taskStart.EnvironmentVariables['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    $taskStart.EnvironmentVariables['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
    $taskStart.EnvironmentVariables['DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE'] = 'true'
    $taskProcess = [System.Diagnostics.Process]::Start($taskStart)
    $taskOutput = $taskProcess.StandardOutput.ReadToEndAsync()
    $taskErrors = $taskProcess.StandardError.ReadToEndAsync()
    $taskProcess.WaitForExit()
    if ($taskOutput.Result) { Write-Output $taskOutput.Result.TrimEnd() }
    if ($taskErrors.Result) { Write-Output $taskErrors.Result.TrimEnd() }
    if ($taskProcess.ExitCode -ne 0) { throw "$TaskExecutable exited with $($taskProcess.ExitCode)." }
}
$taskProperties = @('-p:UseSharedCompilation=false', "-p:SeaPowerDir=$GameDirectory", "-p:AnchorChainDir=$AnchorChainDirectory")
Invoke-TaskProcess $taskDotnet (@('build', (Join-Path $PSScriptRoot 'src\QuitToDesktop.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'minimal') + $taskProperties)
if ($Test) {
    Invoke-TaskProcess $taskDotnet (@('build', (Join-Path $PSScriptRoot 'tests\QuitToDesktop.Tests.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'minimal') + $taskProperties)
    Invoke-TaskProcess $taskDotnet @((Join-Path $PSScriptRoot 'tests\bin\Release\net10.0\QuitToDesktop.Tests.dll'), 'core', $GameDirectory, $AnchorChainDirectory)
    Invoke-TaskProcess (Join-Path $PSHOME 'powershell.exe') @('-NoProfile', '-NonInteractive', '-Command',
        ("& '" + (Join-Path $PSScriptRoot 'tools\test-mono.ps1').Replace("'", "''") + "' -GameDirectory '" + $GameDirectory.Replace("'", "''") + "' -AnchorChainDirectory '" + $AnchorChainDirectory.Replace("'", "''") + "'"))
}
$taskPackage = Join-Path $PSScriptRoot 'dist\QuitToDesktop'
New-Item -ItemType Directory -Force $taskPackage | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'src\bin\Release\netstandard2.1\QuitToDesktop.dll') $taskPackage -Force
Get-ChildItem (Join-Path $PSScriptRoot 'package') -File | ForEach-Object { Copy-Item $_.FullName $taskPackage -Force }
Copy-Item (Join-Path $PSScriptRoot 'README.md') $taskPackage -Force
Write-Output "Package: $taskPackage"
[xml]$taskProject = Get-Content (Join-Path $PSScriptRoot 'src\QuitToDesktop.csproj')
$taskVersion = [string]$taskProject.Project.PropertyGroup.Version
$taskZip = Join-Path $PSScriptRoot ("dist\QuitToDesktop-v" + $taskVersion + '.zip')
Add-Type -AssemblyName System.IO.Compression
$taskStream = [IO.File]::Open($taskZip, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
$taskArchive = [IO.Compression.ZipArchive]::new($taskStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in (Get-ChildItem $taskPackage -File | Sort-Object Name)) {
        $taskEntry = $taskArchive.CreateEntry(('QuitToDesktop/' + $taskFile.Name), [IO.Compression.CompressionLevel]::Optimal)
        $taskInput = [IO.File]::OpenRead($taskFile.FullName)
        $taskOutput = $taskEntry.Open()
        try { $taskInput.CopyTo($taskOutput) }
        finally { $taskOutput.Dispose(); $taskInput.Dispose() }
    }
} finally { $taskArchive.Dispose(); $taskStream.Dispose() }
Write-Output "Archive: $taskZip"
Write-Output 'Build complete. This script does not install the mod or start the game.'
