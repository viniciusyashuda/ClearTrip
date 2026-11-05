namespace ClearTrip.Services;

public interface IGeminiService
{
    Task<string> GenerateTripPlanAsync(string destination, DateOnly startDate, DateOnly endDate, string travelStyle);
}
