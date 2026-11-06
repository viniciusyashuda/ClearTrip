using System.ComponentModel.DataAnnotations;

namespace ClearTrip.Models;

public class TripViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Informe o destino.")]
    [Display(Name = "Destino")]
    public string Destination { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a data de inicio.")]
    [Display(Name = "Data de Inicio")]
    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Informe a data de termino.")]
    [Display(Name = "Data de Termino")]
    [DataType(DataType.Date)]
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(2);

    [Display(Name = "Estilo de Viagem")]
    public string TravelStyle { get; set; } = "Cultural e Gastronomico";

    public string? WeatherSummary { get; set; }

    public string? GeneratedItinerary { get; set; }

    public string? ErrorMessage { get; set; }

    public int Days => (EndDate.DayNumber - StartDate.DayNumber) + 1;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var maxAllowedDate = today.AddDays(7);

        if (StartDate < today)
        {
            yield return new ValidationResult("A data de inicio nao pode ser anterior a hoje.", [nameof(StartDate)]);
        }

        if (EndDate <= StartDate)
        {
            yield return new ValidationResult("A data de termino deve ser posterior a data de inicio.", [nameof(EndDate)]);
        }

        if (EndDate > maxAllowedDate)
        {
            yield return new ValidationResult("A previsao do tempo esta limitada a no maximo 7 dias a partir de hoje.", [nameof(EndDate)]);
        }

        if (Days > 7)
        {
            yield return new ValidationResult("O periodo da viagem deve ser de no maximo 7 dias.", [nameof(EndDate)]);
        }
    }
}