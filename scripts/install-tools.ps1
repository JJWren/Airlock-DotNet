<#
.SYNOPSIS
    Installs Syft v1.55.0 into the repository's bin folder (IR-25).

.DESCRIPTION
    Downloads the release for this machine's system and architecture from Syft's GitHub releases over HTTPS, compares
    its SHA-256 checksum with the one this script carries and with the release's checksums file before anything is
    extracted, and moves the binary into bin/. Does nothing when bin/ already holds v1.55.0, and stops with a message on
    any failure. Runs on Windows PowerShell 5.1 and on PowerShell 7.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '1.55.0'
$releases = 'https://github.com/anchore/syft/releases/download'
$bin = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin'

function Stop-Install([string] $Message) {
    throw "install-tools: $Message"
}

# Windows PowerShell 5.1 has no $IsWindows; it runs only on Windows.
if ($PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows) {
    $os = 'windows'
} elseif ($IsMacOS) {
    $os = 'darwin'
} else {
    $os = 'linux'
}

switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
    'X64' { $arch = 'amd64' }
    'Arm64' { $arch = 'arm64' }
    default {
        [Console]::Error.WriteLine("install-tools: unsupported architecture: $_")
        exit 1
    }
}

$extension = if ($os -eq 'windows') { 'zip' } else { 'tar.gz' }
$binary = if ($os -eq 'windows') { 'syft.exe' } else { 'syft' }
$target = Join-Path $bin $binary

# Nothing to do when bin/ already holds this version.
if (Test-Path -LiteralPath $target) {
    try {
        $current = & $target version | ForEach-Object { if ($_ -match '^Version:\s*(\S+)') { $Matches[1] } }
    } catch {
        $current = $null
    }

    if ($current -eq $version) {
        Write-Host "Syft $version is already in $bin"
        exit 0
    }
}

# Windows PowerShell 5.1 may not offer TLS 1.2 by default; the progress bar slows its downloads badly.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'

$archive = "syft_${version}_${os}_${arch}.$extension"
$checksums = "syft_${version}_checksums.txt"

# The SHA-256 of each archive this script can fetch, from Syft v1.55.0's checksums file. A download must match it as well
# as the release's own checksums file, so a replaced release asset stops the script (DR-04, U6-NFR-01).
$pinned = @{
    'syft_1.55.0_darwin_amd64.tar.gz' = 'e2e5831cbabaec9a36fe65c34755c1113982c842a685edce3ce0b54a0ded8003'
    'syft_1.55.0_darwin_arm64.tar.gz' = '1217c258bd5eb91ef3189cf22a82e02f9cac07e4ce934350f428e93aff674625'
    'syft_1.55.0_linux_amd64.tar.gz' = 'bb65249542db5fedeeb4d3f9b018b75e7264dbfbbb10486f044581a92f90490d'
    'syft_1.55.0_linux_arm64.tar.gz' = '929bd163db2d7cd2255d3bcb8967208079813d7b46b0c952f7b794d8e2c7d304'
    'syft_1.55.0_windows_amd64.zip' = 'ada4c9b93af575f479ce80fce6629897272a2d41f300a14bc49bace020767ca2'
    'syft_1.55.0_windows_arm64.zip' = 'f52f7796e41e4d1ae511c228c99239fd09887077336dcb7a8c2aa7046ed7c816'
}

if (-not $pinned.ContainsKey($archive)) {
    [Console]::Error.WriteLine("install-tools: this script carries no checksum for $archive")
    exit 1
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('airlock-syft-' + [Guid]::NewGuid().ToString('N'))
$failed = $false
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $archivePath = Join-Path $work $archive
    $checksumsPath = Join-Path $work $checksums
    Invoke-WebRequest -UseBasicParsing -Uri "$releases/v$version/$archive" -OutFile $archivePath
    Invoke-WebRequest -UseBasicParsing -Uri "$releases/v$version/$checksums" -OutFile $checksumsPath

    # The checksums are compared before anything is extracted.
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
    if ($actual -ne $pinned[$archive]) {
        Stop-Install "the checksum of $archive does not match the one this script carries"
    }

    $expected = $null
    foreach ($line in Get-Content -LiteralPath $checksumsPath) {
        $parts = $line -split '\s+'
        if ($parts.Count -ge 2 -and $parts[1].TrimStart('*') -eq $archive) {
            $expected = $parts[0].ToLowerInvariant()
        }
    }

    if (-not $expected) {
        Stop-Install "$checksums does not list $archive"
    }

    if ($actual -ne $expected) {
        Stop-Install "the checksum of $archive does not match the release's"
    }

    $extracted = Join-Path $work 'extracted'
    New-Item -ItemType Directory -Path $extracted | Out-Null
    if ($extension -eq 'zip') {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extracted
    } else {
        & tar -xzf $archivePath -C $extracted $binary
        if ($LASTEXITCODE -ne 0) {
            Stop-Install "could not extract $binary from $archive"
        }
    }

    New-Item -ItemType Directory -Force -Path $bin | Out-Null
    Move-Item -Force -LiteralPath (Join-Path $extracted $binary) -Destination $target
    if ($os -ne 'windows') {
        & chmod +x $target
    }

    Write-Host "Installed Syft $version in $bin"
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    $failed = $true
} finally {
    Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
}

if ($failed) {
    exit 1
}
