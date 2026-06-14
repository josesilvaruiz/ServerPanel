using ServerPanel.Contracts;
using ServerPanel.Models;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ServerPanel.Services;

public class ServerQueryService : IServerQueryService
{
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger<ServerQueryService> _logger;

    public ServerQueryService(
        IConfiguration config,
        ILogger<ServerQueryService> logger)
    {
        _logger = logger;

        _host = config["ServerQuery:Host"] ?? "127.0.0.1";

        _port = int.TryParse(
            config["ServerQuery:Port"],
            out var p)
            ? p
            : 27015;
    }

    public async Task<ServerInfo> GetServerInfoAsync()
    {
        var info = new ServerInfo
        {
            Ip = _host,
            Port = _port
        };

        try
        {
            _logger.LogInformation(
                "Consultando servidor A2S {Host}:{Port}",
                _host,
                _port);

            using var udp = new UdpClient();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var endpoint = new IPEndPoint(
                IPAddress.Parse(_host),
                _port);

            var query =
                new byte[]
                {
                    0xFF,0xFF,0xFF,0xFF,
                    0x54
                }
                .Concat(
                    Encoding.ASCII.GetBytes(
                        "Source Engine Query\0"))
                .ToArray();

            await udp.SendAsync(
                query,
                query.Length,
                endpoint);

            _logger.LogInformation(
                "Petición enviada");

            var response = await udp.ReceiveAsync(timeoutCts.Token);

            _logger.LogInformation(
                "Bytes recibidos: {Length}",
                response.Buffer.Length);

            _logger.LogInformation(
                "Respuesta HEX: {Hex}",
                BitConverter.ToString(response.Buffer));

            info.RawResponse =
                BitConverter.ToString(response.Buffer);

            using var stream =
                new MemoryStream(response.Buffer);

            using var reader =
                new BinaryReader(stream);

            reader.ReadInt32();

            var header = reader.ReadByte();

            _logger.LogInformation(
                "Header recibido: 0x{Header:X2}",
                header);

            // Challenge A2S
            if (header == 0x41)
            {
                _logger.LogInformation(
                    "Servidor requiere challenge A2S");

                var challenge =
                    reader.ReadBytes(4);

                var challengeQuery =
                    new byte[]
                    {
                        0xFF,0xFF,0xFF,0xFF,
                        0x54
                    }
                    .Concat(
                        Encoding.ASCII.GetBytes(
                            "Source Engine Query\0"))
                    .Concat(challenge)
                    .ToArray();

                await udp.SendAsync(
                    challengeQuery,
                    challengeQuery.Length,
                    endpoint);

                _logger.LogInformation(
                    "Challenge enviado");

                response = await udp.ReceiveAsync(timeoutCts.Token);

                _logger.LogInformation(
                    "Respuesta challenge HEX: {Hex}",
                    BitConverter.ToString(response.Buffer));

                info.RawResponse =
                    BitConverter.ToString(response.Buffer);

                using var stream2 =
                    new MemoryStream(response.Buffer);

                using var reader2 =
                    new BinaryReader(stream2);

                reader2.ReadInt32();

                header = reader2.ReadByte();

                if (header != 0x49)
                {
                    _logger.LogWarning(
                        "Respuesta inesperada tras challenge: 0x{Header:X2}",
                        header);

                    return info;
                }

                ParseInfoResponse(
                    reader2,
                    info);

                return info;
            }

            // Respuesta directa
            if (header == 0x49)
            {
                ParseInfoResponse(
                    reader,
                    info);

                return info;
            }

            _logger.LogWarning(
                "Header desconocido: 0x{Header:X2}",
                header);

            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error consultando servidor A2S");

            info.IsOnline = false;

            return info;
        }
    }

    public async Task<List<PlayerInfo>> GetPlayersAsync()
    {
        var players = new List<PlayerInfo>();
        try
        {
            using var udp = new UdpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var endpoint = new IPEndPoint(IPAddress.Parse(_host), _port);

            // A2S_PLAYER: send initial request with -1 challenge to get real challenge
            var initialReq = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x55, 0xFF, 0xFF, 0xFF, 0xFF };
            await udp.SendAsync(initialReq, initialReq.Length, endpoint);
            var resp1 = await udp.ReceiveAsync(cts.Token);

            if (resp1.Buffer.Length < 5) return players;

            // Server responded with player data directly (no challenge needed)
            if (resp1.Buffer[4] == 0x44)
                return ParseA2SPlayerResponse(resp1.Buffer);

            // Server responded with challenge (0x41)
            if (resp1.Buffer[4] != 0x41 || resp1.Buffer.Length < 9)
                return players;

            var challenge = resp1.Buffer.Skip(5).Take(4).ToArray();
            var playerReq = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x55 }.Concat(challenge).ToArray();
            await udp.SendAsync(playerReq, playerReq.Length, endpoint);
            var resp2 = await udp.ReceiveAsync(cts.Token);

            if (resp2.Buffer.Length >= 5 && resp2.Buffer[4] == 0x44)
                return ParseA2SPlayerResponse(resp2.Buffer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A2S_PLAYER falló para {Host}:{Port}", _host, _port);
        }
        return players;
    }

    private static List<PlayerInfo> ParseA2SPlayerResponse(byte[] buffer)
    {
        var players = new List<PlayerInfo>();
        try
        {
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            reader.ReadInt32(); // FF FF FF FF
            reader.ReadByte(); // 0x44
            var count = reader.ReadByte();

            for (var i = 0; i < count; i++)
            {
                reader.ReadByte(); // player index
                var name = ReadNullTerminated(reader);
                reader.ReadInt32(); // score
                reader.ReadSingle(); // duration

                if (string.IsNullOrWhiteSpace(name)) continue;
                players.Add(new PlayerInfo { Name = name, IsBot = false });
            }
        }
        catch { }
        return players;
    }

    private void ParseInfoResponse(
        BinaryReader reader,
        ServerInfo info)
    {
        // Protocol Version
        reader.ReadByte();

        info.ServerName =
            ReadNullTerminated(reader);

        info.Map =
            ReadNullTerminated(reader);

        info.Folder =
            ReadNullTerminated(reader);

        info.Game =
            ReadNullTerminated(reader);

        info.AppId =
            reader.ReadUInt16();

        var players =
            reader.ReadByte();

        var maxPlayers =
            reader.ReadByte();

        info.Bots =
            reader.ReadByte();

        info.ServerType =
            ((char)reader.ReadByte()).ToString();

        info.Environment =
            ((char)reader.ReadByte()).ToString();

        info.Visibility =
            reader.ReadByte();

        info.Vac =
            reader.ReadByte();

        info.Version =
            ReadNullTerminated(reader);

        info.Players = players;
        info.MaxPlayers = maxPlayers;

        info.IsOnline = true;

        _logger.LogInformation(
            "A2S ServerName={ServerName}",
            info.ServerName);

        _logger.LogInformation(
            "A2S Map={Map}",
            info.Map);

        _logger.LogInformation(
            "A2S Players={Players}",
            info.Players);

        _logger.LogInformation(
            "A2S MaxPlayers={MaxPlayers}",
            info.MaxPlayers);

        _logger.LogInformation(
            "A2S Version={Version}",
            info.Version);
    }

    private static string ReadNullTerminated(
        BinaryReader reader)
    {
        var bytes = new List<byte>();

        byte current;

        while ((current = reader.ReadByte()) != 0)
        {
            bytes.Add(current);
        }

        return Encoding.UTF8.GetString(
            bytes.ToArray());
    }
}