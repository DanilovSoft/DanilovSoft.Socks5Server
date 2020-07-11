namespace DanilovSoft.Socks5Server
{
    internal enum ResponseCode
    {
        /// <summary>
        /// Запрос предоставлен.
        /// </summary>
        RequestSuccess = 0x00,
        /// <summary>
        /// Ошибка SOCKS-сервера.
        /// </summary>
        ErrorSocksServer = 0x01,
        /// <summary>
        /// Соединение запрещено набором правил.
        /// </summary>
        ConnectionNotAllowed = 0x02,
        /// <summary>
        /// Сеть недоступна.
        /// </summary>
        NetworkUnreachable = 0x03,
        /// <summary>
        /// Хост недоступен.
        /// </summary>
        HostUnreachable = 0x04,
        /// <summary>
        /// Отказ в соединении.
        /// </summary>
        ConnectionRefused = 0x05,
        /// <summary>
        ///  Истечение TTL.
        /// </summary>
        TtlOutflow = 0x06,
        /// <summary>
        /// Команда не поддерживается / ошибка протокола.
        /// </summary>
        CommandNotSupportedOrProtocolError = 0x07,
        /// <summary>
        /// Тип адреса не поддерживается.
        /// </summary>
        AddressTypeNotSupported = 0x08
    }
}
