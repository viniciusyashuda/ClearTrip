using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ClearTrip.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly IWeatherService _weatherService;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _endpointUrl;

    public GeminiService(HttpClient httpClient, IWeatherService weatherService, IConfiguration configuration, string? overrideApiKey = null)
    {
        _httpClient = httpClient;
        _weatherService = weatherService;
        _apiKey = overrideApiKey ?? ResolveApiKey(configuration);
        _model = configuration["Gemini:Model"] ?? configuration["GeminiModel"] ?? "gemini-3.8-flash";
        _endpointUrl = configuration["Gemini:EndpointUrl"] ?? "https://generativelanguage.googleapis.com/v1beta/models";
    }

    public static string ResolveApiKey(IConfiguration configuration)
    {
        var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User);

        if (!string.IsNullOrWhiteSpace(envKey) && !envKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return envKey.Trim();
        }

        var configKey = configuration["Gemini:ApiKey"] ?? configuration["GeminiApiKey"];
        if (!string.IsNullOrWhiteSpace(configKey) && !configKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return configKey.Trim();
        }

        return string.Empty;
    }

    public Task<string> GenerateTripPlanAsync(string destination, int days, string travelStyle)
    {
        var start = DateOnly.FromDateTime(DateTime.Today);
        var end = start.AddDays(Math.Max(1, days) - 1);
        return GenerateTripPlanAsync(destination, start, end, travelStyle);
    }

    public async Task<string> GenerateTripPlanAsync(string destination, DateOnly startDate, DateOnly endDate, string travelStyle)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return "Chave de API do Gemini nao configurada. Configure a chave no arquivo appsettings.json ou defina a variavel de ambiente GEMINI_API_KEY.";
        }

        var candidateModels = GetCandidateModels();
        string? lastError = null;

        foreach (var model in candidateModels)
        {
            var (success, result) = await TryGenerateWithModelAsync(model, destination, startDate, endDate, travelStyle);
            if (success)
            {
                return result;
            }

            lastError = result;
            if (!IsHighDemandError(lastError))
            {
                return lastError;
            }
        }

        return lastError ?? "Nao foi possivel obter resposta do assistente.";
    }

    private async Task<(bool Success, string Text)> TryGenerateWithModelAsync(string model, string destination, DateOnly startDate, DateOnly endDate, string travelStyle)
    {
        var endpoint = $"{_endpointUrl.TrimEnd('/')}/{model}:generateContent?key={_apiKey}";
        var toolDeclaration = CreateToolDeclaration();
        var contents = CreateInitialContent(destination, startDate, endDate, travelStyle);

        var requestBody = new JsonObject
        {
            ["contents"] = contents,
            ["tools"] = new JsonArray { toolDeclaration }
        };

        var firstResponse = await PostGeminiAsync(endpoint, requestBody);
        var apiError = ExtractErrorMessage(firstResponse);
        if (!string.IsNullOrEmpty(apiError))
        {
            return (false, apiError);
        }

        var callNode = ExtractFunctionCall(firstResponse);
        if (callNode != null)
        {
            var functionName = callNode["name"]?.ToString();
            if (functionName is "get_weather_forecast" or "consultar_previsao_tempo")
            {
                var args = callNode["args"];
                var cityParam = ExtractCity(args, destination);
                var (startParam, endParam) = ExtractDates(args, startDate, endDate);

                var weatherData = await _weatherService.GetForecastAsync(cityParam, startParam, endParam);
                var modelResponse = firstResponse?["candidates"]?[0]?["content"];

                if (modelResponse != null)
                {
                    contents.Add(modelResponse.DeepClone());
                }

                contents.Add(CreateToolResponse(functionName, weatherData));

                var secondResponse = await PostGeminiAsync(endpoint, requestBody);
                var secondCallError = ExtractErrorMessage(secondResponse);
                if (!string.IsNullOrEmpty(secondCallError))
                {
                    return (false, secondCallError);
                }

                return (true, ExtractText(secondResponse));
            }
        }

        return (true, ExtractText(firstResponse));
    }

    private List<string> GetCandidateModels()
    {
        var list = new List<string> { _model };
        if (!list.Contains("gemini-3.5-flash"))
        {
            list.Add("gemini-3.5-flash");
        }
        if (!list.Contains("gemini-flash-lite-latest"))
        {
            list.Add("gemini-flash-lite-latest");
        }
        return list;
    }

    private static bool IsHighDemandError(string? error)
    {
        if (string.IsNullOrEmpty(error)) return false;
        return error.Contains("high demand", StringComparison.OrdinalIgnoreCase)
            || error.Contains("503")
            || error.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase);
    }

    public static string ExtractCity(JsonNode? args, string fallbackCity)
    {
        var city = args?["city"]?.ToString() ?? args?["cidade"]?.ToString();
        return !string.IsNullOrWhiteSpace(city) ? city : fallbackCity;
    }

    public static (DateOnly StartDate, DateOnly EndDate) ExtractDates(JsonNode? args, DateOnly fallbackStart, DateOnly fallbackEnd)
    {
        var startRaw = args?["start_date"]?.ToString() ?? args?["startDate"]?.ToString();
        var endRaw = args?["end_date"]?.ToString() ?? args?["endDate"]?.ToString();

        var start = DateOnly.TryParse(startRaw, CultureInfo.InvariantCulture, out var parsedStart) ? parsedStart : fallbackStart;
        var end = DateOnly.TryParse(endRaw, CultureInfo.InvariantCulture, out var parsedEnd) ? parsedEnd : fallbackEnd;

        if (end < start)
        {
            var fallbackDays = Math.Max(1, (fallbackEnd.DayNumber - fallbackStart.DayNumber) + 1);
            var days = ExtractDays(args, fallbackDays);
            end = start.AddDays(days - 1);
        }

        return (start, end);
    }

    public static int ExtractDays(JsonNode? args, int fallbackDays)
    {
        var daysNode = args?["days"] ?? args?["dias"];
        if (daysNode == null)
        {
            return fallbackDays;
        }

        if (int.TryParse(daysNode.ToString(), out var validDays) && validDays is >= 1 and <= 7)
        {
            return validDays;
        }

        return fallbackDays;
    }

    private static JsonObject CreateToolDeclaration()
    {
        return new JsonObject
        {
            ["function_declarations"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "get_weather_forecast",
                    ["description"] = "Gets the real weather forecast for the specified city between the start date and end date.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "OBJECT",
                        ["properties"] = new JsonObject
                        {
                            ["city"] = new JsonObject
                            {
                                ["type"] = "STRING",
                                ["description"] = "Destination city name"
                            },
                            ["start_date"] = new JsonObject
                            {
                                ["type"] = "STRING",
                                ["description"] = "Trip start date in YYYY-MM-DD format"
                            },
                            ["end_date"] = new JsonObject
                            {
                                ["type"] = "STRING",
                                ["description"] = "Trip end date in YYYY-MM-DD format"
                            }
                        },
                        ["required"] = new JsonArray { "city", "start_date", "end_date" }
                    }
                }
            }
        };
    }

    private static JsonArray CreateInitialContent(string destination, DateOnly startDate, DateOnly endDate, string travelStyle)
    {
        var days = (endDate.DayNumber - startDate.DayNumber) + 1;
        var userPrompt = $"Planeje uma viagem para {destination} no periodo de {startDate:dd/MM/yyyy} a {endDate:dd/MM/yyyy} ({days} dias) no estilo {travelStyle}. Consulte a previsao do tempo para estruturar o roteiro e o checklist da mala em portugues. Nao utilize emojis na resposta, mantendo formatacao puramente textual e profissional.";

        return new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = userPrompt }
                }
            }
        };
    }

    private static JsonObject CreateToolResponse(string functionName, string weatherData)
    {
        return new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray
            {
                new JsonObject
                {
                    ["functionResponse"] = new JsonObject
                    {
                        ["name"] = functionName,
                        ["response"] = new JsonObject
                        {
                            ["name"] = functionName,
                            ["content"] = new JsonObject
                            {
                                ["result"] = weatherData
                            }
                        }
                    }
                }
            }
        };
    }

    private async Task<JsonObject?> PostGeminiAsync(string endpoint, JsonObject body)
    {
        var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, content);
        var json = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(json) as JsonObject;
    }

    private static string? ExtractErrorMessage(JsonObject? response)
    {
        var errorMessage = response?["error"]?["message"]?.ToString();
        if (!string.IsNullOrEmpty(errorMessage))
        {
            return $"Erro da API Gemini: {errorMessage}";
        }
        return null;
    }

    private static JsonNode? ExtractFunctionCall(JsonObject? response)
    {
        var parts = response?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        if (parts == null) return null;

        foreach (var part in parts)
        {
            if (part?["functionCall"] != null)
            {
                return part["functionCall"];
            }
        }

        return null;
    }

    private static string ExtractText(JsonObject? response)
    {
        var parts = response?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        if (parts == null || parts.Count == 0)
        {
            return "Nao foi possivel obter resposta do assistente.";
        }

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part?["text"] != null)
            {
                sb.AppendLine(part["text"]!.ToString());
            }
        }

        return sb.ToString().Trim();
    }
}
