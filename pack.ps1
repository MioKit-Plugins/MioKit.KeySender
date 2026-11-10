[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PackageVersion,
    [string]$PackageId = "MioKit.KeySender",
    [string]$Configuration = "Release",
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "artifacts"
}

$projectPath = Join-Path $PSScriptRoot "plugin\MioKit.KeySender.csproj"
$manifestPath = Join-Path $PSScriptRoot "plugin\plugin.json"
$uiProjectPath = Join-Path $PSScriptRoot "plugin\vue-ui"
$uiEntryPath = Join-Path $PSScriptRoot "plugin\ui\dist\index.html"
$forbiddenFields = @("pluginVersion", "releaseState", "releaseDate")
$forbiddenDlls = @("MioKit.Sdk.dll", "MioKit.SourceGenerate.dll", "MioKit.Webview2.dll", "MioKit.Runtime.dll", "MioKit.Framework.dll")
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?(?:\+[0-9A-Za-z][0-9A-Za-z.-]*)?$') { throw "PackageVersion 必须是有效的 NuGet SemVer，例如 1.2.0 或 1.2.0-beta.1。" }
if (-not (Test-Path -LiteralPath $projectPath) -or -not (Test-Path -LiteralPath $manifestPath)) { throw "未找到插件项目或 plugin.json。" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.metadataVersion -ne "1.0" -or [string]::IsNullOrWhiteSpace($manifest.id) -or [string]::IsNullOrWhiteSpace($manifest.name) -or [string]::IsNullOrWhiteSpace($manifest.assembly) -or [string]::IsNullOrWhiteSpace($manifest.minSdkVersion)) { throw "plugin.json 必须使用 metadataVersion 1.0，并包含 id、name、assembly 和 minSdkVersion。" }
foreach ($field in $forbiddenFields) {
    if ($manifest.PSObject.Properties.Name -contains $field) { throw "plugin.json 禁止字段 '$field'；版本及正式/预发布状态只由 PackageVersion 决定。" }
}

if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) { throw "未找到 pnpm，无法构建设置页。" }
Push-Location -LiteralPath $uiProjectPath
try {
    & pnpm install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) { throw "设置页 pnpm install 失败。" }
    & pnpm build
    if ($LASTEXITCODE -ne 0) { throw "设置页 pnpm build 失败。" }
}
finally {
    Pop-Location
}
if (-not (Test-Path -LiteralPath $uiEntryPath)) { throw "前端构建后缺少设置页入口: $uiEntryPath" }

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
& dotnet build $projectPath -c $Configuration -p:PackageId=$PackageId -p:PackageVersion=$PackageVersion --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败。" }
& dotnet pack $projectPath -c $Configuration --no-build -o $OutputPath -p:PackageId=$PackageId -p:PackageVersion=$PackageVersion --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet pack 失败。" }
$packagePath = Join-Path $OutputPath "$PackageId.$PackageVersion.nupkg"
if (-not (Test-Path -LiteralPath $packagePath)) { throw "未找到打包结果: $packagePath" }
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$inspectPath = [IO.Path]::GetFullPath((Join-Path $tempRoot ("miokit-pack-" + [guid]::NewGuid().ToString("N"))))
if (-not $inspectPath.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "包检查目录必须位于临时目录内。"
}
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $inspectPath)
    if (-not (Test-Path -LiteralPath (Join-Path $inspectPath "plugin.json"))) { throw "nupkg 根目录缺少 plugin.json。" }
    if (-not (Test-Path -LiteralPath (Join-Path $inspectPath $manifest.assembly))) { throw "nupkg 根目录缺少入口 DLL: $($manifest.assembly)" }
    if (-not (Test-Path -LiteralPath (Join-Path $inspectPath "ui\dist\index.html"))) { throw "nupkg 缺少设置页入口 ui/dist/index.html。" }
    if ($manifest.icon -and -not (Test-Path -LiteralPath (Join-Path $inspectPath $manifest.icon))) { throw "nupkg 缺少 plugin.json 声明的资源: $($manifest.icon)" }
    if (-not (Test-Path -LiteralPath (Join-Path $inspectPath "ui\dist\index.html"))) { throw "nupkg 缺少 WebView2 入口: ui/dist/index.html。" }
    foreach ($dll in $forbiddenDlls) {
        if (Get-ChildItem -LiteralPath $inspectPath -Recurse -File -Filter $dll) { throw "nupkg 禁止携带宿主 DLL: $dll" }
    }
    $nuspec = Get-ChildItem -LiteralPath $inspectPath -Filter *.nuspec -File | Select-Object -First 1
    if (-not $nuspec) { throw "nupkg 缺少 nuspec。" }
    [xml]$nuspecXml = Get-Content -LiteralPath $nuspec.FullName -Raw -Encoding UTF8
    $packageIcon = ([string]$nuspecXml.package.metadata.icon).Replace('\', '/')
    $manifestIcon = ([string]$manifest.icon).Replace('\', '/')
    if ($packageIcon -cne $manifestIcon) { throw "nuspec icon '$packageIcon' 与 plugin.json icon '$manifestIcon' 不一致。" }
    $envelope = ([string]$nuspecXml.package.metadata.description) | ConvertFrom-Json
    if ($envelope.schema -ne "miokit.plugin-package" -or $envelope.schemaVersion -ne 1 -or $envelope.plugin.id -ne $manifest.id -or $envelope.plugin.assembly -ne $manifest.assembly) { throw "nuspec description JSON 信封无效或与 plugin.json 不一致。" }
}
finally {
    if (Test-Path -LiteralPath $inspectPath) { Remove-Item -LiteralPath $inspectPath -Recurse -Force }
}
Write-Host "打包完成: $packagePath"
