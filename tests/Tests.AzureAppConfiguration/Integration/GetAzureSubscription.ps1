#!/usr/bin/env pwsh

# GetAzureSubscription.ps1
# This script gets the current Azure subscription ID and saves it to a JSON file

$ErrorActionPreference = "Stop"
$outputPath = Join-Path $PSScriptRoot "appsettings.Secrets.json"

try {
    # Get current subscription from az CLI
    $subscriptionId = az account show --query id -o tsv 2>&1

    # Check if the command was successful
    if ($LASTEXITCODE -ne 0) {
        $errorInfo = @{
            Success = $false
            ErrorMessage = "Azure CLI command failed with exit code $LASTEXITCODE. Output: $subscriptionId"
        }
        $errorInfo | ConvertTo-Json | Out-File $outputPath -Encoding utf8
        exit 1
    }

    # Check if the output is empty
    if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
        $errorInfo = @{
            Success = $false
            ErrorMessage = "No active Azure subscription found. Please run 'az login' first."
        }
        $errorInfo | ConvertTo-Json | Out-File $outputPath -Encoding utf8
        exit 1
    }

    # If successful, save the subscription ID to a JSON file
    $result = @{
        Success = $true
        SubscriptionId = $subscriptionId.Trim()
    }
    
    $result | ConvertTo-Json | Out-File $outputPath -Encoding utf8
    Write-Output "Subscription information saved to: $outputPath"
    exit 0
}
catch {
    $errorInfo = @{
        Success = $false
        ErrorMessage = "Error getting Azure subscription: $_"
    }
    $errorInfo | ConvertTo-Json | Out-File $outputPath -Encoding utf8
    Write-Error $_.Exception.Message
    exit 1
}