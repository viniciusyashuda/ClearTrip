using System.Text.Json.Serialization;

namespace ClearTrip.Dtos;

public class GeocodingResult
{
    [JsonPropertyName("results")]
    public List<GeocodingLocation>? Results { get; set; }
}

public class GeocodingLocation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}

public class ForecastResult
{
    [JsonPropertyName("daily")]
    public ForecastDaily? Daily { get; set; }
}

public class ForecastDaily
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; } = new();

    [JsonPropertyName("temperature_2m_max")]
    public List<double> TemperatureMax { get; set; } = new();

    [JsonPropertyName("temperature_2m_min")]
    public List<double> TemperatureMin { get; set; } = new();

    [JsonPropertyName("precipitation_probability_max")]
    public List<int?> PrecipitationProbability { get; set; } = new();
}
