$Project = "burndown"
$Product = "Burndown"
$Tag = "latest"

$InformationPreference = "Continue"

$SrcDir = "$PSScriptRoot\src"
$DistDir = "$PSScriptRoot\dev-dist"

if (Test-Path $DistDir) {
    Remove-Item $DistDir -Force -Recurse
}

docker-compose --file docker-compose.yml --project-name $Product.ToLower() down --rmi local --remove-orphans

dotnet publish "$SrcDir/$Product/$Product.csproj" --configuration Release --force --output "$DistDir/Container" --self-contained true --runtime linux-x64 -p:EnvironmentName=Production

docker build --network host -m 1GB -t "$($Product.ToLower()):$($Tag)" -f dockerfile .

if (0 -ne $LastExitCode) {
    throw "Docker build failed with exit code $LastExitCode."
}

if (Test-Path $DistDir) {
    Remove-Item $DistDir -Force -Recurse
}

docker-compose --file docker-compose.yml --project-name $Product.ToLower() up --detach
