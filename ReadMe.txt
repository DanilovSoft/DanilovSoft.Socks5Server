Если контейнеры у нас будут на Windows то версия OS в контейнере должна совпадать с версией OS хоста, например используем '8.0-nanoserver-ltsc2022'

# Выполнить Publish.

# Создать образ из файлов 'publish'.
docker build -t socks_server -f Dockerfile .

# Создать виртуальный свитч на определенный интерфейс:
docker network create -d transparent -o com.docker.network.windowsshim.interface="External" tlan

# Запуск контейнера:
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:01 --restart=always --name node1 ex-node --host=10.0.0.132 --port=1234 --id=6C3C7072-5415-46F6-9B77-5C09F316AC01
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:02 --restart=always --name node2 ex-node --host=10.0.0.132 --port=1234 --id=6C3C7072-5415-46F6-9B77-5C09F316AC02
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:03 --restart=always --name node3 ex-node --host=10.0.0.132 --port=1234 --id=6C3C7072-5415-46F6-9B77-5C09F316AC03
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:04 --restart=always --name node4 ex-node --host=10.0.0.132 --port=1234 --id=6C3C7072-5415-46F6-9B77-5C09F316AC04
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:05 --restart=always --name node5 ex-node --host=10.0.0.132 --port=1234 --id=6C3C7072-5415-46F6-9B77-5C09F316AC05
docker run -d --network=tlan --mac-address 00:15:5D:3D:30:06 --restart=always --name node6 ex-node --host=10.0.0.132 --port=1234 --id=6C3C7072-5415-46F6-9B77-5C09F316AC06
