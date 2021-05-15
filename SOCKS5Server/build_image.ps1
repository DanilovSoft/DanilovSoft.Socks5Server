# CHCP 1251 >nul
# Set-Location %~dp0

Get-Content Dockerfile | docker build -t socks_server -
$build_result = $LASTEXITCODE

IF ($build_result -ne 0)
{
    Write-Output Произошла ошибка
}
else 
{
    docker stop socks1
    docker stop socks2
    docker stop socks3
    
    docker rm socks1
    docker rm socks2
    docker rm socks3
    
    # Запуск контейнера:
    docker run -d --network=tlan --mac-address 00:15:5D:3D:30:08 --restart=always --name socks1 --hostname socks1 socks_server
    
    Write-Output Press Any Key
    PAUSE >nul
}