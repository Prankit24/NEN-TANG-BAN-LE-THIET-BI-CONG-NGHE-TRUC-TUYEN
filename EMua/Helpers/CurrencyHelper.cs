using System.Globalization;

namespace EMua.Helpers;

public static class CurrencyHelper
{
    private static readonly CultureInfo ViVn = CultureInfo.GetCultureInfo("vi-VN");

    public static string FormatVnd(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("#,##0", ViVn) + " đ";

    public static string FormatVnd(decimal? amount) =>
        amount.HasValue ? FormatVnd(amount.Value) : "0 đ";
}
