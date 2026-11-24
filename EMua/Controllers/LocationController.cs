using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace EMua.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LocationController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LocationController> _logger;

        private const string NominatimUserAgent = "EMUA-TechStore-StudentProject/1.0 (contact@emua.id.vn)";

        public LocationController(IHttpClientFactory httpClientFactory, ILogger<LocationController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string? q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            {
                return Ok(Array.Empty<object>());
            }

            var cleanQuery = q.Trim();

            // 1. Thử OpenStreetMap Nominatim trước
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(4);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(NominatimUserAgent);
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("vi");

                var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(cleanQuery)}&format=jsonv2&limit=6&addressdetails=1&countrycodes=vn&accept-language=vi";
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        var results = new List<object>();
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            var lat = item.TryGetProperty("lat", out var latProp) ? latProp.GetString() : "";
                            var lon = item.TryGetProperty("lon", out var lonProp) ? lonProp.GetString() : "";
                            var displayName = item.TryGetProperty("display_name", out var dnProp) ? dnProp.GetString() : "";

                            string formatted = "";
                            if (item.TryGetProperty("address", out var addr))
                            {
                                formatted = FormatNominatimAddress(addr);
                            }

                            results.Add(new
                            {
                                lat,
                                lon,
                                display_name = !string.IsNullOrEmpty(formatted) ? formatted : displayName,
                                raw = displayName
                            });
                        }
                        return Ok(results);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nominatim search failed or timed out. Falling back to Photon Komoot.");
            }

            // 2. Fallback sang Photon Komoot API
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(4);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(NominatimUserAgent);

                var photonUrl = $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(cleanQuery)}&limit=6&lang=en";
                var response = await client.GetAsync(photonUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
                    {
                        var results = new List<object>();
                        foreach (var feat in features.EnumerateArray())
                        {
                            string lat = "";
                            string lon = "";
                            if (feat.TryGetProperty("geometry", out var geom) && geom.TryGetProperty("coordinates", out var coords) && coords.GetArrayLength() >= 2)
                            {
                                lon = coords[0].GetDouble().ToString(CultureInfo.InvariantCulture);
                                lat = coords[1].GetDouble().ToString(CultureInfo.InvariantCulture);
                            }

                            string formatted = "";
                            if (feat.TryGetProperty("properties", out var props))
                            {
                                formatted = FormatPhotonProperties(props);
                            }

                            if (!string.IsNullOrEmpty(formatted))
                            {
                                results.Add(new
                                {
                                    lat,
                                    lon,
                                    display_name = formatted,
                                    raw = formatted
                                });
                            }
                        }
                        if (results.Any())
                        {
                            return Ok(results);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Photon search also failed");
            }

            return Ok(Array.Empty<object>());
        }

        [HttpGet("reverse")]
        public async Task<IActionResult> Reverse([FromQuery] double? lat, [FromQuery] double? lon)
        {
            if (!lat.HasValue || !lon.HasValue)
            {
                return BadRequest(new { message = "Latitude and Longitude are required." });
            }

            var latStr = lat.Value.ToString(CultureInfo.InvariantCulture);
            var lonStr = lon.Value.ToString(CultureInfo.InvariantCulture);

            // 1. Thử Nominatim OpenStreetMap trước
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(4);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(NominatimUserAgent);
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("vi");

                var url = $"https://nominatim.openstreetmap.org/reverse?lat={latStr}&lon={lonStr}&format=jsonv2&zoom=18&addressdetails=1&accept-language=vi";
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;

                    string formatted = "";
                    if (root.TryGetProperty("address", out var addr))
                    {
                        formatted = FormatNominatimAddress(addr);
                    }

                    var raw = root.TryGetProperty("display_name", out var dn) ? dn.GetString() : "";

                    return Ok(new
                    {
                        display_name = !string.IsNullOrEmpty(formatted) ? formatted : raw,
                        address = addr.Clone(),
                        lat = lat.Value,
                        lon = lon.Value
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nominatim reverse failed or timed out. Falling back to Photon Komoot.");
            }

            // 2. Fallback sang Photon Komoot API (reverse)
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(4);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(NominatimUserAgent);

                var photonUrl = $"https://photon.komoot.io/reverse?lat={latStr}&lon={lonStr}";
                var response = await client.GetAsync(photonUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array && features.GetArrayLength() > 0)
                    {
                        var first = features[0];
                        if (first.TryGetProperty("properties", out var props))
                        {
                            var formatted = FormatPhotonProperties(props);
                            if (!string.IsNullOrEmpty(formatted))
                            {
                                return Ok(new
                                {
                                    display_name = formatted,
                                    lat = lat.Value,
                                    lon = lon.Value
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Photon reverse failed");
            }

            // Fallback an toàn nếu cả 2 API ngoại tuyến
            return Ok(new
            {
                display_name = $"Khu vực ghim ({lat.Value:F4}, {lon.Value:F4}) - Đà Nẵng",
                lat = lat.Value,
                lon = lon.Value
            });
        }

        private static string FormatNominatimAddress(JsonElement a)
        {
            var parts = new List<string>();

            // Số nhà & tên đường
            var houseNum = GetProp(a, "house_number");
            var road = GetProp(a, "road", "pedestrian", "residential", "street", "footway");

            if (!string.IsNullOrEmpty(houseNum) && !string.IsNullOrEmpty(road))
            {
                parts.Add($"{houseNum} {road}");
            }
            else if (!string.IsNullOrEmpty(road))
            {
                parts.Add(road);
            }
            else if (!string.IsNullOrEmpty(houseNum))
            {
                parts.Add(houseNum);
            }

            // Phường / Xã
            var ward = GetProp(a, "suburb", "quarter", "neighbourhood", "village", "hamlet");
            if (!string.IsNullOrEmpty(ward) && !parts.Contains(ward))
            {
                parts.Add(ward);
            }

            // Quận / Huyện
            var district = GetProp(a, "city_district", "district", "county", "town");
            if (!string.IsNullOrEmpty(district) && !parts.Contains(district))
            {
                parts.Add(district);
            }

            // Tỉnh / Thành phố
            var city = GetProp(a, "city", "state", "province");
            if (!string.IsNullOrEmpty(city) && !parts.Contains(city))
            {
                parts.Add(city);
            }

            return string.Join(", ", parts.Distinct());
        }

        private static string FormatPhotonProperties(JsonElement p)
        {
            var parts = new List<string>();

            var name = GetProp(p, "name");
            var street = GetProp(p, "street");
            var houseNumber = GetProp(p, "housenumber");
            var district = GetProp(p, "district", "suburb");
            var city = GetProp(p, "city");
            var state = GetProp(p, "state");

            if (!string.IsNullOrEmpty(houseNumber) && !string.IsNullOrEmpty(street))
            {
                parts.Add($"{houseNumber} {street}");
            }
            else if (!string.IsNullOrEmpty(street))
            {
                parts.Add(street);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                parts.Add(name);
            }

            if (!string.IsNullOrEmpty(district) && !parts.Contains(district))
            {
                parts.Add(district);
            }

            if (!string.IsNullOrEmpty(city) && !parts.Contains(city))
            {
                parts.Add(city);
            }
            else if (!string.IsNullOrEmpty(state) && !parts.Contains(state))
            {
                parts.Add(state);
            }

            return string.Join(", ", parts.Distinct());
        }

        private static string GetProp(JsonElement el, params string[] propertyNames)
        {
            foreach (var prop in propertyNames)
            {
                if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
                {
                    var text = val.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }
            return "";
        }
    }
}
