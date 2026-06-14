using System.Text;
using ServerPanel.Contracts;

namespace ServerPanel.Services;

public class RconService : IRconService
{
    private readonly ISshService _ssh;
    private readonly int _port;
    private readonly string _password;
    private readonly ILogger<RconService> _logger;

    public RconService(ISshService ssh, IConfiguration configuration, ILogger<RconService> logger)
    {
        _ssh      = ssh;
        _logger   = logger;
        var s     = configuration.GetSection("Rcon");
        _port     = int.TryParse(s["Port"], out var p) ? p : 27015;
        _password = s["Password"] ?? "";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_password);

    public async Task<string> ExecuteAsync(string command, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return "[RCON no configurado — añade Rcon:Password en appsettings.json]";

        // Route RCON through SSH to the node's localhost.
        // CS2 pod runs with hostNetwork:true, so its RCON port is directly on 127.0.0.1 of the node.
        var b64pwd    = Convert.ToBase64String(Encoding.UTF8.GetBytes(_password));
        var b64cmd    = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        var pyScript  = BuildScript(b64pwd, b64cmd);
        var scriptB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(pyScript));
        var sshCmd    = $"python3 -c \"exec(__import__('base64').b64decode('{scriptB64}').decode())\"";

        try
        {
            var output = await _ssh.ExecuteAsync(sshCmd);
            _logger.LogInformation("SSH-RCON '{Cmd}' => '{Out}'", command, output.Trim());
            return output.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSH-RCON error ejecutando '{Cmd}'", command);
            return $"[RCON error: {ex.Message}]";
        }
    }

    private string BuildScript(string b64pwd, string b64cmd) => $@"import socket, struct, base64

pwd  = base64.b64decode('{b64pwd}').decode()
cmd  = base64.b64decode('{b64cmd}').decode()
port = {_port}
TERM = bytes(2)

def send(s, id, type, body):
    d = body.encode('utf-8')
    s.sendall(struct.pack('<iii', 4 + 4 + len(d) + 2, id, type) + d + TERM)

def recv(s):
    h = b''
    while len(h) < 12:
        x = s.recv(12 - len(h))
        if not x:
            break
        h += x
    if len(h) < 12:
        return -1, -1, ''
    sz, id, tp = struct.unpack('<iii', h)
    r = b''
    while len(r) < sz - 8:
        x = s.recv(sz - 8 - len(r))
        if not x:
            break
        r += x
    return id, tp, r.rstrip(bytes(1)).decode('utf-8', 'replace')

try:
    with socket.create_connection(('127.0.0.1', port), 5) as s:
        send(s, 1, 3, pwd)
        pk = recv(s)
        auth = pk if pk[1] == 2 else recv(s)
        if auth[0] == -1:
            print('[RCON: contrasena incorrecta]')
        else:
            send(s, 2, 2, cmd)
            try:
                out = recv(s)[2]
                if out:
                    print(out)
            except Exception:
                pass
except Exception as e:
    print('[RCON error: ' + str(e) + ']')
";
}
