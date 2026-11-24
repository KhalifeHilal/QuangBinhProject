$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = Join-Path $project 'Temp/GamepadStartRegression'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$source = Get-Content -Raw -LiteralPath (Join-Path $project 'Assets/Scripts/FloodScenarioQuizPanel.cs')
$begin = $source.IndexOf('    public static void AddStartControl(')
if ($begin -lt 0) { throw 'Production start helper missing' }
$opening = $source.IndexOf('{', $begin)
$depth = 1
$end = $opening + 1
while ($depth -gt 0 -and $end -lt $source.Length) {
    if ($source[$end] -eq '{') { $depth++ }
    if ($source[$end] -eq '}') { $depth-- }
    $end++
}
$helper = 'using System; using UnityEngine; using TMPro; public static class FloodScenarioContinuePrompt {' +
    $source.Substring($begin, $end - $begin) + '}'
$helperPath = Join-Path $out 'ProductionStartHelper.cs'
Set-Content -LiteralPath $helperPath -Value $helper -Encoding UTF8
foreach ($n in 2..6) {
    $flow = Get-Content -Raw -LiteralPath (Join-Path $project "Assets/Scripts/A1P${n}ScenarioFlow.cs")
    if ($flow -notmatch 'CreateStartPrompt\(StartPreview\)' -or $flow -notmatch 'AddStartControl\(root.transform, onStart, -220\)' -or
        $flow -notmatch 'Phase.WaitingForA && !ParticipantInput.IsGamepadParticipant') { throw "Incorrect scenario $n wiring" }
}
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
& "$unityData/NetCoreRuntime/dotnet.exe" "$unityData/DotNetSdkRoslyn/csc.dll" /nologo /target:exe /nostdlib+ `
    "/r:$framework/mscorlib.dll" "/r:$framework/System.dll" "/r:$framework/System.Core.dll" "/out:$out/Checks.exe" `
    (Join-Path $project 'Assets/Scripts/GamepadUIInput.cs') (Join-Path $project 'Assets/Scripts/ParticipantInput.cs') `
    (Join-Path $PSScriptRoot 'Stubs.cs') (Join-Path $PSScriptRoot 'Checks.cs') $helperPath
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& (Join-Path $out 'Checks.exe')
if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
