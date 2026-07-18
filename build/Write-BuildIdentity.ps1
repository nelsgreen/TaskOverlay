param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Invoke-GitValue([string[]]$Arguments) {
    try {
        $value = & git @Arguments 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($value)) {
            return ($value | Select-Object -First 1).Trim()
        }
    }
    catch {
    }

    return $null
}

$commitSha = $env:GITHUB_SHA
if ([string]::IsNullOrWhiteSpace($commitSha)) {
    $commitSha = Invoke-GitValue @('rev-parse', 'HEAD')
}
if ([string]::IsNullOrWhiteSpace($commitSha)) {
    $commitSha = 'unknown'
}

$branchOrHead = $env:GITHUB_HEAD_REF
if ([string]::IsNullOrWhiteSpace($branchOrHead)) {
    $branchOrHead = $env:GITHUB_REF_NAME
}
if ([string]::IsNullOrWhiteSpace($branchOrHead)) {
    $branchOrHead = Invoke-GitValue @('branch', '--show-current')
}
if ([string]::IsNullOrWhiteSpace($branchOrHead)) {
    $branchOrHead = 'unknown'
}

$runNumber = if ([string]::IsNullOrWhiteSpace($env:GITHUB_RUN_NUMBER)) {
    'local'
} else {
    $env:GITHUB_RUN_NUMBER.Trim()
}

$destination = [System.IO.Path]::GetFullPath($OutputPath)
$directory = [System.IO.Path]::GetDirectoryName($destination)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
@(
    "CommitSHA=$commitSha"
    "BranchOrHead=$branchOrHead"
    "CIRunNumber=$runNumber"
    "BuildUtc=$([DateTimeOffset]::UtcNow.ToString('O'))"
) | Set-Content -LiteralPath $destination -Encoding utf8
