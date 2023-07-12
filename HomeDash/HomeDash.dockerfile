FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
RUN echo "deb http://ftp.debian.org/debian bullseye-backports main" >> /etc/apt/sources.list.d/backports.list
RUN apt update && apt install -t bullseye-backports -y curl dbus alsa-utils pulseaudio libnss3 libatk-bridge2.0-0 libcups2 libgbm1 libgtk-3-0 dbus
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
RUN apt update && apt install -y nodejs 
RUN mkdir /run/dbus && chmod 777 /run/dbus
RUN groupadd --gid 1000 homedash
RUN useradd --system --create-home --gid homedash --uid 1000 homedash
EXPOSE 8120

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS publish
RUN echo "deb http://ftp.debian.org/debian bullseye-backports main" >> /etc/apt/sources.list.d/backports.list
RUN apt update && apt install -t bullseye-backports -y curl libfuse2
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
RUN apt update && apt install -y nodejs
COPY . ./
RUN dotnet tool install ElectronNET.CLI -g
WORKDIR HomeDash
RUN $HOME/.dotnet/tools/electronize build /target linux
WORKDIR bin/Desktop
RUN ./HomeDash-1.0.0.AppImage --appimage-extract
WORKDIR squashfs-root
RUN chmod 4755 chrome-sandbox

FROM base AS final
COPY --from=publish HomeDash/bin/Desktop/squashfs-root /app
WORKDIR /app
USER homedash
ENTRYPOINT ["./home-dash"]