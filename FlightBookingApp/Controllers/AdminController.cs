using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlightBookingApp.Data;
using FlightBookingApp.Models;
using FlightBookingApp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using FlightBookingApp.ViewModel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using FlightBookingApp.Views.Admin;

namespace FlightBookingApp.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly FlightDataService _flightDataService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AdminController> _logger;
        private readonly IMemoryCache _cache;
        private readonly IServiceScopeFactory _scopeFactory;
         private readonly EmailService _emailService;

        public AdminController(
            IConfiguration configuration,
            FlightDataService flightDataService,
            ApplicationDbContext context,
            ILogger<AdminController> logger,
            IMemoryCache cache,
            IServiceScopeFactory scopeFactory,            
            EmailService emailService)
        {
            _configuration = configuration;
            _flightDataService = flightDataService;
            _context = context;
            _logger = logger;
            _cache = cache;
            _scopeFactory = scopeFactory;
            _emailService = emailService;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login()
        {
            return View(new LoginViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (model == null)
            {
                _logger.LogWarning("LoginViewModel is null");
                return BadRequest("Model is null");
            }

            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    _logger.LogWarning("Validation error: {ErrorMessage}", error.ErrorMessage);
                }
                return View(model);
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null)
            {
                TempData["Error"] = "Email không tồn tại.";
                return View(model);
            }

            if (!BCrypt.Net.BCrypt.Verify(model.Password, user.Password))
            {
                _logger.LogWarning("Incorrect password for UserId: {UserId}", user.UserId);
                TempData["Error"] = "Mật khẩu không đúng.";
                return View(model);
            }

            if (user.Role != "Admin")
            {
                _logger.LogWarning("User is not an Admin: Role={Role}", user.Role);
                TempData["Error"] = "Tài khoản này không được phép đăng nhập tại đây.";
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var claimsIdentity = new ClaimsIdentity(claims, "AdminCookieAuth");
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync("AdminCookieAuth", claimsPrincipal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            });

            HttpContext.Session.SetString("UserId", user.UserId.ToString());

            return RedirectToAction("Index", "Admin");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserId")))
            {
                return RedirectToAction("Login");
            }

            try
            {
                var cacheKey = "AdminIndexStats_Basic";
                if (!_cache.TryGetValue(cacheKey, out AdminIndexViewModel viewModel))
                {
                    var today = DateTime.Today;

                    // Gộp các truy vấn về chuyến bay
                    var flightStats = await _context.Flights
                        .GroupBy(f => 1)
                        .Select(g => new
                        {
                            CompletedFlights = g.Count(f => f.ArrivalTime < DateTime.Now),
                            ActiveFlights = g.Count(f => f.DepartureTime <= DateTime.Now && f.ArrivalTime >= DateTime.Now),
                            CanceledFlights = g.Count(f => f.Status == "Canceled")
                        })
                        .FirstOrDefaultAsync();

                    // Thống kê tổng quan
                    var totalRevenue = await _context.Bookings
                        .SumAsync(b => (decimal?)b.TotalPrice) ?? 0;

                    // Thống kê số lượt truy cập
                    var visitStats = await _context.WebsiteVisits
                        .GroupBy(v => 1)
                        .Select(g => new
                        {
                            TotalVisits = g.Count(),
                            VisitsToday = g.Count(v => v.VisitDate.Date == today)
                        })
                        .FirstOrDefaultAsync();

                    // Thống kê tổng số vé
                    var totalTicketsThisMonth = await _context.Bookings
                        .CountAsync();

                    // Tạo ViewModel với dữ liệu cơ bản
                    viewModel = new AdminIndexViewModel
                    {
                        CompletedFlights = flightStats?.CompletedFlights ?? 0,
                        ActiveFlights = flightStats?.ActiveFlights ?? 0,
                        CanceledFlights = flightStats?.CanceledFlights ?? 0,
                        TotalRevenue = totalRevenue,
                        TotalVisits = visitStats?.TotalVisits ?? 0,
                        VisitsToday = visitStats?.VisitsToday ?? 0,
                        TotalTicketsThisMonth = totalTicketsThisMonth,
                        TicketSalesThisWeek = null, // Sẽ tải bằng AJAX
                        FlightScheduleLast8Months = null, // Sẽ tải bằng AJAX
                        PopularAirlines = null, // Sẽ tải bằng AJAX
                        PopularDestinations = null, // Sẽ tải bằng AJAX
                        Airlines = null, // Sẽ tải bằng AJAX
                        TopFlightRoutes = null // Sẽ tải bằng AJAX
                    };

                    // Lưu vào cache
                    _cache.Set(cacheKey, viewModel, TimeSpan.FromMinutes(10));
                }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Index action while fetching statistics");
                TempData["ErrorMessage"] = "Có lỗi xảy ra khi tải thống kê.";
                return View(new AdminIndexViewModel());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTicketSalesThisWeek()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cacheKey = "TicketSalesThisWeek";
            if (!_cache.TryGetValue(cacheKey, out Dictionary<string, int> ticketSalesThisWeek))
            {
                var today = DateTime.Today;
                var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                ticketSalesThisWeek = new Dictionary<string, int>();
                for (int i = 0; i < 7; i++)
                {
                    var day = startOfWeek.AddDays(i);
                    var tickets = await context.Bookings
                        .CountAsync(b => b.BookingDate.Date == day);
                    ticketSalesThisWeek[day.ToString("ddd")] = tickets;
                }

                _cache.Set(cacheKey, ticketSalesThisWeek, TimeSpan.FromMinutes(10));
            }

            return Json(ticketSalesThisWeek);
        }

        [HttpGet]
        public async Task<IActionResult> GetFlightScheduleLast8Months()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cacheKey = "FlightScheduleLast8Months";
            if (!_cache.TryGetValue(cacheKey, out Dictionary<string, int> flightScheduleLast8Months))
            {
                var today = DateTime.Today;
                var startOfLast8Months = today.AddMonths(-7);
                flightScheduleLast8Months = new Dictionary<string, int>();
                for (int i = 0; i < 8; i++)
                {
                    var month = startOfLast8Months.AddMonths(i);
                    var flights = await context.Flights
                        .CountAsync(f => f.DepartureTime.Year == month.Year && f.DepartureTime.Month == month.Month);
                    flightScheduleLast8Months[month.ToString("MMM yyyy")] = flights;
                }

                _cache.Set(cacheKey, flightScheduleLast8Months, TimeSpan.FromMinutes(10));
            }

            return Json(flightScheduleLast8Months);
        }

        [HttpGet]
        public async Task<IActionResult> GetPopularAirlines()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cacheKey = "PopularAirlines";
            if (!_cache.TryGetValue(cacheKey, out Dictionary<string, double> popularAirlinesDict))
            {
                var totalFlights = await context.Flights.CountAsync();
                var popularAirlines = await context.Flights
                    .GroupBy(f => f.Airline)
                    .Select(g => new
                    {
                        Airline = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(g => g.Count)
                    .Take(5)
                    .ToListAsync();
                popularAirlinesDict = popularAirlines.ToDictionary(
                    g => g.Airline,
                    g => totalFlights > 0 ? (double)g.Count / totalFlights * 100 : 0
                );

                _cache.Set(cacheKey, popularAirlinesDict, TimeSpan.FromMinutes(10));
            }

            return Json(popularAirlinesDict);
        }

        [HttpGet]
        public async Task<IActionResult> GetPopularDestinations()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cacheKey = "PopularDestinations";
            if (!_cache.TryGetValue(cacheKey, out Dictionary<string, double> popularDestinationsDict))
            {
                var totalDestinations = await context.Flights.CountAsync();
                var popularDestinations = await context.Flights
                    .GroupBy(f => f.DestinationAirport.City)
                    .Select(g => new
                    {
                        City = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(g => g.Count)
                    .Take(5)
                    .ToListAsync();
                popularDestinationsDict = popularDestinations.ToDictionary(
                    g => g.City,
                    g => totalDestinations > 0 ? (double)g.Count / totalDestinations * 100 : 0
                );

                _cache.Set(cacheKey, popularDestinationsDict, TimeSpan.FromMinutes(10));
            }

            return Json(popularDestinationsDict);
        }

        [HttpGet]
        public async Task<IActionResult> GetAirlines()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cacheKey = "Airlines";
            if (!_cache.TryGetValue(cacheKey, out List<AirlineStats> airlines))
            {
                airlines = await context.Flights
                    .GroupBy(f => f.Airline)
                    .Select(g => new AirlineStats
                    {
                        Name = g.Key,
                        TotalFlights = g.Count(),
                        NextFlightTime = g.Where(f => f.DepartureTime > DateTime.Now)
                                         .OrderBy(f => f.DepartureTime)
                                         .Select(f => f.DepartureTime)
                                         .FirstOrDefault()
                    })
                    .OrderByDescending(a => a.TotalFlights)
                    .Take(5)
                    .ToListAsync();

                _cache.Set(cacheKey, airlines, TimeSpan.FromMinutes(10));
            }

            return Json(airlines);
        }

        [HttpGet]
        public async Task<IActionResult> GetTopFlightRoutes()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cacheKey = "TopFlightRoutes";
            if (!_cache.TryGetValue(cacheKey, out List<FlightRouteStats> topFlightRoutesList))
            {
                var topFlightRoutes = await context.Flights
                    .GroupBy(f => new { f.DepartureAirportId, f.DestinationAirportId })
                    .Select(g => new
                    {
                        DepartureAirportId = g.Key.DepartureAirportId,
                        DestinationAirportId = g.Key.DestinationAirportId,
                        TotalPassengers = context.Passengers
                            .Count(p => p.Booking.FlightId == g.First().FlightId || p.Booking.ReturnFlightId == g.First().FlightId),
                        Duration = g.First().ArrivalTime - g.First().DepartureTime
                    })
                    .OrderByDescending(g => g.TotalPassengers)
                    .Take(5)
                    .ToListAsync();

                topFlightRoutesList = new List<FlightRouteStats>();
                foreach (var route in topFlightRoutes)
                {
                    var departureAirport = await context.Airports
                        .Where(a => a.AirportId == route.DepartureAirportId)
                        .Select(a => new { a.IataCode, a.City })
                        .FirstOrDefaultAsync();
                    var destinationAirport = await context.Airports
                        .Where(a => a.AirportId == route.DestinationAirportId)
                        .Select(a => new { a.IataCode, a.City })
                        .FirstOrDefaultAsync();

                    topFlightRoutesList.Add(new FlightRouteStats
                    {
                        Route = $"{departureAirport?.City} ({departureAirport?.IataCode}) - {destinationAirport?.City} ({destinationAirport?.IataCode})",
                        Duration = route.Duration,
                        TotalPassengers = route.TotalPassengers
                    });
                }

                _cache.Set(cacheKey, topFlightRoutesList, TimeSpan.FromMinutes(10));
            }

            return Json(topFlightRoutesList);
        }

        [HttpGet]
        public async Task<IActionResult> Flights_Book(int page = 1, int pageSize = 10)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Bắt đầu xử lý Flights_Book tại: {Time}", startTime);

            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserId")))
            {
                _logger.LogWarning("UserId không tồn tại trong session, chuyển hướng về Login");
                return RedirectToAction("Login");
            }

            try
            {
                // Đếm tổng số bản ghi
                var countStartTime = DateTime.UtcNow;
                int totalBookings;
                if (!_cache.TryGetValue("TotalBookings", out totalBookings))
                {
                    totalBookings = await _context.Bookings.CountAsync();
                    _cache.Set("TotalBookings", totalBookings, TimeSpan.FromMinutes(10));
                }
                _logger.LogInformation("Đếm tổng số bản ghi mất: {Duration} ms", (DateTime.UtcNow - countStartTime).TotalMilliseconds);

                int skip = (page - 1) * pageSize;
                int totalPages = (int)Math.Ceiling((double)totalBookings / pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;

                // Truy vấn danh sách bookings
                var queryStartTime = DateTime.UtcNow;
                var cacheKey = $"Bookings_Page_{page}_Size_{pageSize}";
                List<BookingViewModel> bookings;
                if (!_cache.TryGetValue(cacheKey, out bookings))
                {
                    var bookingList = await _context.Bookings
                        .AsNoTracking()
                        .OrderByDescending(b => b.BookingDate)
                        .Skip(skip)
                        .Take(pageSize)
                        .Include(b => b.Flight)
                            .ThenInclude(f => f.DepartureAirport)
                        .Include(b => b.Flight)
                            .ThenInclude(f => f.DestinationAirport)
                        .Include(b => b.ReturnFlight)
                            .ThenInclude(f => f.DepartureAirport)
                        .Include(b => b.ReturnFlight)
                            .ThenInclude(f => f.DestinationAirport)
                        .Include(b => b.Invoices)
                        .Select(b => new BookingViewModel
                        {
                            BookingId = b.BookingId,
                            UserId = b.UserId,
                            ContactName = b.ContactName ?? "Không có",
                            ContactEmail = b.ContactEmail ?? "Không có",
                            ContactPhone = b.ContactPhone ?? "Không có",
                            ContactGender = b.ContactGender ?? "Không có",
                            IsRoundTrip = b.IsRoundTrip,
                            OutboundFlight = b.Flight != null ? new FlightViewModel
                            {
                                FlightId = b.Flight.FlightId,
                                Airline = b.Flight.Airline ?? "Không có",
                                FlightNumber = b.Flight.FlightNumber ?? "Không có",
                                DepartureAirport = b.Flight.DepartureAirport != null ? new AirportViewModel
                                {
                                    IataCode = b.Flight.DepartureAirport.IataCode ?? "Không có",
                                    City = b.Flight.DepartureAirport.City ?? "Không có"
                                } : new AirportViewModel { IataCode = "Không có", City = "Không có" },
                                DestinationAirport = b.Flight.DestinationAirport != null ? new AirportViewModel
                                {
                                    IataCode = b.Flight.DestinationAirport.IataCode ?? "Không có",
                                    City = b.Flight.DestinationAirport.City ?? "Không có"
                                } : new AirportViewModel { IataCode = "Không có", City = "Không có" },
                                DepartureTime = b.Flight.DepartureTime,
                                ArrivalTime = b.Flight.ArrivalTime,
                                Price = b.Flight.Price
                            } : null,
                            ReturnFlight = b.ReturnFlight != null ? new FlightViewModel
                            {
                                FlightId = b.ReturnFlight.FlightId,
                                Airline = b.ReturnFlight.Airline ?? "Không có",
                                FlightNumber = b.ReturnFlight.FlightNumber ?? "Không có",
                                DepartureAirport = b.ReturnFlight.DepartureAirport != null ? new AirportViewModel
                                {
                                    IataCode = b.ReturnFlight.DepartureAirport.IataCode ?? "Không có",
                                    City = b.ReturnFlight.DepartureAirport.City ?? "Không có"
                                } : new AirportViewModel { IataCode = "Không có", City = "Không có" },
                                DestinationAirport = b.ReturnFlight.DestinationAirport != null ? new AirportViewModel
                                {
                                    IataCode = b.ReturnFlight.DestinationAirport.IataCode ?? "Không có",
                                    City = b.ReturnFlight.DestinationAirport.City ?? "Không có"
                                } : new AirportViewModel { IataCode = "Không có", City = "Không có" },
                                DepartureTime = b.ReturnFlight.DepartureTime,
                                ArrivalTime = b.ReturnFlight.ArrivalTime,
                                Price = b.ReturnFlight.Price
                            } : null,
                            TotalPrice = b.TotalPrice,
                            PaymentStatus = b.Status ?? "Không có",
                            BookingDate = b.BookingDate,
                            InvoiceRequest = b.Invoices.Any(),
                            CompanyName = b.Invoices.Any() ? (b.Invoices.First().CompanyName ?? "Không có") : "Không có",
                            CompanyAddress = b.Invoices.Any() ? (b.Invoices.First().CompanyAddress ?? "Không có") : "Không có",
                            CompanyCity = b.Invoices.Any() ? (b.Invoices.First().CompanyCity ?? "Không có") : "Không có",
                            TaxCode = b.Invoices.Any() ? (b.Invoices.First().TaxCode ?? "Không có") : "Không có",
                            InvoiceRecipient = b.Invoices.Any() ? (b.Invoices.First().InvoiceRecipient ?? "Không có") : "Không có",
                            InvoicePhone = b.Invoices.Any() ? (b.Invoices.First().InvoicePhone ?? "Không có") : "Không có",
                            InvoiceEmail = b.Invoices.Any() ? (b.Invoices.First().InvoiceEmail ?? "Không có") : "Không có"
                        })
                        .ToListAsync();

                    bookings = bookingList;

                    _logger.LogInformation("Truy vấn danh sách bookings mất: {Duration} ms", (DateTime.UtcNow - queryStartTime).TotalMilliseconds);

                    // Lưu vào cache
                    var cacheStartTime = DateTime.UtcNow;
                    _cache.Set(cacheKey, bookings, TimeSpan.FromMinutes(5));
                    _logger.LogInformation("Lưu vào cache mất: {Duration} ms", (DateTime.UtcNow - cacheStartTime).TotalMilliseconds);
                }
                else
                {
                    _logger.LogInformation("Lấy dữ liệu từ cache cho {CacheKey}", cacheKey);
                }

                // Tạo view model
                var viewModelStartTime = DateTime.UtcNow;
                var viewModel = new ManageBookingsViewModel
                {
                    Bookings = bookings,
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalPages = totalPages
                };
                _logger.LogInformation("Tạo view model mất: {Duration} ms", (DateTime.UtcNow - viewModelStartTime).TotalMilliseconds);

                if (!bookings.Any())
                {
                    TempData["InfoMessage"] = "Không có đặt vé nào để hiển thị.";
                }

                _logger.LogInformation("Hoàn tất Flights_Book, tổng thời gian: {Duration} ms", (DateTime.UtcNow - startTime).TotalMilliseconds);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Flights_Book action while fetching bookings");
                TempData["ErrorMessage"] = "Có lỗi xảy ra khi tải danh sách đặt vé.";
                return View(new ManageBookingsViewModel
                {
                    Bookings = new List<BookingViewModel>(),
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalPages = 1
                });
            }
        }
        [Authorize(Policy = "AdminOnly")]
        [HttpGet]
        public async Task<IActionResult> UsersManager()
        {
            // Lấy toàn bộ user có role = "Customer"
            var customers = await _context.Users
                .Where(u => u.Role == "Customer")
                .ToListAsync();

            // Trả về view, truyền danh sách customers
            return View(customers);
        }
        [HttpPost]
        public async Task<IActionResult> UpdateUserRole(int userId, string newRole)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                user.Role = newRole;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Cập nhật Role thành công!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user role.");
                return StatusCode(500, new { message = "Có lỗi xảy ra khi cập nhật role." });
            }
        }


        [HttpGet]
        public async Task<IActionResult> GetBookingDetails(int bookingId)
        {
            try
            {
                var booking = await _context.Bookings
                    .AsNoTracking()
                    .Include(b => b.Invoices)
                    .Include(b => b.Passengers)
                    .Include(b => b.Flight)
                        .ThenInclude(f => f.DepartureAirport)
                    .Include(b => b.Flight)
                        .ThenInclude(f => f.DestinationAirport)
                    .Include(b => b.ReturnFlight)
                        .ThenInclude(f => f.DepartureAirport)
                    .Include(b => b.ReturnFlight)
                        .ThenInclude(f => f.DestinationAirport)
                    .Where(b => b.BookingId == bookingId)
                    .Select(b => new BookingViewModel
                    {
                        BookingId = b.BookingId,
                        UserId = b.UserId,
                        ContactName = b.ContactName ?? "Không có",
                        ContactEmail = b.ContactEmail ?? "Không có",
                        ContactPhone = b.ContactPhone ?? "Không có",
                        ContactGender = b.ContactGender ?? "Không có",
                        IsRoundTrip = b.IsRoundTrip,
                        OutboundFlight = b.Flight != null ? new FlightViewModel
                        {
                            FlightId = b.Flight.FlightId,
                            Airline = b.Flight.Airline ?? "Không có",
                            FlightNumber = b.Flight.FlightNumber ?? "Không có",
                            DepartureAirport = b.Flight.DepartureAirport != null ? new AirportViewModel
                            {
                                IataCode = b.Flight.DepartureAirport.IataCode ?? "Không có",
                                City = b.Flight.DepartureAirport.City ?? "Không có"
                            } : new AirportViewModel { IataCode = "Không có", City = "Không có" },
                            DestinationAirport = b.Flight.DestinationAirport != null ? new AirportViewModel
                            {
                                IataCode = b.Flight.DestinationAirport.IataCode ?? "Không có",
                                City = b.Flight.DestinationAirport.City ?? "Không có"
                            } : new AirportViewModel { IataCode = "Không có", City = "Không có" },
                            DepartureTime = b.Flight.DepartureTime,
                            ArrivalTime = b.Flight.ArrivalTime,
                            Price = b.Flight.Price
                        } : null,
                        ReturnFlight = b.ReturnFlight != null ? new FlightViewModel
                        {
                            FlightId = b.ReturnFlight.FlightId,
                            Airline = b.ReturnFlight.Airline ?? "Không có",
                            FlightNumber = b.ReturnFlight.FlightNumber ?? "Không có",
                            DepartureAirport = b.ReturnFlight.DepartureAirport != null ? new AirportViewModel
                            {
                                IataCode = b.ReturnFlight.DepartureAirport.IataCode ?? "Không có",
                                City = b.ReturnFlight.DepartureAirport.City ?? "Không có"
                            } : new AirportViewModel { IataCode = "Không có", City = "Không có" },
                            DestinationAirport = b.ReturnFlight.DestinationAirport != null ? new AirportViewModel
                            {
                                IataCode = b.ReturnFlight.DestinationAirport.IataCode ?? "Không có",
                                City = b.ReturnFlight.DestinationAirport.City ?? "Không có"
                            } : new AirportViewModel { IataCode = "Không có", City = "Không có" },
                            DepartureTime = b.ReturnFlight.DepartureTime,
                            ArrivalTime = b.ReturnFlight.ArrivalTime,
                            Price = b.ReturnFlight.Price
                        } : null,
                        Passengers = b.Passengers.Select(p => new PassengerViewModel
                        {
                            FullName = p.FullName ?? "Không có",
                            Gender = p.Gender ?? "Không có",
                            DateOfBirth = p.DateOfBirth,
                            Nationality = p.Nationality ?? "Không có",
                            IdType = p.IdType ?? "Không có",
                            IdExpiry = p.IdExpiry,
                            IdCountry = p.IdCountry ?? "Không có",
                            LuggageFee = p.LuggageFee ?? 0
                        }).ToList(),
                        TotalPrice = b.TotalPrice,
                        PaymentStatus = b.Status ?? "Không có",
                        BookingDate = b.BookingDate,
                        InvoiceRequest = b.Invoices.Any(),
                        CompanyName = b.Invoices.Any() ? (b.Invoices.First().CompanyName ?? "Không có") : "Không có",
                        CompanyAddress = b.Invoices.Any() ? (b.Invoices.First().CompanyAddress ?? "Không có") : "Không có",
                        CompanyCity = b.Invoices.Any() ? (b.Invoices.First().CompanyCity ?? "Không có") : "Không có",
                        TaxCode = b.Invoices.Any() ? (b.Invoices.First().TaxCode ?? "Không có") : "Không có",
                        InvoiceRecipient = b.Invoices.Any() ? (b.Invoices.First().InvoiceRecipient ?? "Không có") : "Không có",
                        InvoicePhone = b.Invoices.Any() ? (b.Invoices.First().InvoicePhone ?? "Không có") : "Không có",
                        InvoiceEmail = b.Invoices.Any() ? (b.Invoices.First().InvoiceEmail ?? "Không có") : "Không có"
                    })
                    .FirstOrDefaultAsync();

                if (booking == null)
                {
                    return NotFound(new { message = "Không tìm thấy đặt vé." });
                }

                // Tính IsAdult
                foreach (var passenger in booking.Passengers)
                {
                    passenger.IsAdult = passenger.DateOfBirth.HasValue ? (DateTime.UtcNow.Year - passenger.DateOfBirth.Value.Year) >= 12 : true;
                }

                return Json(booking);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetBookingDetails action for BookingId: {BookingId}", bookingId);
                return StatusCode(500, new { message = "Có lỗi xảy ra khi tải chi tiết đặt vé." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SyncFlights()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserId")))
            {
                return RedirectToAction("Login");
            }

            try
            {
                await _flightDataService.FetchAndSaveAirportDataAsync();
                await _flightDataService.FetchAndSaveGlobalFlightDataAsync();
                TempData["SyncMessage"] = "Đồng bộ dữ liệu sân bay và chuyến bay thành công!";
                _logger.LogInformation("Flight data synchronized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SyncFlights action");
                TempData["SyncMessage"] = $"Lỗi khi đồng bộ dữ liệu: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("AdminCookieAuth");
            HttpContext.Session.Clear();
            TempData["Success"] = "Đăng xuất thành công!";
            return RedirectToAction("Login");
        }

        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> Data(int page = 1, int pageSize = 50, DateTime? lastDepartureTime = null, DateTime? filterDate = null)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Bắt đầu xử lý Data action tại: {Time}", startTime);

            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserId")))
            {
                _logger.LogWarning("UserId không tồn tại trong session, chuyển hướng về Login");
                return RedirectToAction("Login");
            }

            try
            {
                if (!_cache.TryGetValue("TotalFlights", out int totalFlights))
                {
                    totalFlights = await _context.Flights.CountAsync();
                    _cache.Set("TotalFlights", totalFlights, TimeSpan.FromMinutes(10));
                }

                var totalPages = (int)Math.Ceiling((double)totalFlights / pageSize);
                page = Math.Max(1, page);

                var lastDepartureTimes = HttpContext.Session.GetObject<List<DateTime?>>("LastDepartureTimes") ?? new List<DateTime?>();

                if (page == 1)
                {
                    lastDepartureTimes.Clear();
                    lastDepartureTimes.Add(null);
                }

                if (page <= lastDepartureTimes.Count)
                {
                    lastDepartureTime = lastDepartureTimes[page - 1];
                }
                else
                {
                    lastDepartureTime = lastDepartureTimes.LastOrDefault();
                }

                var flights = await GetFlightsFromDatabaseAsync(page, pageSize, lastDepartureTime, filterDate);

                if (flights.Any())
                {
                    var nextLastDepartureTime = flights.Last().DepartureTime;
                    if (lastDepartureTimes.Count < page)
                    {
                        lastDepartureTimes.Add(nextLastDepartureTime);
                    }
                    else
                    {
                        lastDepartureTimes[page - 1] = nextLastDepartureTime;
                    }
                }

                HttpContext.Session.SetObject("LastDepartureTimes", lastDepartureTimes);

                ViewBag.PageNumber = page;
                ViewBag.TotalPages = totalPages > 0 ? totalPages : 1;
                ViewBag.LastDepartureTimes = lastDepartureTimes;

                if (!flights.Any())
                {
                    TempData["InfoMessage"] = "Không có chuyến bay nào để hiển thị. Vui lòng đồng bộ dữ liệu.";
                }

                _logger.LogInformation("Hoàn tất Data action, tổng thời gian: {Duration} ms", (DateTime.UtcNow - startTime).TotalMilliseconds);
                return View(flights);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Data action while fetching flights");
                TempData["ErrorMessage"] = "Có lỗi xảy ra khi tải danh sách chuyến bay.";
                return View(new List<Flight>());
            }
        }

        private async Task<List<Flight>> GetFlightsFromDatabaseAsync(int pageNumber = 1, int pageSize = 50, DateTime? lastDepartureTime = null, DateTime? filterDate = null)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Bắt đầu truy vấn GetFlightsFromDatabaseAsync tại: {Time}", startTime);

            try
            {
                _context.Database.SetCommandTimeout(60);

                IQueryable<Flight> query = _context.Flights
                    .AsNoTracking()
                    .Include(f => f.DepartureAirport)
                    .Include(f => f.DestinationAirport)
                    .Select(f => new Flight
                    {
                        FlightId = f.FlightId,
                        FlightNumber = f.FlightNumber,
                        DepartureAirportId = f.DepartureAirportId,
                        DestinationAirportId = f.DestinationAirportId,
                        DepartureAirport = f.DepartureAirport != null ? new Airport
                        {
                            AirportId = f.DepartureAirport.AirportId,
                            Name = f.DepartureAirport.Name
                        } : null,
                        DestinationAirport = f.DestinationAirport != null ? new Airport
                        {
                            AirportId = f.DestinationAirport.AirportId,
                            Name = f.DestinationAirport.Name
                        } : null,
                        DepartureTime = f.DepartureTime,
                        ArrivalTime = f.ArrivalTime,
                        Price = f.Price
                    });

                // Thêm điều kiện lọc để chỉ lấy chuyến bay từ hiện tại trở đi
                query = query.Where(f => f.DepartureTime >= DateTime.Now);

                if (filterDate.HasValue)
                {
                    var startOfDay = filterDate.Value.Date;
                    var endOfDay = startOfDay.AddDays(1);
                    query = query.Where(f => f.DepartureTime >= startOfDay && f.DepartureTime < endOfDay);
                }

                query = query.OrderBy(f => f.DepartureTime);

                if (lastDepartureTime.HasValue)
                {
                    query = query.Where(f => f.DepartureTime > lastDepartureTime.Value);
                }

                var flights = await query
                    .Take(pageSize)
                    .ToListAsync();

                _logger.LogInformation("Truy vấn GetFlightsFromDatabaseAsync mất: {Duration} ms", (DateTime.UtcNow - startTime).TotalMilliseconds);
                return flights;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching flights from database");
                return new List<Flight>();
            }
        }

        [HttpGet]
        public IActionResult ReportManager()
        {
            return View();
        }

        // POST /Admin/SearchFlight
        [HttpPost]
        public async Task<IActionResult> SearchFlight([FromBody] FlightSearchRequest request)
        {
            // Chuẩn hóa đầu vào
            var flightNumber = (request?.FlightNumber ?? "").Trim();
            if (string.IsNullOrEmpty(flightNumber))
            {
                return Json(new { success = false, message = "Bạn chưa nhập FlightNumber." });
            }
            var normalized = flightNumber.ToUpper();

            // Truy vấn các chuyến bay có FlightNumber khớp, bao gồm thông tin sân bay
            var flights = await _context.Flights
                .Include(f => f.DepartureAirport)
                .Include(f => f.DestinationAirport)
                .Where(f => f.FlightNumber.ToUpper() == normalized)
                .ToListAsync();

            if (flights.Count == 0)
            {
                return Json(new { success = false, message = $"Không tìm thấy chuyến bay có FlightNumber = {flightNumber}" });
            }

            // Lấy thông tin chi tiết (bao gồm danh sách booking) cho tất cả các chuyến bay
            var flightDetails = new List<object>();
            foreach (var flight in flights)
            {
                var route = $"{flight.DepartureAirport?.City ?? "N/A"} -> {flight.DestinationAirport?.City ?? "N/A"}";
                var outboundTime = flight.DepartureTime.ToString("dd/MM/yyyy HH:mm");
                var returnTime = ""; // Vì model không có ReturnFlight
                var bookings = await _context.Bookings
                    .Include(b => b.Passengers)
                    .Where(b => b.FlightId == flight.FlightId)
                    .ToListAsync();

                flightDetails.Add(new
                {
                    flightId = flight.FlightId,
                    flightNumber = flight.FlightNumber,
                    route = route,
                    outboundTime = outboundTime,
                    returnTime = returnTime,
                    status = flight.Status,
                    totalBookings = bookings.Count,
                    bookings = bookings.Select(b => new
                    {
                        bookingId = b.BookingId,
                        contactName = b.ContactName,
                        contactEmail = b.ContactEmail,
                        passengerCount = b.Passengers.Count
                    }).ToList()
                });
            }

            return Json(new
            {
                success = true,
                flights = flightDetails
            });
        }

        /// <summary>
        /// Xây dựng JSON trả về thông tin của một chuyến bay kèm danh sách booking liên quan.
        /// </summary>
        private async Task<JsonResult> BuildFlightJson(Flight flight)
        {
            var route = $"{flight.DepartureAirport?.City ?? "N/A"} -> {flight.DestinationAirport?.City ?? "N/A"}";
            var outboundTime = flight.DepartureTime.ToString("dd/MM/yyyy HH:mm");
            var returnTime = ""; // Vì model không có ReturnFlight
            var bookings = await _context.Bookings
                .Include(b => b.Passengers)
                .Where(b => b.FlightId == flight.FlightId)
                .ToListAsync();

            return Json(new
            {
                success = true,
                flightId = flight.FlightId,
                flightNumber = flight.FlightNumber,
                route = route,
                outboundTime = outboundTime,
                returnTime = returnTime,
                totalBookings = bookings.Count,
                bookings = bookings.Select(b => new {
                    bookingId = b.BookingId,
                    contactName = b.ContactName,
                    contactEmail = b.ContactEmail,
                    passengerCount = b.Passengers.Count
                })
            });
        }

        // POST /Admin/SendDelayNotification
        [HttpPost]
        public async Task<IActionResult> SendDelayNotification(int flightId, int delayMinutes)
        {
            try
            {
                Console.WriteLine($"Received request to send delay notification: FlightId={flightId}, DelayMinutes={delayMinutes}");

                var flight = await _context.Flights
                    .Include(f => f.DepartureAirport)
                    .Include(f => f.DestinationAirport)
                    .FirstOrDefaultAsync(f => f.FlightId == flightId);
                if (flight == null)
                {
                    Console.WriteLine($"Flight not found: FlightId={flightId}");
                    return Json(new { success = false, message = "Chuyến bay không tồn tại." });
                }

                Console.WriteLine($"Flight found: FlightNumber={flight.FlightNumber}, DepartureTime={flight.DepartureTime}");

                if (string.IsNullOrEmpty(flight.FlightNumber))
                {
                    Console.WriteLine("FlightNumber is invalid.");
                    return Json(new { success = false, message = "FlightNumber không hợp lệ." });
                }
                if (flight.DepartureTime == default(DateTime))
                {
                    Console.WriteLine("DepartureTime is invalid.");
                    return Json(new { success = false, message = "DepartureTime không hợp lệ." });
                }

                var bookings = await _context.Bookings
                    .Where(b => b.FlightId == flightId)
                    .ToListAsync();

                if (!bookings.Any())
                {
                    Console.WriteLine($"No bookings found for FlightId={flightId}");
                    return Json(new { success = false, message = "Không có booking nào cho chuyến bay này. Email không được gửi." });
                }

                Console.WriteLine($"Found {bookings.Count} bookings for FlightId={flightId}");

                int successfulEmails = 0;
                foreach (var booking in bookings)
                {
                    if (string.IsNullOrEmpty(booking.ContactEmail) || !IsValidEmail(booking.ContactEmail))
                    {
                        Console.WriteLine($"Invalid email for BookingId={booking.BookingId}: {booking.ContactEmail}");
                        continue;
                    }

                    var subject = $"Thông báo delay chuyến bay {flight.FlightNumber}";
                    var newDeparture = flight.DepartureTime.AddMinutes(delayMinutes);
                    var body = $"Kính gửi {booking.ContactName},\n" +
                               $"Chuyến bay {flight.FlightNumber} ({flight.DepartureAirport?.City ?? "N/A"} -> {flight.DestinationAirport?.City ?? "N/A"}) đã bị delay {delayMinutes} phút.\n" +
                               $"Thời gian khởi hành mới dự kiến: {newDeparture:dd/MM/yyyy HH:mm}\n" +
                               $"Trân trọng,\nHãng bay";

                    Console.WriteLine($"Sending delay notification to {booking.ContactEmail} for FlightId={flightId}");
                    await _emailService.SendEmailAsync(booking.ContactEmail, subject, body);
                    Console.WriteLine($"Successfully sent delay notification to {booking.ContactEmail}");
                    successfulEmails++;
                }

                if (successfulEmails == 0)
                {
                    Console.WriteLine("No valid emails to send notification.");
                    return Json(new { success = false, message = "Không có email hợp lệ để gửi thông báo." });
                }

                Console.WriteLine($"Successfully sent delay notification to {successfulEmails} emails.");
                return Json(new { success = true, message = $"Đã gửi thông báo delay thành công đến {successfulEmails} email!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SendDelayNotification: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return Json(new { success = false, message = "Có lỗi xảy ra khi gửi thông báo: " + ex.Message });
            }
        }




        // Hàm kiểm tra email hợp lệ
        public class TestEmailRequest
        {
            public string Email { get; set; }
            public string ContactName { get; set; }
            public int FlightId { get; set; }
            public int DelayMinutes { get; set; }
        }
        public class SendMultipleEmailsRequest
        {
            public List<EmailRecipient> Recipients { get; set; }
            public int FlightId { get; set; }
            public int DelayMinutes { get; set; }
        }

        public class EmailRecipient
        {
            public string Email { get; set; }
            public string ContactName { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SendMultipleEmails([FromBody] SendMultipleEmailsRequest request)
        {
            try
            {
                var flight = await _context.Flights
                    .Include(f => f.DepartureAirport)
                    .Include(f => f.DestinationAirport)
                    .FirstOrDefaultAsync(f => f.FlightId == request.FlightId);
                if (flight == null)
                {
                    return Json(new { success = false, message = "Chuyến bay không tồn tại." });
                }

                if (string.IsNullOrEmpty(flight.FlightNumber))
                {
                    return Json(new { success = false, message = "FlightNumber không hợp lệ." });
                }
                if (flight.DepartureTime == default(DateTime))
                {
                    return Json(new { success = false, message = "DepartureTime không hợp lệ." });
                }

                int successfulEmails = 0;
                foreach (var recipient in request.Recipients)
                {
                    if (string.IsNullOrEmpty(recipient.Email) || !IsValidEmail(recipient.Email))
                    {
                        continue;
                    }

                    var subject = $"Thông báo delay chuyến bay {flight.FlightNumber}";
                    var newDeparture = flight.DepartureTime.AddMinutes(request.DelayMinutes);
                    var body = $"Kính gửi {recipient.ContactName},\n" +
                               $"Chuyến bay {flight.FlightNumber} ({flight.DepartureAirport?.City ?? "N/A"} -> {flight.DestinationAirport?.City ?? "N/A"}) đã bị delay {request.DelayMinutes} phút.\n" +
                               $"Thời gian khởi hành mới dự kiến: {newDeparture:dd/MM/yyyy HH:mm}\n" +
                               $"Trân trọng,\nHãng bay";

                    await _emailService.SendEmailAsync(recipient.Email, subject, body);
                    successfulEmails++;
                }

                if (successfulEmails == 0)
                {
                    return Json(new { success = false, message = "Không có email hợp lệ để gửi." });
                }

                return Json(new { success = true, message = $"Đã gửi thông báo delay thành công đến ${successfulEmails} email!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra khi gửi email: " + ex.Message });
            }
        }


        [HttpPost]
        public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest request)
        {
            try
            {
                Console.WriteLine($"Received request to send test email to: {request.Email}, FlightId: {request.FlightId}, DelayMinutes: {request.DelayMinutes}");

                if (string.IsNullOrEmpty(request.Email) || !IsValidEmail(request.Email))
                {
                    Console.WriteLine("Email is invalid.");
                    return Json(new { success = false, message = "Email không hợp lệ." });
                }

                var flight = await _context.Flights
                    .Include(f => f.DepartureAirport)
                    .Include(f => f.DestinationAirport)
                    .FirstOrDefaultAsync(f => f.FlightId == request.FlightId);
                if (flight == null)
                {
                    Console.WriteLine($"Flight not found: FlightId={request.FlightId}");
                    return Json(new { success = false, message = "Chuyến bay không tồn tại." });
                }

                if (string.IsNullOrEmpty(flight.FlightNumber))
                {
                    Console.WriteLine("FlightNumber is invalid.");
                    return Json(new { success = false, message = "FlightNumber không hợp lệ." });
                }
                if (flight.DepartureTime == default(DateTime))
                {
                    Console.WriteLine("DepartureTime is invalid.");
                    return Json(new { success = false, message = "DepartureTime không hợp lệ." });
                }

                var subject = $"Thông báo delay chuyến bay {flight.FlightNumber}";
                var newDeparture = flight.DepartureTime.AddMinutes(request.DelayMinutes);
                var body = $"Kính gửi Anh / Chị,\n\n" +
                           $"Chuyến bay {flight.FlightNumber} ({flight.DepartureAirport?.City ?? "N/A"} -> {flight.DestinationAirport?.City ?? "N/A"}) đã bị delay {request.DelayMinutes} phút.\n\n" +
                           $"Thời gian khởi hành mới dự kiến: {newDeparture:dd/MM/yyyy HH:mm}\n\n" +
                           $"Trân trọng,\nViVu";

                Console.WriteLine($"Sending email to {request.Email} with subject: {subject}");
                await _emailService.SendEmailAsync(request.Email, subject, body);
                Console.WriteLine($"Successfully sent email to {request.Email}");

                return Json(new { success = true, message = $"Đã gửi email thử thành công đến {request.Email}!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SendTestEmail: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return Json(new { success = false, message = "Có lỗi xảy ra khi gửi email thử: " + ex.Message });
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}