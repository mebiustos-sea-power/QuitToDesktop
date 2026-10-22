param([Parameter(Mandatory=$true)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskFixture = Join-Path $taskRoot 'artifacts\game-fixture'
New-Item -ItemType Directory -Force $taskFixture | Out-Null
$taskOriginal = Join-Path $GameDirectory 'Sea Power_Data\Managed\Seapower-Scripts.dll'
$taskCopy = Join-Path $taskFixture 'Seapower-Scripts.dll'
if ([IO.Path]::GetFullPath($taskOriginal) -eq [IO.Path]::GetFullPath($taskCopy)) { throw 'The test fixture must not overwrite the game assembly.' }
[void][Reflection.Assembly]::LoadFrom((Join-Path $GameDirectory 'BepInEx\core\Mono.Cecil.dll'))
$taskResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$taskResolver.AddSearchDirectory((Join-Path $GameDirectory 'Sea Power_Data\Managed'))
$taskReader = [Mono.Cecil.ReaderParameters]::new()
$taskReader.AssemblyResolver = $taskResolver
$taskAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($taskOriginal, $taskReader)
try {
    $taskMethods = @(
        @('SeapowerUI.ViewModels.PauseMenuViewModel', 'set_PauseMenuButtons'),
        @('SeapowerUI.MenuSystemView', 'MenuSystem_PreviewKeyDown'),
        @('SeaPower.CancelAction', 'cancel'),
        @('SeaPower.CampaignMain', 'PauseMenu_performed'),
        @('SeaPower.LinearCampaign', 'PauseMenu_performed'),
        @('SeapowerUI.ViewModels.MenuSystemViewModel', 'set_CurrentWindow')
    )
    $taskBefore = @{}
    foreach ($taskPair in $taskMethods) {
        $taskMethod = @($taskAssembly.MainModule.GetType($taskPair[0]).Methods | Where-Object Name -eq $taskPair[1])
        if ($taskMethod.Count -ne 1) { throw "Ambiguous test target: $taskPair" }
        $taskBefore[$taskMethod[0].FullName] = ($taskMethod[0].Body.Instructions | ForEach-Object ToString) -join "`n"
    }
    # The standalone Mono process has no Unity scene or game-data services.
    # Only these environment boundaries change in the ignored test copy.
    $taskInitializer = @($taskAssembly.MainModule.GetType('SeaPower.Globals').Methods | Where-Object Name -eq '.cctor')[0]
    $taskInitializer.Body.Instructions.Clear()
    $taskInitializer.Body.ExceptionHandlers.Clear()
    $taskInitializer.Body.Variables.Clear()
    $taskInitializer.Body.Instructions.Add([Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $taskConstructor = @($taskAssembly.MainModule.GetType('SeapowerUI.PauseMenuView').Methods | Where-Object Name -eq '.ctor')[0]
    $taskGuards = @($taskConstructor.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Boolean UnityEngine.Application::get_isPlaying()' })
    if ($taskGuards.Count -ne 1) { throw 'Unsupported pause-view scene guard.' }
    $taskGuards[0].OpCode = [Mono.Cecil.Cil.OpCodes]::Ldc_I4_0
    $taskGuards[0].Operand = $null
    foreach ($taskPair in $taskMethods) {
        $taskMethod = @($taskAssembly.MainModule.GetType($taskPair[0]).Methods | Where-Object Name -eq $taskPair[1])[0]
        if ($taskBefore[$taskMethod.FullName] -cne (($taskMethod.Body.Instructions | ForEach-Object ToString) -join "`n")) { throw 'A product hook target was changed in the test fixture.' }
    }
    $taskAssembly.Write($taskCopy)
    [ordered]@{
        original = $taskOriginal
        originalSha256 = (Get-FileHash -Algorithm SHA256 $taskOriginal).Hash.ToLowerInvariant()
        fixture = $taskCopy
        fixtureSha256 = (Get-FileHash -Algorithm SHA256 $taskCopy).Hash.ToLowerInvariant()
        substitutions = @('SeaPower.Globals::.cctor: skip global game-data loading', 'PauseMenuView::.ctor: Application.isPlaying scene guard returns false')
        unchangedHookMethods = @($taskBefore.Keys | Sort-Object)
    } | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $taskFixture 'fixture.json')
} finally { $taskAssembly.Dispose(); $taskResolver.Dispose() }
Write-Output "Prepared isolated game fixture: $taskFixture"
