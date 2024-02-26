@ECHO OFF
CHCP 1251 >nul
CD %~dp0

docker build -t socks5server -f Dockerfile .

ECHO.
IF %ERRORLEVEL% NEQ 0 "Произошла ошибка"
ECHO.
REM ECHO Всё выполнено

docker stop socks1
docker stop socks2
docker stop socks3
docker stop socks4

docker rm socks1
docker rm socks2
docker rm socks3
docker rm socks4

REM Создать виртуальный свитч на определенный интерфейс:
docker network create -d transparent -o com.docker.network.windowsshim.interface="External" tlan

REM Запуск контейнера:
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:01 --restart=always --name socks1 --hostname socks1 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:02 --restart=always --name socks2 --hostname socks2 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:03 --restart=always --name socks3 --hostname socks3 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:04 --restart=always --name socks4 --hostname socks4 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:05 --restart=always --name socks5 --hostname socks5 socks5server
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:06 --restart=always --name socks6 --hostname socks6 socks5server