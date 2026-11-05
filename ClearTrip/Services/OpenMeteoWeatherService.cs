using System.Globalization;
using System.Text.Json;
using ClearTrip.Dtos;

namespace ClearTrip.Services;

public class OpenMeteoWeatherService : IWeatherService
{
    private readonly HttpClient _httpClient;

    public OpenMeteoWeatherService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<string> GetForecastAsync(string city, int days)
    {
        var start = DateOnly.FromDateTime(DateTime.Today);
        var end = start.AddDays(Math.Max(1, days) - 1);
        return GetForecastAsync(city, start, end);
    }

    public async Task<string> GetForecastAsync(string city, DateOnly startDate, DateOnly endDate)
    {
        var geocodingUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=pt&format=json";
        var geocodingResponse = await _httpClient.GetAsync(geocodingUrl);

        if (!geocodingResponse.IsSuccessStatusCode)
        {
            return "Location service could not be queried.";
        }

        var geocodingJson = await geocodingResponse.Content.ReadAsStringAsync();
        var geocodingData = JsonSerializer.Deserialize<GeocodingResult>(geocodingJson);

        if (geocodingData?.Results == null || geocodingData.Results.Count == 0)
        {
            return $"Location '{city}' not found.";
        }

        var location = geocodingData.Results[0];
        var forecastUrl = $"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude.ToString(CultureInfo.InvariantCulture)}&longitude={location.Longitude.ToString(CultureInfo.InvariantCulture)}&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max&start_date={startDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}&timezone=auto";

        var forecastResponse = await _httpClient.GetAsync(forecastUrl);
        if (!forecastResponse.IsSuccessStatusCode)
        {
            return "Weather forecast could not be retrieved.";
        }

        var forecastJson = await forecastResponse.Content.ReadAsStringAsync();
        var forecastData = JsonSerializer.Deserialize<ForecastResult>(forecastJson);

        if (forecastData?.Daily == null)
        {
            return "Weather data currently unavailable.";
        }

        var summary = new List<string>();
        for (var i = 0; i < forecastData.Daily.Time.Count; i++)
        {
            var date = forecastData.Daily.Time[i];
            var min = forecastData.Daily.TemperatureMin[i];
            var max = forecastData.Daily.TemperatureMax[i];
            var rainProbability = forecastData.Daily.PrecipitationProbability.Count > i ? forecastData.Daily.PrecipitationProbability[i] : 0;
            summary.Add($"Date: {date} | Min: {min}C, Max: {max}C | Rain Probability: {rainProbability}%");
        }

        return string.Join("; ", summary);
    }
}
