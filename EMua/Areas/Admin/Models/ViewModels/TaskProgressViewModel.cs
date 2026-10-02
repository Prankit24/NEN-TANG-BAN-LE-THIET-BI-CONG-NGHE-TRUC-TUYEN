namespace EMua.Areas.Admin.Models.ViewModels;

public class AdminTaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public bool IsCompleted => CompletedCount >= TotalCount;
}

public class TaskProgressViewModel
{
    public List<AdminTaskItem> Tasks { get; set; } = new();
}