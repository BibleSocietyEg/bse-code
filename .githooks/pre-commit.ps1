#!/usr/bin/env pwsh
# Pre-commit hook (PowerShell): verify code formatting and run tests.
# Install: git config core.hooksPath .githooks
# Then run: git config --global core.hooksPath .githooks

$ErrorActionPreference = 'Stop'

Write-Host "🔍 Checking code formatting..."
dotnet format --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ Formatting issues found. Run 'dotnet format' to fix them."
    exit 1
}

Write-Host "✅ Formatting OK"
Write-Host "🧪 Running tests..."
dotnet test tests/BSE_Code.Tests.csproj --no-restore -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ Tests failed. Fix them before committing."
    exit 1
}

Write-Host "✅ All checks passed"
