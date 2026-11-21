using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EMua.Services;

public enum DeletionTargetType
{
    Brand,
    Category,
    Role,
    ProductImage
}

public class UndoDeletionItem
{
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public DeletionTargetType TargetType { get; set; }
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string? AdditionalData { get; set; } // Ví dụ ImageUrl, hoặc Json snapshot
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddSeconds(60);
    public string InitiatorUserId { get; set; } = string.Empty;
    public string InitiatorUserName { get; set; } = string.Empty;
    public bool IsRestored { get; set; }
    public bool IsCommitted { get; set; }
}

public interface IUndoDeletionManager
{
    UndoDeletionItem RegisterPendingDeletion(
        DeletionTargetType targetType, 
        int entityId, 
        string entityName, 
        string? additionalData, 
        ClaimsPrincipal user);

    UndoDeletionItem? GetPendingDeletion(string token);

    bool TryClaimUndo(string token, ClaimsPrincipal user, out UndoDeletionItem? item, out string? errorMessage);

    void MarkCommitted(string token);

    bool IsEntityPendingDeletion(DeletionTargetType targetType, int entityId);
}

public class UndoDeletionManager : IUndoDeletionManager
{
    private readonly ConcurrentDictionary<string, UndoDeletionItem> _items = new();

    public UndoDeletionItem RegisterPendingDeletion(
        DeletionTargetType targetType, 
        int entityId, 
        string entityName, 
        string? additionalData, 
        ClaimsPrincipal user)
    {
        CleanupExpired();

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var userName = user.Identity?.Name ?? "Admin";

        var item = new UndoDeletionItem
        {
            Token = Guid.NewGuid().ToString("N"),
            TargetType = targetType,
            EntityId = entityId,
            EntityName = entityName,
            AdditionalData = additionalData,
            InitiatorUserId = userId,
            InitiatorUserName = userName,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(60)
        };

        _items[item.Token] = item;
        return item;
    }

    public UndoDeletionItem? GetPendingDeletion(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (_items.TryGetValue(token, out var item))
        {
            if (DateTime.UtcNow <= item.ExpiresAtUtc && !item.IsRestored && !item.IsCommitted)
            {
                return item;
            }
        }
        return null;
    }

    public bool TryClaimUndo(string token, ClaimsPrincipal user, out UndoDeletionItem? item, out string? errorMessage)
    {
        item = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            errorMessage = "Mã xác thực hoàn tác không hợp lệ.";
            return false;
        }

        if (!_items.TryGetValue(token, out var foundItem))
        {
            errorMessage = "Thao tác hoàn tác không tồn tại hoặc đã hết hạn 60 giây.";
            return false;
        }

        lock (foundItem)
        {
            if (DateTime.UtcNow > foundItem.ExpiresAtUtc)
            {
                errorMessage = "Thời gian 60 giây để hoàn tác đã kết thúc. Dữ liệu không thể khôi phục tự động.";
                return false;
            }

            if (foundItem.IsRestored)
            {
                errorMessage = "Thao tác này đã được hoàn tác trước đó. Không thể hoàn tác hai lần.";
                return false;
            }

            if (foundItem.IsCommitted)
            {
                errorMessage = "Thao tác xóa đã được áp dụng vĩnh viễn.";
                return false;
            }

            // Kiểm tra quyền
            var currentUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            bool isPrivileged = user.IsInRole("Admin") || user.IsInRole("Quản trị viên") || user.IsInRole("QuanTriVien");

            if (!isPrivileged && currentUserId != foundItem.InitiatorUserId)
            {
                errorMessage = "Bạn không có quyền hoàn tác thao tác của người dùng khác.";
                return false;
            }

            foundItem.IsRestored = true;
            item = foundItem;
            return true;
        }
    }

    public void MarkCommitted(string token)
    {
        if (_items.TryGetValue(token, out var item))
        {
            item.IsCommitted = true;
        }
    }

    public bool IsEntityPendingDeletion(DeletionTargetType targetType, int entityId)
    {
        CleanupExpired();
        foreach (var kvp in _items)
        {
            var it = kvp.Value;
            if (it.TargetType == targetType && it.EntityId == entityId && !it.IsRestored && DateTime.UtcNow <= it.ExpiresAtUtc)
            {
                return true;
            }
        }
        return false;
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _items)
        {
            // Xóa khỏi bộ nhớ sau 15 phút để giải phóng dung lượng
            if (now > kvp.Value.ExpiresAtUtc.AddMinutes(15))
            {
                _items.TryRemove(kvp.Key, out _);
            }
        }
    }
}
