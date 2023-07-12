cd ..

$email="mattmckenzy@outlook.com"
$name="MattMckenzy"
$linuxCurrentPath = "/"+(($pwd -replace "\\","/") -replace ":","").Trim("/")
$version = docker run -it --rm -v ${linuxCurrentPath}/HomeCast:/homecast mattmckenzy/debbuild:latest /bin/bash -c 'cat /homecast/HomeCast.csproj | grep -oPm1 \"(?<=<AssemblyVersion>)[^<]+\"'
$notes = docker run -it --rm -v ${linuxCurrentPath}/HomeCast:/homecast mattmckenzy/debbuild:latest /bin/bash -c 'cat /homecast/HomeCast.csproj | grep -oPm1 \"(?<=<PackageReleaseNotes>)[^<]+\"'
$currentVersion = docker run -it --rm -v ${linuxCurrentPath}/HomeCast:/homecast mattmckenzy/debbuild:latest /bin/bash -c 'cat homecast/debian/changelog | grep -m1 -o "\(.*\)" | tr -d ''()'''

if ($version -ne $currentVersion)
{
	docker run -it --rm -v ${linuxCurrentPath}/HomeCast:/homecast -e DEBEMAIL="${email}" -e DEBFULLNAME="${name}" -e VERSION="${version}" -e NOTES="${notes}" mattmckenzy/debbuild:latest /bin/bash -c 'cd /homecast/debian && dch -v $VERSION $NOTES'
} 

$password = Import-CliXml -Path "${env:LOCALAPPDATA}\Credentials\SecretStore.xml"
Unlock-SecretStore -Password $password
$githubToken = Get-Secret -Name GitHubAptToken -AsPlainText
$aptSecretKey = Get-Secret -Name AptSecretKey -AsPlainText
$aptSecretKeyFingerprint = Get-Secret -Name AptSecretKeyFingerprint -AsPlainText

docker buildx build --build-arg CONFIG="Release" --build-arg VERSION="${version}" --build-arg GITHUB_TOKEN="${githubToken}" --build-arg APT_SECRET_KEY="${aptSecretKey}" --build-arg APT_SECRET_KEY_FINGERPRINT="${aptSecretKeyFingerprint}" --build-arg DEBEMAIL="${email}" --build-arg DEBFULLNAME="${name}" --platform=linux/amd64,linux/arm64 --push -t "mattmckenzy/homecast-package:latest" -f "HomeCast/HomeCast.package.dockerfile" .

if ($LASTEXITCODE -ne 0) 
{ 
	Read-Host -Prompt "HomeCast package publish failed... Press enter to exit" 
}
else
{		
	Read-Host -Prompt "HomeCast package published! Press enter to exit"  
}