#!/usr/bin/env pwsh

# GetAzureSubscription.ps1
# This script gets the AppConfig - Dev subscription ID and saves it to a JSON file

$outputPath = Join-Path $PSScriptRoot "appsettings.Secrets.json"

Write-Host "Checking for active Azure CLI login"

az account show | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Host "Must be logged in with the Azure CLI to proceed"

    az login

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Azure login failed"
        return
    }
}

az account set --name "AppConfig - Dev"

# Get current subscription from az CLI
$subscriptionId = az account show --query id -o tsv 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "Azure CLI command failed with exit code $LASTEXITCODE. Output: $subscriptionId"
    return
}

# Check if the output is empty
if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
    Write-Host "No active Azure subscription found. Please run 'az login' first."

    exit 1
}

# If successful, save the subscription ID to a JSON file
$result = @{
    SubscriptionId = $subscriptionId.Trim()
}
    
$result | ConvertTo-Json | Out-File $outputPath -Encoding utf8
Write-Host "Subscription information saved to: $outputPath"
exit 0
