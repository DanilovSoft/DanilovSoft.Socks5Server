#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see https://aka.ms/containercompat

FROM mcr.microsoft.com/dotnet/core/runtime:3.1-nanoserver-1809 AS base
WORKDIR /app

# COPY bin/Release/netcoreapp3.1/publish/ ./
# ENTRYPOINT ["dotnet", "SOCKS5Server.dll"]

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-nanoserver-1809 AS build
WORKDIR /src
COPY ["SOCKS5Server/SOCKS5Server.csproj", "SOCKS5Server/"]
COPY ["DanilovSoft.Socks5Server/DanilovSoft.Socks5Server.csproj", "DanilovSoft.Socks5Server/"]
RUN cmd
COPY . .
WORKDIR "/src/SOCKS5Server"
RUN dotnet build "SOCKS5Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SOCKS5Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SOCKS5Server.dll"]