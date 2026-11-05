using System.ComponentModel.DataAnnotations;
using ClearTrip.Models;
using ClearTrip.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClearTrip.Tests;

public class WeatherServiceTests
{
    [Fact]
    public async Task GetForecastAsync_WithValidCityAndDays_ReturnsWeatherForecast()
    {
        using var httpClient = new HttpClient();
        var service = new OpenMeteoWeatherService(httpClient);

        var result = await service.GetForecastAsync("Curitiba", 3);

        Assert.NotNull(result);
        Assert.Contains("Date:", result);
        Assert.Contains("Min:", result);
        Assert.Contains("Max:", result);
    }

    [Fact]
    public async Task GetForecastAsync_WithDateRange_ReturnsWeatherForecast()
    {
        using var httpClient = new HttpClient();
        var service = new OpenMeteoWeatherService(httpClient);
        var start = DateOnly.FromDateTime(DateTime.Today);
        var end = start.AddDays(2);

        var result = await service.GetForecastAsync("Curitiba", start, end);

        Assert.NotNull(result);
        Assert.Contains("Date:", result);
    }

    [Fact]
    public async Task GetForecastAsync_WithInvalidCity_ReturnsLocationNotFoundMessage()
    {
        using var httpClient = new HttpClient();
        var service = new OpenMeteoWeatherService(httpClient);

        var result = await service.GetForecastAsync("InvalidCityNameXYZ999Unknown", 2);

        Assert.NotNull(result);
        Assert.Contains("not found", result);
    }
}

public class TripViewModelValidationTests
{
    [Fact]
    public void TripViewModel_WithValidDates_PassesValidation()
    {
        var model = new TripViewModel
        {
            Destination = "Gramado",
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = DateOnly.FromDateTime(DateTime.Today).AddDays(3),
            TravelStyle = "Romantico / Casal"
        };

        var validationResults = ValidateModel(model);

        Assert.Empty(validationResults);
    }

    [Fact]
    public void TripViewModel_WithEmptyDestination_FailsValidation()
    {
        var model = new TripViewModel
        {
            Destination = string.Empty,
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = DateOnly.FromDateTime(DateTime.Today).AddDays(2)
        };

        var validationResults = ValidateModel(model);

        Assert.Contains(validationResults, v => v.MemberNames.Contains("Destination"));
    }

    [Fact]
    public void TripViewModel_WithStartDateInPast_FailsValidation()
    {
        var model = new TripViewModel
        {
            Destination = "Florianopolis",
            StartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-1),
            EndDate = DateOnly.FromDateTime(DateTime.Today).AddDays(2)
        };

        var validationResults = ValidateModel(model);

        Assert.Contains(validationResults, v => v.MemberNames.Contains("StartDate"));
    }

    [Fact]
    public void TripViewModel_WithEndDateBeforeStartDate_FailsValidation()
    {
        var model = new TripViewModel
        {
            Destination = "Florianopolis",
            StartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(2),
            EndDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1)
        };

        var validationResults = ValidateModel(model);

        Assert.Contains(validationResults, v => v.MemberNames.Contains("EndDate"));
    }

    [Fact]
    public void TripViewModel_WithDateRangeExceeding7Days_FailsValidation()
    {
        var model = new TripViewModel
        {
            Destination = "Florianopolis",
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = DateOnly.FromDateTime(DateTime.Today).AddDays(8)
        };

        var validationResults = ValidateModel(model);

        Assert.Contains(validationResults, v => v.MemberNames.Contains("EndDate"));
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, context, validationResults, true);
        return validationResults;
    }
}

public class GeminiServiceTests
{
    [Fact]
    public async Task GenerateTripPlanAsync_WithDefaultOrEmptyApiKey_ReturnsConfigurationWarning()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["GeminiApiKey"] = "YOUR_GEMINI_API_KEY",
            ["Gemini:ApiKey"] = "YOUR_GEMINI_API_KEY"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        using var httpClient = new HttpClient();
        var weatherService = new OpenMeteoWeatherService(httpClient);
        var geminiService = new GeminiService(httpClient, weatherService, configuration, overrideApiKey: "");

        var result = await geminiService.GenerateTripPlanAsync("Gramado", 3, "Cultural e Gastronomico");

        Assert.Contains("Chave de API do Gemini nao configurada", result);
    }

    [Fact]
    public void ExtractDays_WithInteger_ReturnsValidNumber()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("{\"days\": 5}");
        var result = GeminiService.ExtractDays(node, 3);
        Assert.Equal(5, result);
    }

    [Fact]
    public void ExtractDays_WithStringNumber_ReturnsParsedNumber()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("{\"days\": \"4\"}");
        var result = GeminiService.ExtractDays(node, 3);
        Assert.Equal(4, result);
    }

    [Theory]
    [InlineData("{\"days\": 0}")]
    [InlineData("{\"days\": 10}")]
    [InlineData("{\"days\": \"invalid\"}")]
    [InlineData("{}")]
    public void ExtractDays_WithInvalidOrOutOfBoundsValue_ReturnsFallback(string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        var result = GeminiService.ExtractDays(node, 3);
        Assert.Equal(3, result);
    }

    [Fact]
    public void ExtractDays_WithNullNode_ReturnsFallback()
    {
        var result = GeminiService.ExtractDays(null, 3);
        Assert.Equal(3, result);
    }

    [Fact]
    public void ExtractCity_WithEnglishCityKey_ReturnsCity()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("{\"city\": \"Tokyo\"}");
        var result = GeminiService.ExtractCity(node, "London");
        Assert.Equal("Tokyo", result);
    }

    [Fact]
    public void ExtractCity_WithNullOrEmpty_ReturnsFallbackCity()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("{}");
        var result = GeminiService.ExtractCity(node, "London");
        Assert.Equal("London", result);
    }

    [Fact]
    public void ExtractDates_WithValidIsoDates_ReturnsParsedDates()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("{\"start_date\": \"2026-11-06\", \"end_date\": \"2026-11-09\"}");
        var fallbackStart = new DateOnly(2026, 11, 10);
        var fallbackEnd = new DateOnly(2026, 11, 12);

        var (start, end) = GeminiService.ExtractDates(node, fallbackStart, fallbackEnd);

        Assert.Equal(new DateOnly(2026, 11, 6), start);
        Assert.Equal(new DateOnly(2026, 11, 9), end);
    }

    [Fact]
    public void ExtractDates_WithNullNode_ReturnsFallbackDates()
    {
        var fallbackStart = new DateOnly(2026, 11, 10);
        var fallbackEnd = new DateOnly(2026, 11, 12);

        var (start, end) = GeminiService.ExtractDates(null, fallbackStart, fallbackEnd);

        Assert.Equal(fallbackStart, start);
        Assert.Equal(fallbackEnd, end);
    }
}
