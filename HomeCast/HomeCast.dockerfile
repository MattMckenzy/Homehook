FROM mcr.microsoft.com/dotnet/aspnet:7.0-bookworm-slim AS base
RUN apt update
RUN apt install -y curl 
RUN apt install -y pulseaudio 
RUN apt install -y evtest 
RUN apt install -y socat 
RUN apt install -y python3 
RUN apt install -y ffmpeg
RUN apt install -y yt-dlp
RUN apt install -y mpv
WORKDIR /app
EXPOSE 8121

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/nightly/sdk:8.0-preview AS net-build
ARG TARGETARCH
ARG CONFIG
COPY . ./
RUN dotnet restore "HomeCast/HomeCast.csproj" -a $TARGETARCH
RUN dotnet publish "HomeCast/HomeCast.csproj" -a $TARGETARCH -c $CONFIG --self-contained -o /app/publish

FROM base AS final
WORKDIR /app
RUN groupadd --gid 1000 homecast
RUN useradd --system --create-home --gid homecast --uid 1000 homecast
RUN chmod 777 /app
RUN chmod 777 /home/homecast
USER homecast
COPY --from=net-build /app/publish .
ENTRYPOINT ["dotnet", "HomeCast.dll"]