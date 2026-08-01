using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServerPanel.Data;
using ServerPanel.Models;

namespace ServerPanel.Services;

// Sustituye al antiguo sondeo de logs (Cs2PlayerConnectionTrackerBackgroundService): consume en
// tiempo real los eventos que el plugin Cs2EventBridge publica en RabbitMQ (exchange 'cs2.events',
// ver https://github.com/josesilvaruiz/cs2-event-bridge) en vez de leer el log del servidor cada
// 20s. Se queda con el binding '#' (todo el exchange) aposta, no solo 'player.*' — así, cuando el
// plugin añada un tipo de evento nuevo en el futuro, llega aquí sin tener que tocar el binding;
// simplemente se ignoran los tipos que este servicio aún no sabe interpretar.
public class Cs2EventConsumerBackgroundService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Cs2EventConsumerBackgroundService> _logger;

    private IConnection? _connection;
    private IModel? _channel;

    public Cs2EventConsumerBackgroundService(
        IConfiguration config,
        IServiceScopeFactory scopeFactory,
        ILogger<Cs2EventConsumerBackgroundService> logger)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reconnectSeconds = Math.Max(1, _config.GetValue("RabbitMq:ReconnectSeconds", 5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Connect();

                // El consumo real ocurre en el hilo de despacho de RabbitMQ.Client (OnMessageReceived);
                // aquí solo esperamos mientras la conexión siga viva, para poder reconectar si cae.
                while (!stoppingToken.IsCancellationRequested && _connection is { IsOpen: true })
                    await Task.Delay(2000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Apagado normal de la app.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cs2EventConsumer: error de conexión con RabbitMQ, reintentando en segundo plano");
            }
            finally
            {
                Cleanup();
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(reconnectSeconds), stoppingToken); }
                catch (OperationCanceledException) { }
            }
        }

        Cleanup();
    }

    private void Connect()
    {
        var cfg = _config.GetSection("RabbitMq");
        var host = cfg["Host"] ?? "rabbitmq.notifications.svc.cluster.local";
        var port = cfg.GetValue("Port", 5672);
        var exchange = cfg["Exchange"] ?? "cs2.events";
        var queue = cfg["Queue"] ?? "serverpanel.cs2events";

        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = port,
            UserName = cfg["Username"] ?? "guest",
            Password = cfg["Password"] ?? "guest",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(Math.Max(1, cfg.GetValue("ReconnectSeconds", 5))),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
        };

        _connection = factory.CreateConnection("ServerPanel-Cs2EventConsumer");
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(queue, exchange, routingKey: "#");

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceived;

        // autoAck: preferimos perder algún evento puntual (p.ej. si el pod muere justo entre la
        // entrega y el insert en BD) antes que arriesgarnos a un reenvío/duplicado por un ack que
        // nunca llega — el usuario fue explícito en que no quiere eventos duplicados en el panel.
        _channel.BasicConsume(queue, autoAck: true, consumer: consumer);

        _logger.LogInformation("Cs2EventConsumer: conectado a RabbitMQ {Host}:{Port}, cola '{Queue}' (exchange '{Exchange}')",
            host, port, queue, exchange);
    }

    private void OnMessageReceived(object? sender, BasicDeliverEventArgs ea)
    {
        // El evento de RabbitMQ.Client se dispara en su propio hilo de despacho, ya fuera de
        // cualquier ruta crítica — igualmente no bloqueamos ahí: se lanza el trabajo async y se
        // vuelve enseguida.
        _ = ProcessMessageAsync(ea.Body.ToArray());
    }

    private async Task ProcessMessageAsync(byte[] body)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<IncomingEvent>(Encoding.UTF8.GetString(body), JsonOpts);
            if (msg is null) return;

            var entity = msg.EventType switch
            {
                "player.connected" => BuildEvent(msg, Cs2ConnectionEventType.Connect),
                "player.disconnected" => BuildEvent(msg, Cs2ConnectionEventType.Disconnect),
                _ => null, // tipos de evento futuros que este servicio aún no interpreta — se ignoran
            };
            if (entity is null) return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var exists = await db.Cs2PlayerConnectionEvents.AnyAsync(e =>
                e.PlayerName == entity.PlayerName &&
                e.EventType == entity.EventType &&
                e.TimestampUtc == entity.TimestampUtc);
            if (exists) return;

            db.Cs2PlayerConnectionEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cs2EventConsumer: error procesando mensaje de RabbitMQ");
        }
    }

    private static Cs2PlayerConnectionEvent? BuildEvent(IncomingEvent msg, Cs2ConnectionEventType type)
    {
        if (msg.Data is not { ValueKind: JsonValueKind.Object } data) return null;
        if (!data.TryGetProperty("name", out var nameProp) || nameProp.GetString() is not { } name) return null;

        string? steamId64 = data.TryGetProperty("steamId64", out var steamProp) && steamProp.ValueKind == JsonValueKind.String
            ? steamProp.GetString()
            : null;

        return new Cs2PlayerConnectionEvent
        {
            TimestampUtc = msg.TimestampUtc,
            EventType = type,
            PlayerName = name,
            SteamId64 = steamId64,
            ServerName = msg.ServerName,
        };
    }

    private void Cleanup()
    {
        try { _channel?.Close(); _channel?.Dispose(); } catch { /* apagado defensivo */ }
        try { _connection?.Close(TimeSpan.FromSeconds(1)); _connection?.Dispose(); } catch { }
        _channel = null;
        _connection = null;
    }

    private sealed class IncomingEvent
    {
        public string EventType { get; set; } = "";
        public DateTime TimestampUtc { get; set; }
        public string ServerName { get; set; } = "";
        public JsonElement? Data { get; set; }
    }
}
