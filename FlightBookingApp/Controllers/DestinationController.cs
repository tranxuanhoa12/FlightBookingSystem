using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlightBookingApp.Controllers
{
    public class DestinationController : Controller
    {
        [Authorize(Policy = "CustomerOnly")]
        public IActionResult BlogHanQuoc()
        {
            return View();
        }
        public IActionResult BlogAnh()
        {
            return View();
        }
    }
}
