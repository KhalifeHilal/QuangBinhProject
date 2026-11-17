param([string]$Server = 'ws://localhost:1000', [string]$PreparedSet = 'Workspace', [string]$ModelName = 'Flooding VR.gaml')
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$testProject = Join-Path $project 'Temp/MainSceneGamaValidation'
$models = Join-Path $testProject 'models/version 2'
New-Item -ItemType Directory -Force -Path $models | Out-Null
$original = if ($PreparedSet -eq 'Workspace') { 'C:/Users/GIGABYTE/Gama_Workspace/Gama' } else { 'C:/Users/GIGABYTE/UnityProjects/simple.webplatform/learning-packages/Quanh-Binh' }
Copy-Item -LiteralPath (Join-Path $original 'includes') -Destination $testProject -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $original 'models/version 2') -Filter '*.gaml' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $models -Force
}
foreach ($name in 'Flooding Model.gaml','Flooding VR.gaml') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../$PreparedSet/$name") -Destination $models -Force
}
# Use the same WebSocket implementation as the Unity application. The .NET
# Framework client rejects GAMA's otherwise usable Upgrade,Keep-Alive header.
$dll = Join-Path $project 'Assets/Plugins/websocket-sharp.dll'
Add-Type -Path $dll
$probe = @'
using System;
using System.Collections.Concurrent;
using WebSocketSharp;
public static class GamaDescribeProbe {
    public static string Describe(string uri, string request) {
        var messages = new BlockingCollection<string>();
        using (var socket = new WebSocket(uri)) {
            socket.OnMessage += (sender, e) => { if(e.IsText) messages.Add(e.Data); };
            socket.WaitTime = TimeSpan.FromSeconds(30);
            socket.Connect();
            if (!socket.IsAlive) throw new Exception("GAMA socket is not connected");
            string hello;
            if (!messages.TryTake(out hello,30000)) throw new Exception("No GAMA greeting");
            socket.Send(request);
            string response;
            if (!messages.TryTake(out response,30000)) throw new Exception("No GAMA describe response");
            return response;
        }
    }
}
'@
Add-Type -TypeDefinition $probe -ReferencedAssemblies $dll
$request = @{type='describe'; model=(Join-Path $models $ModelName); experiments=$true; speciesNames=$false; speciesVariables=$false; speciesActions=$false} | ConvertTo-Json -Compress
$response = [GamaDescribeProbe]::Describe($Server,$request) | ConvertFrom-Json
$response | ConvertTo-Json -Depth 12
if ($response.type -ne 'CommandExecutedSuccessfully') { throw 'GAMA compilation/description failed' }
