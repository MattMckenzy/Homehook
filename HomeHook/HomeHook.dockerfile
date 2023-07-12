FROM mcr.microsoft.com/dotnet/aspnet:latest AS base
RUN echo "deb http://ftp.debian.org/debian bullseye-backports main" >> /etc/apt/sources.list.d/backports.list
RUN apt update && apt install -t bullseye-backports -y curl python3
RUN curl --create-dirs --output-dir /usr/local/bin -OLJ https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp && chmod 755 /usr/local/bin/yt-dlp
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