using Microsoft.AspNetCore.Mvc;
using ClearTrip.Models;
using ClearTrip.Services;

namespace ClearTrip.Controllers;

public class TripController : Controller
{
    private readonly IGeminiService _geminiService;

    public TripController(IGeminiService geminiService)
    {
        _geminiService = geminiService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new TripViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Plan(TripViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        try
        {
            model.GeneratedItinerary = await _geminiService.GenerateTripPlanAsync(
                model.Destination,
                model.StartDate,
                model.EndDate,
                model.TravelStyle);
        }
        catch (Exception ex)
        {
            model.ErrorMessage = $"Falha ao gerar o roteiro: {ex.Message}";
        }

        return View("Index", model);
    }
}
