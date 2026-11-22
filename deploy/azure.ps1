<#
.SYNOPSIS
  Deploys GameVault to Azure Container Apps, next to an existing TeamBoard deployment: it reuses that resource group,
  Container Apps environment and SQL server, and adds one free Azure SQL database and one container app.

.DESCRIPTION
  Written from the Azure CLI documentation and the TeamBoard script; it has not been run yet, so expect a rough edge.
  It stops at the first failing step. Everything it creates is separate from TeamBoard's resources, so removing GameVault
  later leaves TeamBoard alone (see the end of the output).

  Needs: the Azure CLI signed in with `az login` and the containerapp extension (`az extension add -n containerapp`),
  and the image pushed by CI to ghcr.io/<owner>/gamevault and made public once (see .github/workflows/ci.yml).

  Cost guard: the database uses the Azure SQL free offer and auto-pauses when the monthly allowance is used. If Azure
  will not give a second free database, the script stops instead of creating a paid one.

  Uploads: the app has no volume here, so images uploaded in the studio pages are lost when the app restarts or scales
  to zero. That is fine for a portfolio demo; mount Azure Files at /data/uploads if it ever matters.

  RAWG stays off in this deployment (the portfolio decision): the site shows the 31 sample games.

.EXAMPLE
  .\deploy\azure.ps1 -Image ghcr.io/taha-hassan-1/gamevault:latest
#>
param(
  [Parameter(Mandatory)][string]$Image,
  [string]$Group = 'teamboard-taha-rg',          # TeamBoard's resource group, reused
  [string]$EnvName = 'teamboard-taha-env',       # TeamBoard's Container Apps environment, reused
  [string]$SqlServer = 'teamboard-taha-sql',     # TeamBoard's SQL logical server, reused (only the database is new)
  [string]$SqlAdmin = 'teamboardadmin',
  [string]$App = 'gamevault-taha',
  [string]$Database = 'gamevault',
  [switch]$AlwaysOn                               # keep one replica running: no cold start, uses more of the free grant
)

$ErrorActionPreference = 'Stop'
function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# PowerShell does not stop when a native command fails, so every az call goes through here and throws on a non-zero exit.
# (Only the first words are shown in the error: the full line can contain a password.)
function Invoke-Az {
  & az @args
  if ($LASTEXITCODE -ne 0) { throw "The Azure CLI failed (exit $LASTEXITCODE): az $(($args | Select-Object -First 3) -join ' ') ..." }
}

function Read-PlainSecret($prompt) {
  $secure = Read-Host $prompt -AsSecureString
  [System.Net.NetworkCredential]::new('', $secure).Password
}

# Secrets are asked for here and never written to a file or echoed.
$sqlPassword = Read-PlainSecret "The SQL admin password for $SqlServer (the one you chose for TeamBoard)"
$adminEmail = Read-Host 'Email for the first GameVault Admin account'
$adminPassword = Read-PlainSecret 'Password for that Admin account (10+ chars, upper, lower, digit)'
if ($adminPassword.Length -lt 10) { throw 'The admin password must be at least 10 characters.' }
# The Azure CLI on Windows is a .cmd wrapper, which misreads these characters in an argument.
foreach ($secret in $sqlPassword, $adminPassword) {
  if ($secret -match '[&|<>^%"''`]') { throw 'Passwords must not contain these characters: & | < > ^ % " '' `' }
}

Step 'Checking what exists'
$previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
$location = (& az group show --name $Group --query location --output tsv 2>$null | Out-String).Trim()
$serverLocation = (& az sql server show --name $SqlServer --resource-group $Group --query location --output tsv 2>$null | Out-String).Trim()
$envExists = (& az containerapp env list --resource-group $Group --query "[?name=='$EnvName'].name" --output tsv 2>$null | Out-String).Trim()
$ErrorActionPreference = $previous
if (-not $serverLocation) { throw "The SQL server '$SqlServer' was not found in '$Group'. Check the names, or run az login again." }
if (-not $envExists) { throw "The Container Apps environment '$EnvName' was not found in '$Group'." }
Write-Host "Reusing $Group, $SqlServer ($serverLocation) and $EnvName."

Step 'The free database'
$previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
$dbExists = (& az sql db list --server $SqlServer --resource-group $Group --query "[?name=='$Database'].name" --output tsv 2>$null | Out-String).Trim()
$ErrorActionPreference = $previous
if ($dbExists) {
  Write-Host "The database '$Database' already exists; reusing it." -ForegroundColor Yellow
} else {
  # --use-free-limit asks for the free offer; if Azure will not give a second one this fails, and nothing paid is created.
  Invoke-Az sql db create --server $SqlServer --resource-group $Group --name $Database `
    --edition GeneralPurpose --family Gen5 --capacity 2 --compute-model Serverless `
    --use-free-limit --free-limit-exhaustion-behavior AutoPause --output none
}

# A long timeout and retries: the first connection after an auto-pause has to wake the database (about a minute).
$connection = "Server=tcp:$SqlServer.database.windows.net,1433;Database=$Database;User ID=$SqlAdmin;Password=$sqlPassword;" +
              "Encrypt=True;TrustServerCertificate=False;Connect Timeout=90;ConnectRetryCount=5;ConnectRetryInterval=10;MultipleActiveResultSets=True"

Step 'The app'
# Migrations and the first admin are applied by the app itself at start (one replica, so nothing races).
# Remove the Admin__Password secret later with: az containerapp secret remove ... (it is ignored once the admin exists).
$minReplicas = if ($AlwaysOn) { 1 } else { 0 }
$previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
$appExists = (& az containerapp list --resource-group $Group --query "[?name=='$App'].name" --output tsv 2>$null | Out-String).Trim()
$ErrorActionPreference = $previous
if ($appExists) { Invoke-Az containerapp delete --name $App --resource-group $Group --yes --output none }

Invoke-Az containerapp create --name $App --resource-group $Group --environment $EnvName --image $Image `
  --ingress external --target-port 8080 --min-replicas $minReplicas --max-replicas 1 `
  --cpu 0.5 --memory 1.0Gi `
  --secrets "conn=$connection" "adminpw=$adminPassword" `
  --env-vars 'ConnectionStrings__GameVault=secretref:conn' 'Database__MigrateOnStart=true' `
             "Admin__Email=$adminEmail" 'Admin__Password=secretref:adminpw' 'Rawg__Enabled=false' --output none

$fqdn = (Invoke-Az containerapp show --name $App --resource-group $Group --query properties.configuration.ingress.fqdn --output tsv | Out-String).Trim()
Write-Host "`nDeployed: https://$fqdn" -ForegroundColor Green
Write-Host "Check: https://$fqdn/health   (the first request after idle can take a minute while the database wakes)"
Write-Host "Sign in at https://$fqdn/Account/Login with the admin email you gave."
Write-Host "Remove only GameVault later with: az containerapp delete --name $App --resource-group $Group --yes; az sql db delete --server $SqlServer --resource-group $Group --name $Database --yes"
