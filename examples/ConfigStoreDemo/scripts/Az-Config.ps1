Param (
    [parameter(Mandatory=$true , Position=0)]
    [ValidateSet("Set-Env")]
    [string]
    $Command,
    
    [parameter()]
    [string]
    $Name,
    
    [parameter()]
    [string]
    $Prefix = "Build/"
)

$version = "2.0"

$res = Invoke-RestMethod -Method Get -Uri http://52.191.252.66/kv -Headers @{ "Accept" = "application/json; version=`"[1.0,$version]`"" }

Write-Host ""
Write-Host "Retrieving Environment Variables..."
Write-Host ""

foreach ($kv in $res.items) {

  if (-not($kv.key.StartsWith("Build/"))) {
    continue;
  }

  $key = $kv.key.replace($Prefix, "")

  Write-Host "Setting Environment variable `"$($key)`" to `"$($kv.value)`""

  Set-Item env:\$($key) -Value $($kv.value)
}

Write-Host ""
Write-Host "Finished..."
Write-Host ""
