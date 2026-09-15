$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

if ((git branch --show-current) -ne "main") {
    throw "Expected branch 'main'. Switch to main before publishing."
}

git diff --check
if ($LASTEXITCODE -ne 0) { throw "Git found whitespace errors. Nothing was published." }

git add --all
if ($LASTEXITCODE -ne 0) { throw "Unable to stage the project changes." }

$stagedChanges = git diff --cached --name-only
if (-not $stagedChanges) {
    Write-Host "No uncommitted changes were found." -ForegroundColor Yellow
    exit 0
}

git commit -m "Complete MMF analytics and database pipeline"
if ($LASTEXITCODE -ne 0) { throw "Unable to create the Git commit." }

git pull --rebase origin main
if ($LASTEXITCODE -ne 0) { throw "Unable to rebase onto origin/main. Resolve the reported conflict before pushing." }

git push origin main
if ($LASTEXITCODE -ne 0) { throw "GitHub push failed. Review the authentication message above." }

Write-Host "Published successfully to origin/main." -ForegroundColor Green
git log -1 --oneline
