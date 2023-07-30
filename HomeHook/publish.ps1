cd ..

docker buildx build --build-arg CONFIG="Release" --platform=linux/amd64,linux/arm64 --push -t "mattmckenzy/homehook2:latest" -f "HomeHook/HomeHook.dockerfile" .

if ($LASTEXITCODE -ne 0) 
{ 
	Read-Host -Prompt "HomeHook package publish failed... Press enter to exit" 
}
else
{		
	Read-Host -Prompt "HomeHook package published! Press enter to exit"  
}