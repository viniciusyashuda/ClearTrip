namespace ClearTrip.Services;

public interface IWeatherService
{
    Task<string> GetForecastAsync(string city, DateOnly startDate, DateOnly endDate);
    Task<string> GetForecastAsync(string city, int days);
}
