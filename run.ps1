#requires -Version 7.0
<#
.SYNOPSIS
    Builds the Burndown image and (re)deploys it to the local Docker engine.

.DESCRIPTION
    Asks for the Firefly III URL (press ENTER to accept the default) and for a Firefly III personal access token, then
    builds the image from the current sources and starts the container `burndown-web` through
    docker-compose/burndown/docker-compose.yml. The app is published on http://127.0.0.1:9042 (loopback only).

    The token is read as a secure string, handed to docker compose through the environment of this process only, and
    never written to disk. It does end up in the container's configuration (visible through `docker inspect` on this
    machine), as with any compose environment variable.

.PARAMETER FireflyUrl
    Base URL of Firefly III as the container reaches it. Prompted for when omitted.

.PARAMETER FireflyToken
    Firefly III personal access token. Prompted for when omitted.

.PARAMETER NoCache
    Rebuild the image without the Docker layer cache.
#>
[CmdletBinding()]
param(
    [string] $FireflyUrl,
    [securestring] $FireflyToken,
    [switch] $NoCache
)

$ErrorActionPreference = "Stop"
$InformationPreference = "Continue"

$DefaultFireflyUrl = "http://host.docker.internal:8042"
$ComposeFile = Join-Path $PSScriptRoot "docker-compose/burndown/docker-compose.yml"
$ContainerName = "burndown-web"
$AppUrl = "http://127.0.0.1:9042"
$HealthTimeoutSeconds = 120

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $Arguments)

    & docker @Arguments
    if (0 -ne $LASTEXITCODE) {
        throw "'docker $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Read-FireflyUrl {
    $answer = Read-Host "Firefly III URL [$DefaultFireflyUrl]"
    if ([string]::IsNullOrWhiteSpace($answer)) {
        return $DefaultFireflyUrl
    }

    return $answer
}

function Assert-FireflyUrl {
    param([string] $Url)

    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref] $uri) -or $uri.Scheme -notin @("http", "https")) {
        throw "'$Url' is not a valid http(s) URL."
    }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker was not found on the PATH."
}

& docker info *> $null
if (0 -ne $LASTEXITCODE) {
    throw "The Docker engine is not running."
}

if ([string]::IsNullOrWhiteSpace($FireflyUrl)) {
    $FireflyUrl = Read-FireflyUrl
}

$FireflyUrl = $FireflyUrl.Trim().TrimEnd("/")
Assert-FireflyUrl $FireflyUrl

if ($null -eq $FireflyToken -or 0 -eq $FireflyToken.Length) {
    $FireflyToken = Read-Host "Firefly III personal access token" -AsSecureString
}

if (0 -eq $FireflyToken.Length) {
    throw "A Firefly III token is required (Firefly III > Profile > OAuth > Personal Access Tokens)."
}

$sourceRevision = (& git -C $PSScriptRoot rev-parse --short HEAD 2>$null)
if (0 -ne $LASTEXITCODE) {
    $sourceRevision = ""
}

$buildArguments = @("compose", "--file", $ComposeFile, "build")
if ($NoCache) {
    $buildArguments += "--no-cache"
}

try {
    $env:FIREFLY_BASE_URL = $FireflyUrl
    $env:FIREFLY_TOKEN = [System.Net.NetworkCredential]::new("", $FireflyToken).Password
    $env:SOURCE_REVISION_ID = "$sourceRevision"

    Write-Information "Building the image..."
    Invoke-Docker @buildArguments

    Write-Information "Starting $ContainerName against $FireflyUrl ..."
    Invoke-Docker compose --file $ComposeFile up --detach --force-recreate --remove-orphans
}
finally {
    Remove-Item Env:FIREFLY_BASE_URL, Env:FIREFLY_TOKEN, Env:SOURCE_REVISION_ID -ErrorAction SilentlyContinue
}

Write-Information "Waiting for the container to become healthy..."
$deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
$status = ""
do {
    Start-Sleep -Seconds 2
    $status = (& docker inspect --format "{{.State.Health.Status}}" $ContainerName).Trim()
} while ($status -eq "starting" -and (Get-Date) -lt $deadline)

if ($status -ne "healthy") {
    & docker logs --tail 50 $ContainerName
    throw "$ContainerName is '$status' instead of 'healthy'."
}

# Readiness needs Firefly: a wrong URL, an invalid token or a missing account or budget shows up here.
try {
    $ready = Invoke-WebRequest -Uri "$AppUrl/health/ready" -UseBasicParsing -SkipHttpErrorCheck
    $readyStatus = [int] $ready.StatusCode
    $readyBody = "$($ready.Content)".Trim()
}
catch {
    $readyStatus = 0
    $readyBody = $_.Exception.Message
}

if (200 -eq $readyStatus) {
    Write-Information "Burndown is running and Firefly III is reachable: $AppUrl"
}
else {
    Write-Warning "Burndown is running at $AppUrl, but /health/ready answered $readyStatus. $readyBody"
    Write-Warning "Check the Firefly III URL and token, then run this script again. Logs: docker logs $ContainerName"
}
