#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:latest AS base
RUN apt update && apt install -y alsa-utils pulseaudio evtest mpv socat python3
ADD https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp resources
WORKDIR /app
EXPOSE 8121

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/nightly/sdk:8.0-preview AS publish
ARG TARGETARCH
ARG CONFIG
COPY . ./
RUN dotnet restore "HomeCast/HomeCast.csproj" -a $TARGETARCH
RUN dotnet publish "HomeCast/HomeCast.csproj" -a $TARGETARCH -c $CONFIG -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HomeCast.dll"]