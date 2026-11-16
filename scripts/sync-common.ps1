function Get-SyncFileHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function Find-SyncAws([string]$RepoRoot) {
    $portable = Join-Path (Split-Path $RepoRoot -Parent) '.tools/aws-cli-portable/Amazon/AWSCLIV2/aws.exe'
    if (Test-Path -LiteralPath $portable) { return $portable }
    return (Get-Command aws -ErrorAction Stop).Source
}
function Invoke-SyncAws {
    param([string]$Aws, [string[]]$Arguments, [string]$Region, [switch]$Text)
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & $Aws @Arguments --region $Region --no-cli-pager --cli-connect-timeout 10 --cli-read-timeout 60 2>&1
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($code -ne 0) {
        # Native errors may contain signed request information; keep diagnostics concise.
        $safe = if (($output -join "`n") -match 'NoCredentials|ExpiredToken|Token.*expired|Unable to locate credentials|login.*expired') { 'AWS login is missing or expired. Run aws login and restart sync.' } else { 'AWS command failed: ' + ($Arguments | Select-Object -First 2) -join ' ' }
        throw $safe
    }
    $value = ($output | ForEach-Object { "$_" }) -join "`n"
    if ($Text) { return $value }
    if ($value.Trim()) { return ($value | ConvertFrom-Json) }
}
function Get-SyncFingerprint([string]$RepoRoot) {
    $files = @((Get-Item -LiteralPath (Join-Path $RepoRoot 'pom.xml')))
    $files += @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'src/main') -Recurse -File)
    $lines = @($files | Sort-Object FullName | Where-Object { $_.Name -notmatch '(~$|\.tmp$|\.bak$|^\.#)' } | ForEach-Object {
        $_.FullName.Substring($RepoRoot.Length).Replace('\', '/') + ':' + (Get-SyncFileHash $_.FullName)
    })
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))) }
    finally { $sha.Dispose() }
}
