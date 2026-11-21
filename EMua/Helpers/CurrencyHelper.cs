using System;
using System.Globalization;

namespace EMua.Helpers
{
    public static class CurrencyHelper
    {
        public static readonly CultureInfo ViCulture = new CultureInfo("vi-VN")
        {
            NumberFormat =
            {
                NumberGroupSeparator = ".",
                NumberDecimalSeparator = ",",
                CurrencyGroupSeparator = ".",
                CurrencyDecimalSeparator = ",",
                CurrencySymbol = "₫"
            }
        };

        public static string FormatVnd(decimal? amount)
        {
            if (!amount.HasValue) return "0 ₫";
            return amount.Value.ToString("#,##0", ViCulture) + " ₫";
        }

        public static string FormatVnd(double? amount)
        {
            if (!amount.HasValue) return "0 ₫";
            return amount.Value.ToString("#,##0", ViCulture) + " ₫";
        }

        public static string FormatVnd(long? amount)
        {
            if (!amount.HasValue) return "0 ₫";
            return amount.Value.ToString("#,##0", ViCulture) + " ₫";
        }

        public static string FormatVnd(int? amount)
        {
            if (!amount.HasValue) return "0 ₫";
            return amount.Value.ToString("#,##0", ViCulture) + " ₫";
        }

        public static string ToVnd(this decimal amount) => FormatVnd(amount);
        public static string ToVnd(this decimal? amount) => FormatVnd(amount);
        public static string ToVnd(this double amount) => FormatVnd(amount);
        public static string ToVnd(this double? amount) => FormatVnd(amount);
        public static string ToVnd(this long amount) => FormatVnd(amount);
        public static string ToVnd(this long? amount) => FormatVnd(amount);
        public static string ToVnd(this int amount) => FormatVnd(amount);
        public static string ToVnd(this int? amount) => FormatVnd(amount);
    }
}
