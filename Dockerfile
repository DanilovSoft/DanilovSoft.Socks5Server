#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see https://aka.ms/containercompat

FROM mcr.microsoft.com/dotnet/runtime:8.0-nanoserver-ltsc2022 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-nanoserver-ltsc2022 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SOCKS5Server/SOCKS5Server.csproj", "SOCKS5Server/"]
COPY ["DanilovSoft.Socks5Server/DanilovSoft.Socks5Server.csproj", "DanilovSoft.Socks5Server/"]
RUN dotnet restore "./SOCKS5Server/SOCKS5Server.csproj"
COPY . .
WORKDIR "/src/SOCKS5Server"
RUN dotnet build "./SOCKS5Server.csproj" -c %BUILD_CONFIGURATION% -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SOCKS5Server.csproj" -c %BUILD_CONFIGURATION% -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SOCKS5Server.dll"]