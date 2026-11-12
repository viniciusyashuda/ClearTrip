using System.ComponentModel.DataAnnotations;
using ClearTrip.Models;
using ClearTrip.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClearTrip.Tests;

public class WeatherServiceTests
{
    [Fact]
    public async Task GetForecastAsync_WithValidCityAndDateRange_ReturnsWeatherForecast()
    {
        using var httpClient = new HttpClient();
        var service = new OpenMeteoWeatherService(httpClient);
        var start = DateOnly.FromDateTime(DateTime.Today);
        var end = start.AddDays(2);

        var result = await service.GetForecastAsync("Curitiba", start, end);

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
        var start = DateOnly.FromDateTime(DateTime.Today);
        var end = start.AddDays(2);

        var result = await service.GetForecastAsync("InvalidCityNameXYZ999Unknown", start, end);

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
    public void TripViewModel_WithSameStartAndEndDate_FailsValidation()
    {
        var model = new TripViewModel
        {
            Destination = "Florianopolis",
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = DateOnly.FromDateTime(DateTime.Today)
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
            ["Gemini:ApiKey"] = "YOUR_GEMINI_API_KEY"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        using var httpClient = new HttpClient();
        var weatherService = new OpenMeteoWeatherService(httpClient);
        var geminiService = new GeminiService(httpClient, weatherService, configuration, overrideApiKey: "");

        var start = DateOnly.FromDateTime(DateTime.Today);
        var end = start.AddDays(2);
        var result = await geminiService.GenerateTripPlanAsync("Gramado", start, end, "Cultural e Gastronomico");

        Assert.Contains("Gemini API key is not configured", result);
    }

    [Fact]
    public async Task GenerateTripPlanAsync_HappyPath_ReturnsGeneratedItinerary()
    {
        var configuration = new ConfigurationBuilder().Build();
        var apiKey = GeminiService.ResolveApiKey(configuration);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var mockHandler = new MockHttpMessageHandler(request =>
            {
                var responseJson = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Dia 1: Chegada em Gramado e passeio pelo centro.\\nDia 2: Lago Negro e gastronomia local.\\nChecklist de mala: Roupas confortaveis.\"}]}}]}";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
                };
            });
            using var mockClient = new HttpClient(mockHandler);
            var mockWeather = new OpenMeteoWeatherService(mockClient);
            var mockService = new GeminiService(mockClient, mockWeather, configuration, overrideApiKey: "test-api-key");

            var startMock = DateOnly.FromDateTime(DateTime.Today);
            var endMock = startMock.AddDays(2);
            var mockResult = await mockService.GenerateTripPlanAsync("Gramado", startMock, endMock, "Cultural e Gastronomico");

            Assert.NotNull(mockResult);
            Assert.Contains("Gramado", mockResult);
            return;
        }

        using var httpClient = new HttpClient();
        var weatherService = new OpenMeteoWeatherService(httpClient);
        var geminiService = new GeminiService(httpClient, weatherService, configuration);
        var start = DateOnly.FromDateTime(DateTime.Today);
        var end = start.AddDays(2);

        var result = await geminiService.GenerateTripPlanAsync("Gramado", start, end, "Cultural e Gastronomico");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("Gemini API key is not configured", result);
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

internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}
