$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = Join-Path $project 'Temp/MainSceneLoggingValidation'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$shim = @'
namespace UnityEngine {
    public static class JsonUtility { public static T FromJson<T>(string json) { throw new System.NotSupportedException("Not used in queue/record checks"); } }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator *(Vector3 a,float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
    }
}
'@
$shimPath = Join-Path $out 'VectorShim.cs'
Set-Content -LiteralPath $shimPath -Value $shim -Encoding UTF8
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
& "$unityData/NetCoreRuntime/dotnet.exe" "$unityData/DotNetSdkRoslyn/csc.dll" /nologo /target:exe /nostdlib+ `
    "/r:$framework/mscorlib.dll" "/r:$framework/System.dll" "/r:$framework/System.Core.dll" `
    "/out:$out/RoundChecks.exe" $shimPath (Join-Path $project 'Assets/Scripts/MainSceneRoundLog.cs') `
    (Join-Path $project 'Assets/Scripts/MainSceneWorldQueue.cs') `
    (Join-Path $project 'Assets/Scripts/Gama Provider/Serialization/WorldJSONInfo.cs') `
    (Join-Path $project 'Assets/Scripts/Gama Provider/Serialization/Attributes.cs') `
    (Join-Path $PSScriptRoot 'MainSceneRoundLogChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& (Join-Path $out 'RoundChecks.exe')
if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
