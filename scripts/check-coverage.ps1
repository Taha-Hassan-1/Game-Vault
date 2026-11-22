# Fails when line coverage of the Application project is below the target (default 70%).
#
#   dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage" --results-directory TestResults
#   pwsh scripts/check-coverage.ps1            # or powershell scripts\check-coverage.ps1
#
# coverage.runsettings limits collection to GameVault.Application, the layer the spec sets the target for.
param(
    [string]$ResultsDirectory = "TestResults",
    [double]$Minimum = 70.0
)

$report = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $report) { Write-Error "No coverage.cobertura.xml under '$ResultsDirectory'. Run the tests with --collect:'XPlat Code Coverage' first."; exit 2 }

[xml]$xml = Get-Content $report.FullName
$rate = [double]::Parse($xml.coverage.'line-rate', [Globalization.CultureInfo]::InvariantCulture) * 100
$covered = [int]$xml.coverage.'lines-covered'
$valid = [int]$xml.coverage.'lines-valid'

Write-Host ("GameVault.Application line coverage: {0:F1}% ({1} of {2} lines), target {3:F0}%" -f $rate, $covered, $valid, $Minimum)

# Per class, lowest first, so the gaps are easy to see.
$xml.SelectNodes("//class") | ForEach-Object {
    [pscustomobject]@{ Class = $_.name; Lines = ([double]::Parse($_.'line-rate', [Globalization.CultureInfo]::InvariantCulture) * 100) }
} | Group-Object Class | ForEach-Object { $_.Group | Select-Object -First 1 } | Sort-Object Lines | Select-Object -First 8 |
    ForEach-Object { Write-Host ("  {0,5:F0}%  {1}" -f $_.Lines, $_.Class) }

if ($rate -lt $Minimum) { Write-Error ("Coverage {0:F1}% is below the {1:F0}% target." -f $rate, $Minimum); exit 1 }
