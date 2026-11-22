$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Orbiq.csproj'
$publishDir = Join-Path $PSScriptRoot 'publish\win-x64'
$artifactsDir = Join-Path $PSScriptRoot 'artifacts'
$zipPath = Join-Path $artifactsDir 'Orbiq-v3.3.2-win-x64.zip'

if (-not (Test-Path $project)) {
    throw "Не найден файл проекта: $project"
}

Write-Host 'Публикация Orbiq v3.3.2 для Windows x64...' -ForegroundColor Cyan
dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    --output $publishDir

$exePath = Join-Path $publishDir 'Orbiq.exe'
if (-not (Test-Path $exePath)) {
    throw "Публикация завершилась, но Orbiq.exe не найден в $publishDir"
}

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $exePath -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ''
Write-Host 'Готово!' -ForegroundColor Green
Write-Host "EXE: $exePath"
Write-Host "ZIP для GitHub Releases: $zipPath"
Write-Host ''
Write-Host 'Проверь Orbiq.exe двойным щелчком до публикации.' -ForegroundColor Yellow
