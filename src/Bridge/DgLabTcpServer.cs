using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DgLabSocketSpire2.Configuration;

namespace DgLabSocketSpire2.Bridge;

internal sealed class DgLabTcpServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly ConcurrentDictionary<string, ConnectedClient> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pairs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _reversePairs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _channelTimers = new(StringComparer.Ordinal);
    private readonly BridgeService _service;
    private readonly int _port;
    private readonly int _heartbeatIntervalMs;
    private readonly int _defaultPunishmentFrequency;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private Task? _heartbeatLoop;

    public DgLabTcpServer(BridgeService service, ServerConfig config)
    {
        _service = service;
        _port = config.Port;
        _heartbeatIntervalMs = Math.Max(10_000, config.HeartbeatIntervalMs);
        _defaultPunishmentFrequency = Math.Max(1, config.DefaultPunishmentFrequency);
    }

    public bool IsRunning => _listener != null;

    public int Port => _port;

    public int ConnectionCount => _connections.Count;

    public int PairCount => _pairs.Count;

    public void Start()
    {
        if (_listener != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _heartbeatLoop = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        ModLog.Info($"TCP bridge listening on port {_port}.");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch
        {
        }

        foreach (var client in _connections.Values)
        {
            try
            {
                client.Socket.Abort();
                client.Socket.Dispose();
            }
            catch
            {
            }
        }

        foreach (var timer in _channelTimers.Values)
        {
            timer.Cancel();
            timer.Dispose();
        }

        _connections.Clear();
        _pairs.Clear();
        _reversePairs.Clear();
        _channelTimers.Clear();
        _listener = null;
        _cts?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleTcpClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Accept loop error: {ex.Message}");
                client?.Dispose();
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var client = tcpClient;
        using var stream = client.GetStream();

        try
        {
            var requestBytes = await ReadHeaderAsync(stream, cancellationToken);
            if (requestBytes.Length == 0)
            {
                return;
            }

            var requestText = Encoding.ASCII.GetString(requestBytes);
            var request = HttpRequestInfo.Parse(requestText);
            if (request == null)
            {
                return;
            }

            if (request.IsWebSocketRequest)
            {
                await HandleWebSocketAsync(stream, request, cancellationToken);
            }
            else
            {
                await HandleHttpAsync(stream, request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"TCP client handling failed: {ex.Message}");
        }
    }

    private async Task HandleHttpAsync(NetworkStream stream, HttpRequestInfo request, CancellationToken cancellationToken)
    {
        var requestUri = new Uri($"http://localhost{request.Path}");
        var path = requestUri.AbsolutePath;
        var query = ParseQuery(requestUri.Query);

        if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
        {
            var payload = JsonSerializer.Serialize(_service.GetStatusSnapshot(), JsonOptions);
            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(payload), cancellationToken);
            return;
        }

        if (path.Equals("/api/control/state", StringComparison.OrdinalIgnoreCase))
        {
            var payload = JsonSerializer.Serialize(_service.GetControlPanelState(), JsonOptions);
            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(payload), cancellationToken);
            return;
        }

        if (path.Equals("/api/control/preset", StringComparison.OrdinalIgnoreCase))
        {
            if (query.TryGetValue("name", out var presetName) && !string.IsNullOrWhiteSpace(presetName))
            {
                _service.SetPreset(presetName);
            }

            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/global/save", StringComparison.OrdinalIgnoreCase))
        {
            if (!query.TryGetValue("settings", out var settingsJson) || string.IsNullOrWhiteSpace(settingsJson))
            {
                await WriteHttpResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Missing settings payload"), cancellationToken);
                return;
            }

            ControlPanelGlobalSettings? settings;
            try
            {
                settings = JsonSerializer.Deserialize<ControlPanelGlobalSettings>(settingsJson, JsonOptions);
            }
            catch (JsonException)
            {
                settings = null;
            }

            if (settings == null)
            {
                await WriteHttpResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Invalid settings payload"), cancellationToken);
                return;
            }

            _service.UpdateGlobalSettings(settings);
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/toggle-enabled", StringComparison.OrdinalIgnoreCase))
        {
            _service.ToggleEnabled();
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/reload", StringComparison.OrdinalIgnoreCase))
        {
            _service.ReloadConfig();
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/save-settings", StringComparison.OrdinalIgnoreCase))
        {
            _service.SaveSettings();
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/clear", StringComparison.OrdinalIgnoreCase))
        {
            await _service.ClearAllAsync();
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/test-wave", StringComparison.OrdinalIgnoreCase))
        {
            var wave = query.TryGetValue("wave", out var waveName) && !string.IsNullOrWhiteSpace(waveName) ? waveName : "连击";
            var channel = query.TryGetValue("channel", out var channelText) && channelText.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? ChannelRef.B
                : ChannelRef.A;
            await _service.SendTestPulseAsync(wave, channel);
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/test-strength", StringComparison.OrdinalIgnoreCase))
        {
            var channel = query.TryGetValue("channel", out var channelText) && channelText.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? ChannelRef.B
                : ChannelRef.A;
            var value = query.TryGetValue("value", out var valueText) && int.TryParse(valueText, out var parsedValue)
                ? parsedValue
                : 35;
            await _service.SendTestStrengthAsync(channel, value);
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/configure-strength", StringComparison.OrdinalIgnoreCase))
        {
            var channel = query.TryGetValue("channel", out var channelText) && channelText.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? ChannelRef.B
                : ChannelRef.A;
            var value = query.TryGetValue("value", out var valueText) && int.TryParse(valueText, out var parsedValue)
                ? parsedValue
                : 35;
            _service.SetConfiguredTestStrength(channel, value);
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/control/rule/save", StringComparison.OrdinalIgnoreCase))
        {
            if (!query.TryGetValue("eventType", out var eventTypeText)
                || !Enum.TryParse<BridgeEventType>(eventTypeText, true, out var eventType)
                || !query.TryGetValue("rule", out var ruleJson)
                || string.IsNullOrWhiteSpace(ruleJson))
            {
                await WriteHttpResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Invalid rule request"), cancellationToken);
                return;
            }

            EventRuleConfig? rule;
            try
            {
                rule = JsonSerializer.Deserialize<EventRuleConfig>(ruleJson, JsonOptions);
            }
            catch (JsonException)
            {
                rule = null;
            }

            if (rule == null)
            {
                await WriteHttpResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Invalid rule payload"), cancellationToken);
                return;
            }

            _service.SaveRule(eventType, rule);
            await WriteJsonOkAsync(stream, cancellationToken);
            return;
        }

        if (path.Equals("/api/debug/wave", StringComparison.OrdinalIgnoreCase))
        {
            var wave = query.TryGetValue("wave", out var waveName) ? waveName : "连击";
            var channel = query.TryGetValue("channel", out var channelText) && channelText.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? ChannelRef.B
                : ChannelRef.A;
            await _service.SendTestPulseAsync(wave, channel);
            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes("""{"ok":true}"""), cancellationToken);
            return;
        }

        if (path.Equals("/api/debug/strength", StringComparison.OrdinalIgnoreCase))
        {
            var channel = query.TryGetValue("channel", out var channelText) && channelText.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? ChannelRef.B
                : ChannelRef.A;
            var value = query.TryGetValue("value", out var valueText) && int.TryParse(valueText, out var parsedValue)
                ? parsedValue
                : 35;
            await _service.SendTestStrengthAsync(channel, value);
            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes("""{"ok":true}"""), cancellationToken);
            return;
        }

        if (path.Equals("/api/debug/clear", StringComparison.OrdinalIgnoreCase))
        {
            await _service.ClearAllAsync();
            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes("""{"ok":true}"""), cancellationToken);
            return;
        }

        if (path.Equals("/", StringComparison.OrdinalIgnoreCase) || path.Equals("/pair", StringComparison.OrdinalIgnoreCase))
        {
            var payload = Encoding.UTF8.GetBytes(_service.BuildPairPageHtml());
            await WriteHttpResponseAsync(stream, "200 OK", "text/html; charset=utf-8", payload, cancellationToken);
            return;
        }

        if (path.Equals("/control", StringComparison.OrdinalIgnoreCase))
        {
            var payload = Encoding.UTF8.GetBytes(_service.BuildControlPageHtml());
            await WriteHttpResponseAsync(stream, "200 OK", "text/html; charset=utf-8", payload, cancellationToken);
            return;
        }

        await WriteHttpResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"), cancellationToken);
    }

    private async Task HandleWebSocketAsync(NetworkStream stream, HttpRequestInfo request, CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("Sec-WebSocket-Key", out var key))
        {
            await WriteHttpResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Missing Sec-WebSocket-Key"), cancellationToken);
            return;
        }

        var acceptKey = ComputeWebSocketAccept(key);
        var response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
        var responseBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(responseBytes, cancellationToken);

        var socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));
        var clientId = Guid.NewGuid().ToString();
        var connected = new ConnectedClient(clientId, socket);
        _connections[clientId] = connected;
        _service.NotifyServerStateChanged();

        await SendJsonAsync(connected, new
        {
            type = "bind",
            clientId,
            targetId = "",
            message = "targetId"
        }, cancellationToken);

        try
        {
            await ReceiveLoopAsync(connected, cancellationToken);
        }
        finally
        {
            await DisconnectAsync(clientId, notifyPair: true);
        }
    }

    private async Task ReceiveLoopAsync(ConnectedClient client, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (client.Socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await client.Socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", cancellationToken);
                    }
                    catch
                    {
                    }
                    return;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            client.LastHeartbeatUtc = DateTimeOffset.UtcNow;
            var raw = Encoding.UTF8.GetString(ms.ToArray());
            await HandleWebSocketMessageAsync(client, raw, cancellationToken);
        }
    }

    private async Task HandleWebSocketMessageAsync(ConnectedClient source, string raw, CancellationToken cancellationToken)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                await SendErrorCodeAsync(source, "", "", "403", cancellationToken);
                return;
            }

            if (!TryGetRequiredString(root, "clientId", out var clientId)
                || !TryGetRequiredString(root, "targetId", out var targetId)
                || !TryGetRequiredString(root, "message", out var message))
            {
                await SendErrorCodeAsync(source, "", "", "404", cancellationToken);
                return;
            }

            if (!ValidateSource(source, clientId, targetId))
            {
                await SendErrorCodeAsync(source, clientId, targetId, "404", cancellationToken);
                return;
            }

            var typeElement = root.GetProperty("type");
            switch (typeElement.ValueKind)
            {
                case JsonValueKind.String:
                {
                    var typeText = typeElement.GetString() ?? string.Empty;
                    switch (typeText)
                    {
                        case "bind":
                            await HandleBindAsync(source, clientId, targetId, cancellationToken);
                            break;
                        case "clientMsg":
                            if (!TryGetRequiredString(root, "channel", out var channel))
                            {
                                await SendErrorCodeAsync(source, clientId, targetId, "406", cancellationToken);
                                break;
                            }

                            var timeSeconds = root.TryGetProperty("time", out var timeElement) && timeElement.TryGetInt32(out var parsedTime)
                                ? parsedTime
                                : 1;
                            await HandleWaveAsync(clientId, targetId, channel, message, Math.Max(1, timeSeconds), cancellationToken);
                            break;
                        default:
                            await ForwardToFrontendAsync(clientId, targetId, typeText, message, cancellationToken);
                            break;
                    }
                    break;
                }
                case JsonValueKind.Number:
                {
                    if (!typeElement.TryGetInt32(out var numberType))
                    {
                        await SendErrorCodeAsync(source, clientId, targetId, "403", cancellationToken);
                        break;
                    }

                    switch (numberType)
                    {
                        case 1:
                        case 2:
                        case 3:
                            await HandleStrengthAdjustAsync(root, clientId, targetId, numberType, cancellationToken);
                            break;
                        case 4:
                            await ForwardToTargetAsync(clientId, targetId, message, cancellationToken);
                            break;
                        default:
                            await SendErrorCodeAsync(source, clientId, targetId, "403", cancellationToken);
                            break;
                    }
                    break;
                }
                default:
                    await SendErrorCodeAsync(source, clientId, targetId, "403", cancellationToken);
                    break;
            }
        }
        catch (JsonException)
        {
            await SendErrorCodeAsync(source, "", "", "403", cancellationToken);
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Server message handling failed: {ex.Message}");
            await SendErrorCodeAsync(source, "", "", "500", cancellationToken);
        }
        finally
        {
            document?.Dispose();
        }
    }

    private async Task HandleBindAsync(ConnectedClient source, string clientId, string targetId, CancellationToken cancellationToken)
    {
        if (!_connections.ContainsKey(clientId) || !_connections.ContainsKey(targetId))
        {
            await SendBindResponseAsync(clientId, targetId, "401", cancellationToken);
            return;
        }

        if (_pairs.ContainsKey(clientId) || _reversePairs.ContainsKey(targetId))
        {
            await SendBindResponseAsync(clientId, targetId, "400", cancellationToken);
            return;
        }

        _pairs[clientId] = targetId;
        _reversePairs[targetId] = clientId;
        await SendBindResponseAsync(clientId, targetId, "200", cancellationToken);
        _service.NotifyServerStateChanged();
    }

    private async Task HandleStrengthAdjustAsync(JsonElement root, string clientId, string targetId, int type, CancellationToken cancellationToken)
    {
        if (!IsPaired(clientId, targetId))
        {
            await SendErrorCodeToClientAsync(clientId, targetId, "402", cancellationToken);
            return;
        }

        var channel = root.TryGetProperty("channel", out var channelElement) && channelElement.TryGetInt32(out var parsedChannel)
            ? parsedChannel
            : 1;
        var sendType = type - 1;
        var strength = type >= 3 && root.TryGetProperty("strength", out var strengthElement) && strengthElement.TryGetInt32(out var parsedStrength)
            ? parsedStrength
            : 1;

        await ForwardToTargetAsync(clientId, targetId, $"strength-{channel}+{sendType}+{strength}", cancellationToken);
    }

    private async Task HandleWaveAsync(string clientId, string targetId, string channel, string message, int durationSeconds, CancellationToken cancellationToken)
    {
        if (!IsPaired(clientId, targetId))
        {
            await SendErrorCodeToClientAsync(clientId, targetId, "402", cancellationToken);
            return;
        }

        if (!_connections.TryGetValue(targetId, out var target))
        {
            await SendErrorCodeToClientAsync(clientId, targetId, "404", cancellationToken);
            return;
        }

        if (!string.Equals(channel, "A", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(channel, "B", StringComparison.OrdinalIgnoreCase))
        {
            await SendErrorCodeToClientAsync(clientId, targetId, "406", cancellationToken);
            return;
        }

        var timerKey = $"{clientId}-{channel.ToUpperInvariant()}";
        if (_channelTimers.TryRemove(timerKey, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
            await ForwardToTargetAsync(clientId, targetId, $"clear-{(string.Equals(channel, "A", StringComparison.OrdinalIgnoreCase) ? 1 : 2)}", cancellationToken);
            await Task.Delay(150, cancellationToken);
        }

        var waveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _channelTimers[timerKey] = waveCts;
        _ = Task.Run(async () =>
        {
            try
            {
                var totalSends = Math.Max(1, _defaultPunishmentFrequency * durationSeconds);
                var intervalMs = Math.Max(50, 1000 / _defaultPunishmentFrequency);
                for (var index = 0; index < totalSends && !waveCts.IsCancellationRequested; index++)
                {
                    await SendJsonAsync(target, new
                    {
                        type = "msg",
                        clientId,
                        targetId,
                        message = $"pulse-{message}"
                    }, waveCts.Token);

                    if (index + 1 < totalSends)
                    {
                        await Task.Delay(intervalMs, waveCts.Token);
                    }
                }

                if (_connections.TryGetValue(clientId, out var source))
                {
                    await SendJsonAsync(source, new
                    {
                        type = "notify",
                        clientId,
                        targetId,
                        message = "发送完毕"
                    }, CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Wave timer failed: {ex.Message}");
            }
            finally
            {
                if (_channelTimers.TryGetValue(timerKey, out var activeCts) && activeCts == waveCts)
                {
                    _channelTimers.TryRemove(timerKey, out _);
                }
                waveCts.Dispose();
            }
        }, waveCts.Token);
    }

    private async Task ForwardToTargetAsync(string clientId, string targetId, string appMessage, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(targetId, out var target))
        {
            await SendErrorCodeToClientAsync(clientId, targetId, "404", cancellationToken);
            return;
        }

        await SendJsonAsync(target, new
        {
            type = "msg",
            clientId,
            targetId,
            message = appMessage
        }, cancellationToken);
    }

    private async Task ForwardToFrontendAsync(string clientId, string targetId, string type, string message, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(clientId, out var client))
        {
            return;
        }

        await SendJsonAsync(client, new
        {
            type,
            clientId,
            targetId,
            message
        }, cancellationToken);
    }

    private async Task SendBindResponseAsync(string clientId, string targetId, string code, CancellationToken cancellationToken)
    {
        var payload = new
        {
            type = "bind",
            clientId,
            targetId,
            message = code
        };

        if (_connections.TryGetValue(clientId, out var web))
        {
            await SendJsonAsync(web, payload, cancellationToken);
        }

        if (_connections.TryGetValue(targetId, out var app) && app.ClientId != clientId)
        {
            await SendJsonAsync(app, payload, cancellationToken);
        }
    }

    private async Task SendErrorCodeAsync(ConnectedClient client, string clientId, string targetId, string code, CancellationToken cancellationToken)
    {
        await SendJsonAsync(client, new
        {
            type = "error",
            clientId,
            targetId,
            message = code
        }, cancellationToken);
    }

    private async Task SendErrorCodeToClientAsync(string clientId, string targetId, string code, CancellationToken cancellationToken)
    {
        if (_connections.TryGetValue(clientId, out var source))
        {
            await SendErrorCodeAsync(source, clientId, targetId, code, cancellationToken);
        }
    }

    private bool ValidateSource(ConnectedClient source, string clientId, string targetId)
    {
        return source.ClientId == clientId || source.ClientId == targetId;
    }

    private bool IsPaired(string clientId, string targetId)
    {
        return _pairs.TryGetValue(clientId, out var right) && right == targetId
            || _reversePairs.TryGetValue(clientId, out var left) && left == targetId;
    }

    private async Task DisconnectAsync(string clientId, bool notifyPair)
    {
        if (!_connections.TryRemove(clientId, out var client))
        {
            return;
        }

        foreach (var timerKey in _channelTimers.Keys.Where(key => key.StartsWith(clientId + "-", StringComparison.Ordinal)).ToArray())
        {
            if (_channelTimers.TryRemove(timerKey, out var timerCts))
            {
                timerCts.Cancel();
                timerCts.Dispose();
            }
        }

        string? otherId = null;
        if (_pairs.TryRemove(clientId, out var appId))
        {
            otherId = appId;
            _reversePairs.TryRemove(appId, out _);
        }
        else if (_reversePairs.TryRemove(clientId, out var webId))
        {
            otherId = webId;
            _pairs.TryRemove(webId, out _);
        }

        if (notifyPair && !string.IsNullOrWhiteSpace(otherId) && _connections.TryGetValue(otherId, out var other))
        {
            await SendJsonAsync(other, new
            {
                type = "break",
                clientId,
                targetId = otherId,
                message = "209"
            }, CancellationToken.None);
        }

        try
        {
            client.Socket.Dispose();
        }
        catch
        {
        }

        _service.NotifyServerStateChanged();
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatIntervalMs, cancellationToken);
                foreach (var client in _connections.Values)
                {
                    if (client.Socket.State != WebSocketState.Open)
                    {
                        continue;
                    }

                    await SendJsonAsync(client, new
                    {
                        type = "heartbeat",
                        clientId = client.ClientId,
                        targetId = GetPair(client.ClientId) ?? string.Empty,
                        message = "200"
                    }, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Heartbeat loop failed: {ex.Message}");
            }
        }
    }

    private string? GetPair(string clientId)
    {
        return _pairs.TryGetValue(clientId, out var right)
            ? right
            : _reversePairs.TryGetValue(clientId, out var left) ? left : null;
    }

    private static async Task<byte[]> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                return Array.Empty<byte>();
            }

            ms.Write(buffer, 0, read);
            var bytes = ms.ToArray();
            if (bytes.Length >= 4)
            {
                for (var i = 3; i < bytes.Length; i++)
                {
                    if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                    {
                        return bytes;
                    }
                }
            }

            if (ms.Length > 16 * 1024)
            {
                return Array.Empty<byte>();
            }
        }
    }

    private static async Task WriteHttpResponseAsync(NetworkStream stream, string status, string contentType, byte[] body, CancellationToken cancellationToken)
    {
        var header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private static Task WriteJsonOkAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        return WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes("""{"ok":true}"""), cancellationToken);
    }

    private static string ComputeWebSocketAccept(string key)
    {
        var bytes = Encoding.ASCII.GetBytes(key + WebSocketGuid);
        return Convert.ToBase64String(SHA1.HashData(bytes));
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        value = element.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static async Task SendJsonAsync(ConnectedClient client, object payload, CancellationToken cancellationToken)
    {
        if (client.Socket.State != WebSocketState.Open)
        {
            return;
        }

        var raw = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(raw);
        await client.SendLock.WaitAsync(cancellationToken);
        try
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        catch
        {
        }
        finally
        {
            client.SendLock.Release();
        }
    }

    private sealed class ConnectedClient
    {
        public ConnectedClient(string clientId, WebSocket socket)
        {
            ClientId = clientId;
            Socket = socket;
        }

        public string ClientId { get; }

        public WebSocket Socket { get; }

        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public DateTimeOffset LastHeartbeatUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class HttpRequestInfo
    {
        public string Method { get; private init; } = "GET";

        public string Path { get; private init; } = "/";

        public Dictionary<string, string> Headers { get; private init; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsWebSocketRequest =>
            Headers.TryGetValue("Connection", out var connectionValue)
            && connectionValue.Contains("Upgrade", StringComparison.OrdinalIgnoreCase)
            && Headers.TryGetValue("Upgrade", out var upgradeValue)
            && upgradeValue.Equals("websocket", StringComparison.OrdinalIgnoreCase);

        public static HttpRequestInfo? Parse(string raw)
        {
            var lines = raw.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            {
                return null;
            }

            var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestLine.Length < 2)
            {
                return null;
            }

            var info = new HttpRequestInfo
            {
                Method = requestLine[0],
                Path = requestLine[1]
            };

            for (var index = 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                info.Headers[key] = value;
            }

            return info;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var trimmed = query.TrimStart('?');
        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }
}
