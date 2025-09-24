using System.Text.Json;
using NektoMe_MITM_text;

public class Program
{
    public static async Task Main()
    {
        var manager = new NektoChatManager();

        manager.AddMember(
            "f1e9598662273d2dcb5088169e9a58bd8a52ed96b543d72192b3097cd7939bcf",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            "M",
            "F",
            new[] { 0, 17 },
            new[] { new[] { 0, 17 } }
        );

        manager.AddMember(
            "34e1248bf81d54c0d6eb201e0eeb36a0f2282dc2a60d902bfc420c5870793e63",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            "F",
            "M",
            new[] { 0, 17 },
            new[] { new[] { 0, 17 } }
        );

        await manager.StartAsync();
    }
}
