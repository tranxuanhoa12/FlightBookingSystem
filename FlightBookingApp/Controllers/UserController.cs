using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlightBookingApp.Data;
using FlightBookingApp.Models;
using FlightBookingApp.ViewModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace FlightBookingApp.Controllers
{
    [Authorize(Policy = "CustomerOnly")]
    public class UserController : Controller
    {
        private readonly ApplicationDbContext _context;

        public UserController(ApplicationDbContext context)
        {
            _context = context;
        }

        [Authorize]
        public async Task<IActionResult> ManageBookings(int page = 1, int pageSize = 10)
        {
            // Lấy UserId từ Claims của người dùng đã đăng nhập
            int userId;
            try
            {
                userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            }
            catch
            {
                // Nếu không lấy được UserId, chuyển hướng đến trang đăng nhập
                return RedirectToAction("Login", "Account");
            }

            // Tính toán số lượng bản ghi cần bỏ qua (skip) và lấy (take)
            int skip = (page - 1) * pageSize;

            // Đếm tổng số đặt vé để tính số trang
            int totalBookings = await _context.Bookings
                .CountAsync(b => b.UserId == userId);
            int totalPages = (int)Math.Ceiling((double)totalBookings / pageSize);

            // Bước 1: Tải danh sách Booking với phân trang
            var bookingList = await _context.Bookings
                .Where(b => b.UserId == userId)
                .OrderBy(b => b.BookingDate) // Sắp xếp theo ngày đặt vé
                .Skip(skip)
                .Take(pageSize)
                .Select(b => new
                {
                    b.BookingId,
                    b.UserId,
                    b.ContactName,
                    b.ContactEmail,
                    b.ContactPhone,
                    b.ContactGender,
                    b.IsRoundTrip,
                    b.FlightId,
                    b.ReturnFlightId,
                    b.TotalPrice,
                    b.Status,
                    b.BookingDate,
                    HasInvoices = b.Invoices.Any(),
                    Invoice = b.Invoices.FirstOrDefault()
                })
                .ToListAsync();

            // Bước 2: Tải dữ liệu liên quan cho từng Booking
            var bookings = new List<BookingViewModel>();
            foreach (var b in bookingList)
            {
                // Tải Flight (OutboundFlight)
                var flight = await _context.Flights
                    .Where(f => f.FlightId == b.FlightId)
                    .Select(f => new FlightViewModel
                    {
                        FlightId = f.FlightId,
                        Airline = f.Airline ?? "Không có",
                        FlightNumber = f.FlightNumber ?? "Không có",
                        DepartureAirport = _context.Airports
                            .Where(a => a.AirportId == f.DepartureAirportId)
                            .Select(a => new AirportViewModel
                            {
                                IataCode = a.IataCode ?? "Không có",
                                City = a.City ?? "Không có"
                            })
                            .FirstOrDefault() ?? new AirportViewModel { IataCode = "Không có", City = "Không có" },
                        DestinationAirport = _context.Airports
                            .Where(a => a.AirportId == f.DestinationAirportId)
                            .Select(a => new AirportViewModel
                            {
                                IataCode = a.IataCode ?? "Không có",
                                City = a.City ?? "Không có"
                            })
                            .FirstOrDefault() ?? new AirportViewModel { IataCode = "Không có", City = "Không có" },
                        DepartureTime = f.DepartureTime,
                        ArrivalTime = f.ArrivalTime,
                        Price = f.Price
                    })
                    .FirstOrDefaultAsync();

                // Tải ReturnFlight (nếu có)
                FlightViewModel returnFlight = null;
                if (b.IsRoundTrip && b.ReturnFlightId.HasValue)
                {
                    returnFlight = await _context.Flights
                        .Where(f => f.FlightId == b.ReturnFlightId.Value)
                        .Select(f => new FlightViewModel
                        {
                            FlightId = f.FlightId,
                            Airline = f.Airline ?? "Không có",
                            FlightNumber = f.FlightNumber ?? "Không có",
                            DepartureAirport = _context.Airports
                                .Where(a => a.AirportId == f.DepartureAirportId)
                                .Select(a => new AirportViewModel
                                {
                                    IataCode = a.IataCode ?? "Không có",
                                    City = a.City ?? "Không có"
                                })
                                .FirstOrDefault() ?? new AirportViewModel { IataCode = "Không có", City = "Không có" },
                            DestinationAirport = _context.Airports
                                .Where(a => a.AirportId == f.DestinationAirportId)
                                .Select(a => new AirportViewModel
                                {
                                    IataCode = a.IataCode ?? "Không có",
                                    City = a.City ?? "Không có"
                                })
                                .FirstOrDefault() ?? new AirportViewModel { IataCode = "Không có", City = "Không có" },
                            DepartureTime = f.DepartureTime,
                            ArrivalTime = f.ArrivalTime,
                            Price = f.Price
                        })
                        .FirstOrDefaultAsync();
                }

                // Tải Passengers
                var passengers = await _context.Passengers
                    .Where(p => p.BookingId == b.BookingId)
                    .Select(p => new PassengerViewModel
                    {
                        FullName = p.FullName ?? "Không có",
                        Gender = p.Gender ?? "Không có",
                        DateOfBirth = p.DateOfBirth,
                        Nationality = p.Nationality ?? "Không có",
                        IdType = p.IdType ?? "Không có",
                        IdExpiry = p.IdExpiry,
                        IdCountry = p.IdCountry ?? "Không có",
                        LuggageFee = p.LuggageFee ?? 0,
                        IsAdult = p.DateOfBirth.HasValue ? (DateTime.Now.Year - p.DateOfBirth.Value.Year) >= 12 : true
                    })
                    .ToListAsync();

                // Tạo BookingViewModel
                var bookingViewModel = new BookingViewModel
                {
                    BookingId = b.BookingId,
                    UserId = b.UserId,
                    ContactName = b.ContactName ?? "Không có",
                    ContactEmail = b.ContactEmail ?? "Không có",
                    ContactPhone = b.ContactPhone ?? "Không có",
                    ContactGender = b.ContactGender ?? "Không có",
                    IsRoundTrip = b.IsRoundTrip,
                    OutboundFlight = flight,
                    ReturnFlight = returnFlight,
                    Passengers = passengers ?? new List<PassengerViewModel>(),
                    TotalPrice = b.TotalPrice,
                    PaymentStatus = b.Status ?? "Không có",
                    BookingDate = b.BookingDate,
                    InvoiceRequest = b.HasInvoices,
                    CompanyName = b.HasInvoices ? (b.Invoice.CompanyName ?? "Không có") : "Không có",
                    CompanyAddress = b.HasInvoices ? (b.Invoice.CompanyAddress ?? "Không có") : "Không có",
                    CompanyCity = b.HasInvoices ? (b.Invoice.CompanyCity ?? "Không có") : "Không có",
                    TaxCode = b.HasInvoices ? (b.Invoice.TaxCode ?? "Không có") : "Không có",
                    InvoiceRecipient = b.HasInvoices ? (b.Invoice.InvoiceRecipient ?? "Không có") : "Không có",
                    InvoicePhone = b.HasInvoices ? (b.Invoice.InvoicePhone ?? "Không có") : "Không có",
                    InvoiceEmail = b.HasInvoices ? (b.Invoice.InvoiceEmail ?? "Không có") : "Không có"
                };

                bookings.Add(bookingViewModel);
            }

            var viewModel = new ManageBookingsViewModel
            {
                Bookings = bookings,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages
            };

            return View(viewModel);
        }
    }
}