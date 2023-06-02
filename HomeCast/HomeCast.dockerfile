#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:latest AS base
RUN echo "deb http://ftp.debian.org/debian bullseye-backports main" >> /etc/apt/sources.list.d/backports.list
RUN apt update && apt install -t bullseye-backports -y curl alsa-utils pulseaudio evtest socat python3 ffmpeg
RUN curl --create-dirs --output-dir /usr/local/bin -OLJ https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp && chmod 755 /usr/local/bin/yt-dlp
WORKDIR /app
EXPOSE 8121

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/nightly/sdk:8.0-preview AS net-build
ARG TARGETARCH
ARG CONFIG
COPY . ./
RUN dotnet restore "HomeCast/HomeCast.csproj" -a $TARGETARCH
RUN dotnet publish "HomeCast/HomeCast.csproj" -a $TARGETARCH -c $CONFIG -o /app/publish

FROM mattmckenzy/mpv:latest as mpv

FROM base AS final
COPY --from=mpv / /
WORKDIR /app
RUN groupadd --gid 1000 homecast
RUN useradd --system --create-home --gid homecast --uid 1000 homecast
RUN chmod 777 /app
RUN chmod 777 /home/homecast
USER homecast
COPY --from=net-build /app/publish .
ENTRYPOINT ["dotnet", "HomeCast.dll"]