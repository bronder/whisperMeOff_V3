# Build and Release Script for whisperMeOff
# This script builds the project, creates a zip, and publishes a GitHub release

param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

# Get version from csproj if not provided
if (-not $Version) {
    $Version = Select-String -Path "whisperMeOff.csproj" -Pattern "<Version>(.*)</Version>" | ForEach-Object { $_.Matches.Groups[1].Value }
}

if (-not $Version) {
    Write-Error "Could not determine version. Please provide -Version parameter."
    exit 1
}

Write-Host "Building version $Version..." -ForegroundColor Cyan

# Build and publish
Write-Host "Building project..." -ForegroundColor Yellow
dotnet publish -c $Configuration -o "./publish"

# Create zip file
$ZipFileName = "publish-$Version.zip"
Write-Host "Creating $ZipFileName..." -ForegroundColor Yellow
if (Test-Path $ZipFileName) {
    Remove-Item $ZipFileName -Force
}
Compress-Archive -Path "./publish/*" -DestinationPath $ZipFileName

# Check if gh CLI is installed
$ghPath = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghPath) {
    Write-Error "GitHub CLI (gh) is not installed. Please install it from https://cli.github.com/"
    exit 1
}

# Check if user is authenticated
$ghAuth = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Not authenticated with GitHub. Run 'gh auth login' first."
    exit 1
}

# Get repository info from git remote
$remoteUrl = git remote get-url origin 2>$null
Write-Host "Remote URL: $remoteUrl" -ForegroundColor Gray
if ($remoteUrl -match "github\.com/([^/]+)/([^/]+)\.git") {
    $repoOwner = $matches[1]
    $repoName = $matches[2]
    $repoInfo = "$repoOwner/$repoName"
} elseif ($remoteUrl -match "github\.com/([^/]+)/([^/]+)$") {
    $repoOwner = $matches[1]
    $repoName = $matches[2]
    $repoInfo = "$repoOwner/$repoName"
} else {
    Write-Host "Could not parse remote URL, trying gh repo view..." -ForegroundColor Yellow
    $repoInfo = gh repo view --json owner,repositoryOwner -q ".owner.login + "/" + .repositoryOwner.login" 2>$null
    if (-not $repoInfo) {
        Write-Error "Could not determine repository. Please run from git repo with origin remote."
        exit 1
    }
}
Write-Host "Repository: $repoInfo" -ForegroundColor Gray

# Check if release tag exists
$tagExists = gh release view "v$Version" --repo $repoInfo 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "Release v$Version already exists. Updating..." -ForegroundColor Yellow
    gh release upload "v$Version" $ZipFileName --repo $repoInfo --force
} else {
    Write-Host "Creating new release v$Version..." -ForegroundColor Yellow
    gh release create "v$Version" --title "Version $Version" --notes-file RELEASE.md $ZipFileName --repo $repoInfo
}

Write-Host "Done! Release v$Version has been published." -ForegroundColor Green
