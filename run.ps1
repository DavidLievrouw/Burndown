$Project = "burndown"
$Tag = "latest"

$InformationPreference = "Continue"

docker-compose --file docker-compose.yml --project-name $Project down --rmi local --remove-orphans

docker build --no-cache --network host -m 1GB -t "$($Project):$($Tag)" -f dockerfile .
if (0 -ne $LastExitCode) {
    throw "Docker build failed with exit code $LastExitCode."
}

docker-compose --file docker-compose.yml --project-name $Project up #--detach
