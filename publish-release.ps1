[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [switch]$KeepOldOutput,
    [switch]$Force,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$projectPath = Join-Path $root 'Notchi.csproj'
$issPath = Join-Path $root 'installer\Notchi.iss'
$publishRoot = Join-Path $root 'publish'
$publishDir = Join-Path $publishRoot 'win-x64'
$appExePath = Join-Path $publishDir 'Notchi.exe'
$setupPath = Join-Path $publishRoot 'NotchiSetup-x64.exe'
$checksumPath = Join-Path $publishRoot 'SHA256SUMS.txt'

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath gagal dengan exit code $LASTEXITCODE."
    }
}

function Resolve-IsccPath {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        if (-not (Test-Path -LiteralPath $Path)) {
            throw "ISCC.exe tidak ditemukan di: $Path"
        }

        return (Resolve-Path -LiteralPath $Path).Path
    }

    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $programFilesX86 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw 'ISCC.exe tidak ditemukan. Install Inno Setup atau kirim parameter -IsccPath.'
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "File proyek tidak ditemukan: $projectPath"
}

[xml]$project = Get-Content -LiteralPath $projectPath
$version = [string](@($project.Project.PropertyGroup.Version) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1)
$version = $version.Trim()

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versi '$version' di Notchi.csproj harus berformat X.Y.Z."
}

$numericVersion = "$version.0"

if (-not $Force) {
    $running = Get-Process -Name 'Notchi' -ErrorAction SilentlyContinue
    if ($running) {
        throw 'Notchi sedang berjalan. Tutup aplikasi terlebih dahulu, atau gunakan -Force jika proses tersebut bukan dari folder publish.'
    }
}

Write-Host "Notchi $version"
Write-Host "Membersihkan output lama..."
if (-not $KeepOldOutput -and (Test-Path -LiteralPath $publishRoot)) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Publish single-file (win-x64)..."
$publishArguments = @(
    'publish', $projectPath,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $publishDir
)
Invoke-Native 'dotnet' $publishArguments

if (-not (Test-Path -LiteralPath $appExePath)) {
    throw "Publish tidak menghasilkan Notchi.exe: $appExePath"
}

$looseDll = Join-Path $publishDir 'Notchi.dll'
if (Test-Path -LiteralPath $looseDll) {
    throw "Publish single-file masih menghasilkan Notchi.dll terpisah: $looseDll"
}

$exeInfo = Get-Item -LiteralPath $appExePath
Write-Host ("Single-file: {0} ({1:N1} MB)" -f $appExePath, ($exeInfo.Length / 1MB))

$exeHash = (Get-FileHash -LiteralPath $appExePath -Algorithm SHA256).Hash.ToLowerInvariant()

if ($SkipInstaller) {
    Write-Host ""
    Write-Host "Installer dilewati (-SkipInstaller)."
    Write-Host "SHA256 Notchi.exe: $exeHash"
    return
}

$iscc = Resolve-IsccPath -Path $IsccPath
Write-Host "Membangun installer dengan $iscc ..."
$isccArguments = @(
    "/DMyAppVersion=$version",
    "/DMyAppVersionNumeric=$numericVersion",
    "/O$publishRoot",
    $issPath
)
Invoke-Native $iscc $isccArguments

if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Installer tidak terbentuk: $setupPath"
}

$setupInfo = Get-Item -LiteralPath $setupPath
$setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()

"$setupHash  NotchiSetup-x64.exe" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host ""
Write-Host "Selesai:"
Write-Host ("  Portable : {0} ({1:N1} MB)" -f $appExePath, ($exeInfo.Length / 1MB))
Write-Host ("  Installer: {0} ({1:N1} MB)" -f $setupPath, ($setupInfo.Length / 1MB))
Write-Host "  SHA256   : $checksumPath"
Write-Host "  Setup    : $setupHash"
