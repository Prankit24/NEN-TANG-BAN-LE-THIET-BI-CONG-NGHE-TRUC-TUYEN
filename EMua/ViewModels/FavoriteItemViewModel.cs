using System;

namespace EMua.ViewModels;

public class FavoriteItemViewModel
{
    public int FavoriteId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? BrandName { get; set; }
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public decimal? OriginalPrice { get; set; }
    public bool InStock { get; set; }
    public DateTime AddedAt { get; set; }
}
