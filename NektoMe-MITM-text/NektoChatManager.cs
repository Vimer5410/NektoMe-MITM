using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NektoMe_MITM_text;

public class NektoChatManager
{
    private readonly List<NektoClient> _members = new();
    private readonly Dictionary<NektoClient, List<string>> _messagesBuffer = new();
    private readonly ILogger<NektoChatManager> _logger;

    public NektoChatManager()
    {
        _logger = LoggerFactory
            .Create(builder => builder.AddConsole())
            .CreateLogger<NektoChatManager>();
    }

    public void AddMember(
        string token,
        string userAgent,
        string sex = null,
        string wishSex = null,
        int[] age = null,
        int[][] wishAge = null,
        bool? role = null,
        bool? adult = null,
        string wishRole = null
    )
    {
        var client = new NektoClient(
            token,
            userAgent,
            this,
            sex,
            wishSex,
            age,
            wishAge,
            role,
            adult,
            wishRole
        );
        _members.Add(client);
        _messagesBuffer[client] = new List<string>();
    }

    public async Task OnTypingAsync(JsonElement data, NektoClient client)
    {
        foreach (
            var member in _members.Where(m =>
                m.Id != client.Id && !string.IsNullOrEmpty(m.DialogId)
            )
        )
        {
            await member.EmitAsync(
                "action",
                new
                {
                    action = "dialog.setTyping",
                    dialogId = member.DialogId,
                    voice = data.TryGetProperty("voice", out var v) ? v.ToString() : null,
                    typing = data.TryGetProperty("typing", out var t) ? t.GetBoolean() : false,
                }
            );
        }
    }

    public async Task OnAuthAsync(JsonElement data, NektoClient client)
    {
        if (!string.IsNullOrEmpty(client.DialogId))
        {
            await client.EmitAsync(
                "action",
                new { action = "anon.leaveDialog", dialogId = client.DialogId }
            );
            return;
        }

        Console.WriteLine($"[{client.Token[..10]}] Ищу собеседника");
        await client.SearchAsync();
    }

    public async Task OnMessageAsync(JsonElement data, NektoClient client)
    {
        var lastMessageId = data.TryGetProperty("id", out var id) ? GetStringValue(id) : null;
        await client.EmitAsync(
            "action",
            new
            {
                action = "anon.readMessages",
                dialogId = client.DialogId,
                lastMessageId,
            }
        );

        var message = data.TryGetProperty("message", out var m) ? GetStringValue(m) : "";
        var senderId = data.TryGetProperty("senderId", out var s) ? GetStringValue(s) : "";

        if (client.Id == senderId)
            return;

        Console.WriteLine($"[{client.Token[..10]}]: {message}");
        _messagesBuffer[client].Add(message);

        foreach (
            var member in _members.Where(m =>
                m.Id != client.Id && !string.IsNullOrEmpty(m.DialogId)
            )
        )
        {
            await member.EmitAsync(
                "action",
                new
                {
                    action = "anon.message",
                    dialogId = member.DialogId,
                    randomId = Guid.NewGuid().ToString("N")[..16],
                    message,
                    fileId = (string)null,
                }
            );
        }
    }

    public async Task OnDialogOpenedAsync(JsonElement data, NektoClient client)
    {
        Console.WriteLine($"[{client.Token[..10]}] Нашел собеседника!");

        foreach (var (member, messages) in _messagesBuffer.Where(x => x.Key != client))
        {
            foreach (var msg in messages)
            {
                await member.EmitAsync(
                    "action",
                    new
                    {
                        action = "anon.message",
                        dialogId = member.DialogId,
                        randomId = Guid.NewGuid().ToString("N")[..16],
                        message = msg,
                        fileId = (string)null,
                    }
                );
            }
        }
    }

    public async Task OnDialogClosedAsync(JsonElement data, NektoClient client)
    {
        Console.WriteLine($"[{client.Token[..10]}] Закрыл диалог.");
        _messagesBuffer[client].Clear();

        foreach (
            var member in _members.Where(m =>
                m.Id != client.Id && !string.IsNullOrEmpty(m.DialogId)
            )
        )
        {
            _messagesBuffer[member].Clear();
            await member.EmitAsync(
                "action",
                new { action = "anon.leaveDialog", dialogId = member.DialogId }
            );
        }

        await client.SearchAsync();
    }

    public async Task StartAsync()
    {
        await Task.WhenAll(_members.Select(m => m.ConnectAsync()));
        await Task.WhenAll(_members.Select(m => m.WaitAsync()));
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
