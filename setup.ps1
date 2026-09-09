$envFile = Join-Path $PSScriptRoot ".env"
$exampleFile = Join-Path $PSScriptRoot ".env.example"

if (-not (Test-Path $envFile)) {
    Copy-Item -LiteralPath $exampleFile -Destination $envFile
    Write-Host ".env created from .env.example"
} else {
    Write-Host ".env already exists"
}

Write-Host "Next step:"
Write-Host "1. Update database and seed-user passwords in .env"
Write-Host "2. Run: docker compose up --build -d"
Write-Host "3. Open: http://localhost:8088/login"
