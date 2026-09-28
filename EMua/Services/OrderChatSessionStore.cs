using System.Collections.Concurrent;

namespace EMua.Services;

/// <summary>
/// Phiên chat đổi trả chỉ tồn tại trong RAM.
/// Không ghi tin nhắn vào PostgreSQL/EF. Khi khách kết thúc phiên hoặc phiên hết hạn,
/// toàn bộ nội dung chat sẽ biến mất.
/// </summary>
public static class OrderChatSessionStore
{
    private static readonly ConcurrentDictionary<int, ChatSession> Sessions = new();
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    public static ChatSession? Start(int orderId, int customerId)
    {
        Cleanup();
        var session = new ChatSession(orderId, customerId);
        Sessions[orderId] = session;
        return session;
    }

    public static bool End(int orderId, int customerId)
    {
        if (!Sessions.TryGetValue(orderId, out var session) || session.CustomerId != customerId)
            return false;

        return Sessions.TryRemove(orderId, out _);
    }

    public static bool EndByStaff(int orderId)
        => Sessions.TryRemove(orderId, out _);

    public static bool TryGet(int orderId, out ChatSession? session)
    {
        Cleanup();
        return Sessions.TryGetValue(orderId, out session);
    }

    public static void Touch(ChatSession session)
        => session.LastActivity = DateTime.UtcNow;

    public static void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var item in Sessions)
        {
            if (now - item.Value.LastActivity > IdleTimeout)
                Sessions.TryRemove(item.Key, out _);
        }
    }
}

public sealed class ChatSession
{
    private readonly object _sync = new();
    private readonly List<OrderChatMessage> _messages = new();

    public ChatSession(int orderId, int customerId)
    {
        OrderId = orderId;
        CustomerId = customerId;
        LastActivity = DateTime.UtcNow;
    }

    public int OrderId { get; }
    public int CustomerId { get; }
    public DateTime LastActivity { get; set; }

    public void AddMessage(string senderName, string message, bool isStaff)
    {
        lock (_sync)
        {
            _messages.Add(new OrderChatMessage
            {
                SenderName = senderName,
                Message = message,
                IsStaff = isStaff,
                Time = DateTime.Now.ToString("HH:mm")
            });
        }
    }

    public IReadOnlyList<OrderChatMessage> GetMessages()
    {
        lock (_sync)
        {
            return _messages.ToList();
        }
    }
}

public sealed class OrderChatMessage
{
    public string SenderName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsStaff { get; set; }
    public string Time { get; set; } = string.Empty;
}
