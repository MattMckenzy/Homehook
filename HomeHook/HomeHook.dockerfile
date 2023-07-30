FROM mcr.microsoft.com/dotnet/aspnet:7.0-bookworm-slim AS base
RUN apt update
RUN apt install -y python3 
RUN apt install -y yt-dlp
WORKDIR /app
EXPOSE 8122

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/nightly/sdk:8.0-preview AS publish
ARG TARGETARCH
ARG CONFIG
COPY . ./
RUN dotnet restore "HomeHook/HomeHook.csproj" -a $TARGETARCH
RUN dotnet publish "HomeHook/HomeHook.csproj" -a $TARGETARCH -c $CONFIG -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HomeHook.dll"]