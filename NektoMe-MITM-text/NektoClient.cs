namespace NektoMe_MITM_text;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SocketIOClient;
using SocketIOClient.Transport;

public class NektoClient
{
    private readonly SocketIOClient.SocketIO _client;
    private readonly NektoChatManager _manager;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, object> _searchParams;

    public string Token { get; }
    public string UserAgent { get; }
    public string Id { get; set; }
    public string DialogId { get; set; }

    public NektoClient(
        string token,
        string userAgent,
        NektoChatManager manager,
        string sex,
        string wishSex,
        int[] age,
        int[][] wishAge,
        bool? role,
        bool? adult,
        string wishRole
    )
    {
        Token = token;
        UserAgent = userAgent;
        _manager = manager;

        _searchParams = new Dictionary<string, object>
        {
            ["wishAge"] = wishAge,
            ["myAge"] = age,
            ["mySex"] = sex,
            ["wishSex"] = wishSex,
            ["adult"] = adult,
            ["role"] = role,
        };

        if (role == true)
        {
            _searchParams["myAge"] = wishRole == "suggest" ? new[] { 30, 40 } : new[] { 10, 20 };
        }

        _client = new SocketIOClient.SocketIO(
            "wss://im.nekto.me",
            new SocketIOOptions
            {
                Transport = TransportProtocol.WebSocket,
                ExtraHeaders = new Dictionary<string, string> { ["User-Agent"] = UserAgent },
            }
        );

        _client.OnConnected += async (s, e) => await OnConnected();
        _client.OnDisconnected += async (s, r) => await OnDisconnected(r);
        _client.On("notice", OnNotice);
    }

    private async Task OnConnected()
    {
        Console.WriteLine($"[{Token[..10]}] Connected!");
        await _client.EmitAsync(
            "action",
            new
            {
                action = "auth.sendToken",
                token = Token,
                locale = "ru",
                t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                timeZone = "Europe/Kiev",
                version = 12,
            }
        );
    }

    private async Task OnDisconnected(string reason)
    {
        Console.WriteLine($"[{Token[..10]}] Disconnected: {reason}");
        Id = null;
        DialogId = null;
    }

    private async void OnNotice(SocketIOResponse response)
    {
        try
        {
            JsonElement data;
            try
            {
                data = response.GetValue<JsonElement[]>().FirstOrDefault();
            }
            catch
            {
                data = response.GetValue<JsonElement>();
            }

            var notice = data.TryGetProperty("notice", out var n) ? GetStringValue(n) : null;
            var hasData = data.TryGetProperty("data", out var dataElement);

            switch (notice)
            {
                case "auth.successToken" when hasData:
                    await HandleAuthSuccess(dataElement);
                    break;
                case "messages.new" when hasData:
                    await _manager.OnMessageAsync(dataElement, this);
                    break;
                case "dialog.opened" when hasData:
                    DialogId = dataElement.TryGetProperty("id", out var i)
                        ? GetStringValue(i)
                        : null;
                    await _manager.OnDialogOpenedAsync(dataElement, this);
                    break;
                case "dialog.closed" when hasData:
                    DialogId = null;
                    await _manager.OnDialogClosedAsync(dataElement, this);
                    break;
                case "dialog.typing" when hasData:
                    await _manager.OnTypingAsync(dataElement, this);
                    break;
                case "search.out":
                    Console.WriteLine($"[{Token[..10]}] Search completed");
                    break;
                case "error.code":
                    Console.WriteLine($"[{Token[..10]}] Error: {dataElement}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Token[..10]}] Error: {ex.Message}");
        }
    }

    private async Task HandleAuthSuccess(JsonElement data)
    {
        Id = data.TryGetProperty("id", out var i) ? GetStringValue(i) : null;

        if (
            data.TryGetProperty("statusInfo", out var si)
            && si.TryGetProperty("anonDialogId", out var di)
        )
        {
            DialogId = GetStringValue(di);
        }

        await _client.EmitAsync(
            "action",
            new
            {
                type = "web-agent",
                data = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            $"{Token}{Id}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                        )
                    )
                )[..16],
            }
        );

        await _manager.OnAuthAsync(data, this);
    }

    public async Task SearchAsync()
    {
        var payload = new Dictionary<string, object> { ["action"] = "search.run" };
        foreach (var p in _searchParams.Where(p => p.Value != null))
            payload[p.Key] = p.Value;

        await _client.EmitAsync("action", payload);
    }

    public async Task EmitAsync(string eventName, object data) =>
        await _client.EmitAsync(eventName, data);

    public async Task ConnectAsync() => await _client.ConnectAsync();

    public async Task WaitAsync()
    {
        try
        {
            await Task.Delay(-1, _cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    public void Disconnect()
    {
        _cts.Cancel();
        _client.DisconnectAsync();
    }

    private static string GetStringValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.ToString(),
        };
}
