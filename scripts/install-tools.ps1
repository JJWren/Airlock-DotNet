<#
.SYNOPSIS
    Installs Syft v1.55.0 into the repository's bin folder (IR-25).

.DESCRIPTION
    Downloads the release for this machine's system and architecture from Syft's GitHub releases over HTTPS, compares
    its SHA-256 checksum with the release's checksums file before anything is extracted, and moves the binary into bin/.
    Does nothing when bin/ already holds v1.55.0, and stops with a message on any failure. Runs on Windows PowerShell 5.1
    and on PowerShell 7.
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
$work = Join-Path ([IO.Path]::GetTempPath()) ('airlock-syft-' + [Guid]::NewGuid().ToString('N'))
$failed = $false
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $archivePath = Join-Path $work $archive
    $checksumsPath = Join-Path $work $checksums
    Invoke-WebRequest -UseBasicParsing -Uri "$releases/v$version/$archive" -OutFile $archivePath
    Invoke-WebRequest -UseBasicParsing -Uri "$releases/v$version/$checksums" -OutFile $checksumsPath

    # The checksum is compared before anything is extracted.
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

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
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
