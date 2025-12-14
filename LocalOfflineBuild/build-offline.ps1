Param(
    [string]$Configuration = "Release"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path "$scriptDir\..\.." | Select-Object -ExpandProperty Path
$buildRoot = $scriptDir

Write-Host "Building Safeturned projects with configuration '$Configuration'..."

$projects = @(
    @{ Name = "Loader"; Path = Join-Path $repoRoot "Plugins\Safeturned.Loader\Safeturned.Loader.csproj" },
    @{ Name = "Module"; Path = Join-Path $repoRoot "Plugins\Safeturned.Module\Safeturned.Module.csproj" },
    @{ Name = "PluginInstaller"; Path = Join-Path $repoRoot "Plugins\Safeturned.PluginInstaller\Safeturned.PluginInstaller.csproj" }
)

foreach ($project in $projects) {
    Write-Host ">= Building $($project.Name)..."
    dotnet build $project.Path -c $Configuration
}

$modulesRoot = Join-Path $buildRoot "Modules"
$loaderDestination = Join-Path $modulesRoot "Safeturned.Loader"
$moduleDestination = Join-Path $buildRoot "SafeturnedBuild\Safeturned.Module"
$installerDestination = Join-Path $buildRoot "SafeturnedBuild\Safeturned.PluginInstaller"

foreach ($dest in @($modulesRoot, $loaderDestination, $moduleDestination, $installerDestination)) {
    if (-Not (Test-Path $dest)) {
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
    }
}

Write-Host "Copying loader outputs..."
$loaderBin = Join-Path $repoRoot "Plugins\Safeturned.Loader\bin\$Configuration\net48"
$loaderPackDir = Join-Path $loaderBin "Safeturned.Loader"
if (Test-Path $loaderPackDir) {
    Remove-Item $loaderDestination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $loaderDestination -Force | Out-Null
    Copy-Item "$loaderPackDir\*" $loaderDestination -Recurse -Force
} else {
    Write-Warning "Loader package folder not found at $loaderPackDir"
}
Write-Host "Copying module outputs..."
$moduleBin = Join-Path $repoRoot "Plugins\Safeturned.Module\bin\$Configuration\net48"
$modulePackDir = Join-Path $moduleBin "Safeturned.Module"
if (Test-Path $modulePackDir) {
    Remove-Item $moduleDestination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $moduleDestination -Force | Out-Null
    Copy-Item "$modulePackDir\*" $moduleDestination -Recurse -Force
} else {
    Write-Warning "Module package folder not found at $modulePackDir"
}

Write-Host "Copying plugin installer outputs..."
$installerBin = Join-Path $repoRoot "Plugins\Safeturned.PluginInstaller\bin\$Configuration\net48"
Copy-Item "$installerBin\Safeturned.PluginInstaller.dll" $installerDestination -Force
Copy-Item "$installerBin\Safeturned.PluginInstaller.pdb" $installerDestination -Force -ErrorAction SilentlyContinue

Write-Host "Build complete. Offline artifacts are under $buildRoot."
