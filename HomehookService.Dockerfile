#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["HomehookService/Homehook.csproj", "HomehookService/"]
RUN dotnet restore "HomehookService/Homehook.csproj"
COPY . .
WORKDIR "/src/HomehookService"
RUN dotnet build "Homehook.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Homehook.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Homehook.dll"]