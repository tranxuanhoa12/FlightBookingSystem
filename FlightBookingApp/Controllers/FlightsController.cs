using Microsoft.AspNetCore.Mvc;
using System.Linq;

using FlightBookingApp.Data;

namespace Airline_Booking.Controllers
{
    public class FlightsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FlightsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetLocations(string term, string type)
        {
            var airports = _context.Airports
                .Where(a => a.Name.Contains(term) || a.IataCode.Contains(term) || a.City.Contains(term))
                .Select(a => new
                {
                    label = $"{a.Name} ({a.IataCode}) - {a.City}, {a.Country}",
                    value = a.IataCode
                })
                .Take(10)
                .ToList();

            return Json(airports);
        }
    }
}