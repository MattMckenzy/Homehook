
function Invoke-NativeCommand() {
    # A handy way to run a command, and automatically throw an error if the
    # exit code is non-zero.

    if ($args.Count -eq 0) {
        throw "Must supply some arguments."
    }

    $command = $args[0]
    $commandArgs = @()
    if ($args.Count -gt 1) {
        $commandArgs = $args[1..($args.Count - 1)]
    }

    & $command $commandArgs
    $result = $LASTEXITCODE

    if ($result -ne 0) {
        throw "$command $commandArgs exited with code $result."
    }
}

cd ..

try
{
    $email="mattmckenzy@outlook.com"
    $name="MattMckenzy"
    $linuxCurrentPath = "/"+(($pwd -replace "\\","/") -replace ":","").Trim("/")
    $version = Invoke-NativeCommand docker run -it --rm -v ${linuxCurrentPath}/HomeCast:/homecast mattmckenzy/debbuild:latest /bin/bash -c 'cat /homecast/HomeCast.csproj | grep -oPm1 \"(?<=<AssemblyVersion>)[^<]+\"'
    $notes = Invoke-NativeCommand docker run -it --rm -v ${linuxCurrentPath}/HomeCast:/homecast mattmckenzy/debbuild:latest /bin/bash -c 'cat /homecast/HomeCast.csproj | grep -oPm1 \"(?<=<PackageReleaseNotes>)[^<]+\"'
    $currentVersion = Invoke-NativeCommand docker run -it --rm -v ${linuxCurrentPath}/HomeCast:/homecast mattmckenzy/debbuild:latest /bin/bash -c 'cat homecast/debian/changelog | grep -m1 -o "\(.*\)" | tr -d ''()'''

    if ($version -ne $currentVersion)
    {
	    Invoke-NativeCommand docker run -it --rm -v ${linuxCurrentPath}/HomeCast:/homecast -e DEBEMAIL="${email}" -e DEBFULLNAME="${name}" -e VERSION="${version}" -e NOTES="${notes}" mattmckenzy/debbuild:latest /bin/bash -c "cd /homecast/debian && dch -v $VERSION $NOTES && dch -r ''"
    } 

    $password = Import-CliXml -Path "${env:LOCALAPPDATA}\Credentials\SecretStore.xml"
    Unlock-SecretStore -Password $password
    $githubToken = Get-Secret -Name GitHubAptToken -AsPlainText
    $aptSecretKey = Get-Secret -Name AptSecretKey -AsPlainText
    $aptSecretKeyFingerprint = Get-Secret -Name AptSecretKeyFingerprint -AsPlainText

    Invoke-NativeCommand docker buildx build --build-arg CONFIG="Release" --build-arg VERSION="${version}" --build-arg GITHUB_TOKEN="${githubToken}" --build-arg APT_SECRET_KEY="${aptSecretKey}" --build-arg APT_SECRET_KEY_FINGERPRINT="${aptSecretKeyFingerprint}" --build-arg DEBEMAIL="${email}" --build-arg DEBFULLNAME="${name}" --platform=linux/amd64 --push -t "mattmckenzy/homecast-package:amd64" -f "HomeCast/HomeCast.package.dockerfile" .
    Invoke-NativeCommand docker buildx build --build-arg CONFIG="Release" --build-arg VERSION="${version}" --build-arg GITHUB_TOKEN="${githubToken}" --build-arg APT_SECRET_KEY="${aptSecretKey}" --build-arg APT_SECRET_KEY_FINGERPRINT="${aptSecretKeyFingerprint}" --build-arg DEBEMAIL="${email}" --build-arg DEBFULLNAME="${name}" --platform=linux/arm64 --push -t "mattmckenzy/homecast-package:arm64" -f "HomeCast/HomeCast.package.dockerfile" .
    Invoke-NativeCommand docker buildx imagetools create -t mattmckenzy/homecast-package:latest mattmckenzy/homecast-package:amd64 mattmckenzy/homecast-package:arm64

    Read-Host -Prompt "HomeCast package published! Press enter to exit"
}
catch
{
	Read-Host -Prompt "HomeCast package publish failed... Press enter to exit"
}
finally
{
    cd HomeCast
}
