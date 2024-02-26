@ECHO OFF
CHCP 1251 >nul
CD %~dp0

docker build -t socks5server -f Dockerfile .

ECHO.
IF %ERRORLEVEL% NEQ 0 ECHO Error building Dockerfile
ECHO.
REM ECHO Всё выполнено

ECHO Stopping running containers...

docker stop socks1 >nul 2>&1
docker stop socks2 >nul 2>&1
docker stop socks3 >nul 2>&1
docker stop socks4 >nul 2>&1
docker stop socks5 >nul 2>&1
docker stop socks6 >nul 2>&1

ECHO Deleting running containers...

docker rm socks1 >nul 2>&1
docker rm socks2 >nul 2>&1
docker rm socks3 >nul 2>&1
docker rm socks4 >nul 2>&1
docker rm socks5 >nul 2>&1
docker rm socks6 >nul 2>&1

REM Создать виртуальный свитч на определенный интерфейс:
docker network create -d transparent -o com.docker.network.windowsshim.interface="External" tlan

REM Запуск контейнера:
docker run -d --network=tlan --mac-address 00:15:5D:3D:31:01 --restart=always --name socks1 --hostname socks1 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:31:02 --restart=always --name socks2 --hostname socks2 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:31:03 --restart=always --name socks3 --hostname socks3 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:31:04 --restart=always --name socks4 --hostname socks4 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:31:05 --restart=always --name socks5 --hostname socks5 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:31:06 --restart=always --name socks6 --hostname socks6 socks5server