param(
    [Parameter(Mandatory=$true)][string]$GameManaged,
    [Parameter(Mandatory=$true)][string]$LoaderCore
)
$ErrorActionPreference = 'Stop'
foreach ($assembly in @((Join-Path $GameManaged 'SunHaven.Core.dll'), (Join-Path $LoaderCore 'BepInEx.dll'))) {
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Required local assembly missing: $assembly" }
}
dotnet build "$PSScriptRoot\src\SunHavenCreate.csproj" -c Release "-p:GameManaged=$GameManaged" "-p:LoaderCore=$LoaderCore"
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed' }
$packageRoot = Join-Path $PSScriptRoot 'artifacts\BepInEx\plugins\SunHavenCreate'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\src\bin\Release\net472\SunHavenCreate.dll" -Destination $packageRoot -Force
Copy-Item -LiteralPath "$PSScriptRoot\src\bin\Release\net472\Newtonsoft.Json.dll" -Destination $packageRoot -Force
Copy-Item -LiteralPath "$PSScriptRoot\assets" -Destination $packageRoot -Recurse -Force
Copy-Item -LiteralPath "$PSScriptRoot\LICENSE","$PSScriptRoot\Newtonsoft.Json.LICENSE.md","$PSScriptRoot\ASSETS.md" -Destination $packageRoot -Force
Write-Output "Install package: $packageRoot"
