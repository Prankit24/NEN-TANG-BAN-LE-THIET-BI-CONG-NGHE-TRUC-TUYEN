namespace EMua.Services;

/// <summary>
/// Quản lý tập trung các Cache Key của hệ thống nhằm đảm bảo tính đồng nhất
/// giữa các Controller và cơ chế Cache Invalidation.
/// </summary>
public static class AppCacheKeys
{
    public const string Home = "EMUA_HOME_V3";
    public const string Categories = "EMUA_CATEGORIES_CACHE";
    public const string FeaturedProducts = "EMUA_HOME_FEATURED_PRODUCTS";
    public const string Brands = "EMUA_BRANDS_CACHE";
}
