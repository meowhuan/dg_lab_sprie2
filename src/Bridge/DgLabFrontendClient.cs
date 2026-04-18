using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DgLabSocketSpire2.Configuration;

namespace DgLabSocketSpire2.Bridge;

internal sealed class DgLabFrontendClient : IDisposable
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly BridgeService _service;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private string? _clientId;
    private string? _targetId;
    private bool _isBound;
    private int _strengthA;
    private int _strengthB;
    private int _limitA;
    private int _limitB;
    private string _lastFeedback = string.Empty;
    private string _lastError = string.Empty;
    private string _lastNotice = string.Empty;

    public DgLabFrontendClient(BridgeService service)
    {
        _service = service;
    }

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public bool IsBound
    {
        get
        {
            lock (_stateGate)
            {
                return _isBound;
            }
        }
    }

    public string? ClientId
    {
        get
        {
            lock (_stateGate)
            {
                return _clientId;
            }
        }
    }

    public string? TargetId
    {
        get
        {
            lock (_stateGate)
            {
                return _targetId;
            }
        }
    }

    public int StrengthA
    {
        get
        {
            lock (_stateGate)
            {
                return _strengthA;
            }
        }
    }

    public int StrengthB
    {
        get
        {
            lock (_stateGate)
            {
                return _strengthB;
            }
        }
    }

    public int LimitA
    {
        get
        {
            lock (_stateGate)
            {
                return _limitA;
            }
        }
    }

    public int LimitB
    {
        get
        {
            lock (_stateGate)
            {
                return _limitB;
            }
        }
    }

    public string LastFeedback
    {
        get
        {
            lock (_stateGate)
            {
                return _lastFeedback;
            }
        }
    }

    public string LastError
    {
        get
        {
            lock (_stateGate)
            {
                return _lastError;
            }
        }
    }

    public string LastNotice
    {
        get
        {
            lock (_stateGate)
            {
                return _lastNotice;
            }
        }
    }

    public void Start(int port)
    {
        if (_loopTask != null)
        {
            return;
        }

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(port, _loopCts.Token));
    }

    public async Task SendStrengthAsync(ChannelRef channel, StrengthOperation operation, int value)
    {
        var targetId = TargetId;
        var clientId = ClientId;
        if (!IsConnected || !IsBound || string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(clientId))
        {
            ModLog.Warn($"Skipped SendStrengthAsync because frontend is not ready. connected={IsConnected}, bound={IsBound}, clientId={clientId}, targetId={targetId}");
            return;
        }

        string message = operation switch
        {
            StrengthOperation.Set => $"strength-{(int)channel}+2+{Math.Clamp(value, 0, 200)}",
            StrengthOperation.DeltaUp => $"strength-{(int)channel}+1+{Math.Clamp(value, 0, 200)}",
            StrengthOperation.DeltaDown => $"strength-{(int)channel}+0+{Math.Clamp(value, 0, 200)}",
            StrengthOperation.Clear => $"strength-{(int)channel}+2+0",
            _ => $"strength-{(int)channel}+2+{Math.Clamp(value, 0, 200)}"
        };

        await SendEnvelopeAsync(new
        {
            type = 4,
            clientId,
            targetId,
            message
        });
        ModLog.Info($"Sent strength message: {message}");
    }

    public async Task SendWaveAsync(ChannelRef channel, string[] frames, int durationSeconds)
    {
        var targetId = TargetId;
        var clientId = ClientId;
        if (!IsConnected || !IsBound || string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(clientId))
        {
            ModLog.Warn($"Skipped SendWaveAsync because frontend is not ready. connected={IsConnected}, bound={IsBound}, clientId={clientId}, targetId={targetId}");
            return;
        }

        var channelName = channel == ChannelRef.A ? "A" : "B";
        var payload = $"{channelName}:{JsonSerializer.Serialize(frames)}";

        await SendEnvelopeAsync(new
        {
            type = "clientMsg",
            clientId,
            targetId,
            channel = channelName,
            time = Math.Max(1, durationSeconds),
            message = payload
        });
        ModLog.Info($"Sent wave message: channel={channelName}, duration={durationSeconds}, frames={frames.Length}");
    }

    public async Task ClearChannelAsync(ChannelRef channel)
    {
        var targetId = TargetId;
        var clientId = ClientId;
        if (!IsConnected || !IsBound || string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(clientId))
        {
            ModLog.Warn($"Skipped ClearChannelAsync because frontend is not ready. connected={IsConnected}, bound={IsBound}, clientId={clientId}, targetId={targetId}");
            return;
        }

        await SendEnvelopeAsync(new
        {
            type = 4,
            clientId,
            targetId,
            message = $"clear-{(int)channel}"
        });
        ModLog.Info($"Sent clear message for channel {(int)channel}");
    }

    private async Task SendEnvelopeAsync(object envelope)
    {
        var socket = _socket;
        if (socket == null || socket.State != WebSocketState.Open)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(payload);
        await _sendLock.WaitAsync();
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to send frontend message: {ex.Message}");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunLoopAsync(int port, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                _socket = socket;
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cancellationToken);
                ModLog.Info("Frontend client connected to local DG-LAB bridge.");
                _service.NotifyFrontendConnectionChanged();
                await ReceiveLoopAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Frontend loop error: {ex.Message}");
            }
            finally
            {
                _socket = null;
                lock (_stateGate)
                {
                    _isBound = false;
                    _targetId = null;
                }
                _service.NotifyFrontendConnectionChanged();
            }

            try
            {
                await Task.Delay(1500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "frontend_close", CancellationToken.None);
                    return;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var raw = Encoding.UTF8.GetString(ms.ToArray());
            HandleServerMessage(raw);
        }
    }

    private void HandleServerMessage(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.ToString() : string.Empty;
            var clientId = root.TryGetProperty("clientId", out var clientElement) ? clientElement.GetString() : null;
            var targetId = root.TryGetProperty("targetId", out var targetElement) ? targetElement.GetString() : null;
            var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? string.Empty : string.Empty;

            switch (type)
            {
                case "bind":
                    lock (_stateGate)
                    {
                        if (string.IsNullOrWhiteSpace(_clientId) && string.IsNullOrWhiteSpace(targetId) && message == "targetId")
                        {
                            _clientId = clientId;
                        }
                        else if (message == "200")
                        {
                            _clientId ??= clientId;
                            _targetId = string.Equals(clientId, _clientId, StringComparison.Ordinal) ? targetId : clientId;
                            _isBound = true;
                            _lastNotice = "APP paired.";
                        }
                    }
                    break;
                case "break":
                    lock (_stateGate)
                    {
                        _targetId = null;
                        _isBound = false;
                        _lastNotice = "APP disconnected.";
                    }
                    break;
                case "error":
                    lock (_stateGate)
                    {
                        _lastError = $"Protocol error {message}";
                    }
                    break;
                case "notify":
                    lock (_stateGate)
                    {
                        _lastNotice = message;
                    }
                    break;
                case "heartbeat":
                    break;
                case "msg":
                    ParseAppPayload(message);
                    break;
            }

            _service.NotifyFrontendMessageReceived();
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to parse frontend server message: {ex.Message}");
        }
    }

    private void ParseAppPayload(string message)
    {
        lock (_stateGate)
        {
            if (message.StartsWith("strength-", StringComparison.OrdinalIgnoreCase))
            {
                var payload = message["strength-".Length..].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (payload.Length >= 4
                    && int.TryParse(payload[0], out var a)
                    && int.TryParse(payload[1], out var b)
                    && int.TryParse(payload[2], out var limitA)
                    && int.TryParse(payload[3], out var limitB))
                {
                    _strengthA = a;
                    _strengthB = b;
                    _limitA = limitA;
                    _limitB = limitB;
                }
            }
            else if (message.StartsWith("feedback-", StringComparison.OrdinalIgnoreCase))
            {
                _lastFeedback = message;
            }
            else
            {
                _lastNotice = message;
            }
        }
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _socket?.Dispose();
        _sendLock.Dispose();
        _loopCts?.Dispose();
    }
}
