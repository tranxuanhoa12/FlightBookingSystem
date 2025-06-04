using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using FlightBookingApp.Data;
using FlightBookingApp.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Net;
using Hangfire;
using FlightBookingApp.Services;
using System.Text;
using System.Globalization;
using Newtonsoft.Json;
using FlightBookingApp.ViewModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using ZXing.QrCode;
using ZXing;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Net.payOS;
using Net.payOS.Types;


namespace FlightBookingApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HomeController> _logger;
        private readonly EmailService _emailService;
        private const string OPENAI_API_KEY = "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";
        private const string VNPAY_TMN_CODE = "TLTVD9RI";
        private const string VNPAY_HASH_SECRET = "EUY0I4TXCUW8GL2SN4AR1UMN0T0SKK7N";
        private const string VNPAY_URL = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
        private const string VNPAY_RETURN_URL = "https://localhost:7065/Home/VNPayReturn"; // Cập nhật
        private const string VNPAY_IPN_URL = "https://localhost:7065/Home/VNPayIPN";     // Cập nhật
        /*private readonly string PayOSClientId = "3a436514-495e-47c6-991e-2361cb65960c";
        private readonly string PayOSApiKey = "60f49ab0-5fcd-4dcd-a73b-d0453d4059e3";*/
        private readonly string PayOSBaseUrl = "https://api-merchant.payos.vn/";
        private readonly PayOS _payOS;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HomeController(
     ApplicationDbContext context,
     ILogger<HomeController> logger,
     EmailService emailService,
     IHttpContextAccessor httpContextAccessor,
     IConfiguration configuration) // Thêm IConfiguration
        {
            _context = context;
            _logger = logger;
            _emailService = emailService;
            _httpContextAccessor = httpContextAccessor;

            // Khởi tạo PayOS từ appsettings.json
            _payOS = new PayOS(
                configuration["Environment:PAYOS_CLIENT_ID"] ?? throw new Exception("Cannot find PAYOS_CLIENT_ID"),
                configuration["Environment:PAYOS_API_KEY"] ?? throw new Exception("Cannot find PAYOS_API_KEY"),
                configuration["Environment:PAYOS_CHECKSUM_KEY"] ?? throw new Exception("Cannot find PAYOS_CHECKSUM_KEY"),
                configuration["Environment:PAYOS_PARTNER_CODE"]
            );
        }

        [Authorize(Policy = "CustomerOnly")]
        [HttpPost]
        public async Task<IActionResult> CreatePaymentLink([FromBody] PaymentRequest request)
        {
            try
            {
                _logger.LogInformation("CreatePaymentLink called with amount={Amount}, orderId={OrderId}", request.Amount, request.OrderId);

                if (request.Amount <= 0)
                {
                    _logger.LogError("Invalid amount: {Amount}. Must be greater than zero.", request.Amount);
                    return Json(new { success = false, message = "Số tiền không hợp lệ. Phải lớn hơn 0." });
                }

                if (string.IsNullOrEmpty(request.OrderId) || !long.TryParse(request.OrderId, out long orderCode))
                {
                    _logger.LogError("Invalid orderId: {OrderId}. Must be a valid long integer.", request.OrderId);
                    return Json(new { success = false, message = "OrderId không hợp lệ. Phải là số nguyên dài." });
                }

                // Tạo description và giới hạn 25 ký tự
                string baseDescription = "Thanh toan don hang";
                string shortOrderId = request.OrderId.Length > 6 ? request.OrderId.Substring(0, 6) : request.OrderId;
                string description = $"{baseDescription} {shortOrderId}".Substring(0, Math.Min(25, $"{baseDescription} {shortOrderId}".Length));
                _logger.LogInformation("Generated description: {Description}", description);

                // Tạo PaymentData
                var paymentData = new PaymentData(
    orderCode: orderCode,
    amount: (int)request.Amount,
    description: description,
    items: new List<ItemData> { new ItemData("Vé máy bay", 1, (int)request.Amount) },
    cancelUrl: "https://localhost:7065/Home/PaymentCancel",
    returnUrl: $"https://localhost:7065/Home/ConfirmBooking?orderId={orderCode}&paymentMethod=PayOS"
);

                // Gọi API PayOS
                CreatePaymentResult createPaymentResult = await _payOS.createPaymentLink(paymentData);

                _logger.LogInformation("Payment link created successfully: {CheckoutUrl}", createPaymentResult.checkoutUrl);

                HttpContext.Session.SetString("CurrentOrderId", orderCode.ToString());

                return Json(new { success = true, paymentUrl = createPaymentResult.checkoutUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating PayOS payment link: {Message}", ex.Message);
                return Json(new { success = false, message = $"Lỗi khi tạo link thanh toán: {ex.Message}" });
            }
        }

     

        // Định nghĩa class để bind dữ liệu từ request body
        public class PaymentRequest
        {
            public decimal Amount { get; set; }
            public string OrderId { get; set; }
        }
        [Authorize(Policy = "CustomerOnly")]
        [HttpGet]
        public async Task<IActionResult> CheckPaymentStatus(string orderId)
        {
            try
            {
                if (!long.TryParse(orderId, out long orderCode))
                {
                    _logger.LogError("Invalid orderId: {OrderId}", orderId);
                    return Json(new { success = false, status = "ERROR", message = "OrderId không hợp lệ." });
                }

                PaymentLinkInformation paymentInfo = await _payOS.getPaymentLinkInformation(orderCode);

                if (paymentInfo.status == "PAID")
                {
                    return Json(new { success = true, status = "SUCCESS" });
                }
                return Json(new { success = false, status = paymentInfo.status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking payment status: {Message}", ex.Message);
                return Json(new { success = false, status = "ERROR", message = ex.Message });
            }
        }
        [Authorize(Policy = "CustomerOnly")]
        [HttpPost]
        public IActionResult PaymentWebhook()
        {
            // Đọc dữ liệu từ webhook
            using (var reader = new StreamReader(Request.Body))
            {
                var body = reader.ReadToEnd();
                var webhookData = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);

                if (webhookData["status"].ToString() == "SUCCESS")
                {
                    var orderId = webhookData["orderCode"].ToString();
                    // Cập nhật trạng thái thanh toán trong cơ sở dữ liệu
                    // Ví dụ: UpdateBookingStatus(orderId, "Completed");
                    return Json(new { success = true });
                }
            }
            return Json(new { success = false });
        }

        [HttpGet]
        public IActionResult PaymentSuccess()
        {
            TempData["Success"] = "Thanh toán thành công!";
            return RedirectToAction("ConfirmBooking");
        }

        [HttpGet]
        public IActionResult PaymentCancel()
        {
            TempData["Error"] = "Thanh toán đã bị hủy.";
            return RedirectToAction("ConfirmBooking");
        }

        [HttpPost]
        public async Task<IActionResult> Chatbot([FromBody] ChatRequest request)
        {
            if (string.IsNullOrEmpty(request.Message))
            {
                return Json(new { response = "Vui lòng nhập câu hỏi trước khi gửi." });
            }

            try
            {
                string response = await GetChatbotResponse(request.Message);
                return Json(new { response });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Chatbot: {Message}, InnerException: {InnerException}, StackTrace: {StackTrace}",
                    ex.Message, ex.InnerException?.Message, ex.StackTrace);
                return Json(new { response = "Đã có lỗi xảy ra. Vui lòng thử lại sau." });
            }
        }

        private async Task<string> GetChatbotResponse(string userMessage)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", OPENAI_API_KEY);

                var requestBody = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new { role = "system", content = "Bạn là một trợ lý thông minh của ViVu Airline, chuyên hỗ trợ khách hàng về đặt vé máy bay, thông tin chuyến bay, và các dịch vụ liên quan." },
                        new { role = "user", content = userMessage }
                    },
                    max_tokens = 300
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var responseString = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Response from API: {Response}", responseString);

                dynamic result = JsonConvert.DeserializeObject(responseString);

                if (result == null || result.error != null)
                {
                    return $"Lỗi từ API: {result?.error?.message ?? "Không thể kết nối tới máy chủ."}";
                }

                if (result?.choices == null || result.choices.Count == 0)
                {
                    return "Không nhận được phản hồi từ chatbot. Vui lòng thử lại.";
                }

                return result.choices[0].message.content.ToString().Trim();
            }
        }

        public class ChatRequest
        {
            public string Message { get; set; }
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult SearchFlights(string tripType, string fromIata, string toIata, string departureDate, string returnDate, int adultCount, int childCount)
        {
            _logger.LogInformation("SearchFlights called with: tripType={TripType}, fromIata={FromIata}, toIata={ToIata}, departureDate={DepartureDate}, returnDate={ReturnDate}, adultCount={AdultCount}, childCount={ChildCount}",
                tripType, fromIata, toIata, departureDate, returnDate, adultCount, childCount);

            try
            {
                if (string.IsNullOrEmpty(tripType))
                {
                    _logger.LogWarning("TripType is empty.");
                    TempData["Error"] = "Vui lòng chọn loại chuyến đi (Khứ hồi hoặc 1 chiều).";
                    return RedirectToAction("Index");
                }

                if (string.IsNullOrEmpty(fromIata) || string.IsNullOrEmpty(toIata))
                {
                    _logger.LogWarning("FromIata or ToIata is empty.");
                    TempData["Error"] = "Vui lòng chọn sân bay đi và sân bay đến.";
                    return RedirectToAction("Index");
                }

                if (fromIata == toIata)
                {
                    _logger.LogWarning("FromIata and ToIata are the same: {IataCode}", fromIata);
                    TempData["Error"] = "Sân bay đi và sân bay đến không được trùng nhau.";
                    return RedirectToAction("Index");
                }

                if (adultCount < 1)
                {
                    _logger.LogWarning("AdultCount must be at least 1.");
                    TempData["Error"] = "Phải có ít nhất 1 người lớn.";
                    return RedirectToAction("Index");
                }

                if (string.IsNullOrEmpty(departureDate))
                {
                    _logger.LogWarning("DepartureDate is empty.");
                    TempData["Error"] = "Vui lòng chọn ngày đi.";
                    return RedirectToAction("Index");
                }

                if (!DateTime.TryParse(departureDate, out DateTime parsedDepartureDate))
                {
                    _logger.LogWarning("Invalid DepartureDate format: {DepartureDate}", departureDate);
                    TempData["Error"] = "Ngày đi không hợp lệ.";
                    return RedirectToAction("Index");
                }

                DateTime today = DateTime.Today;
                if (parsedDepartureDate.Date < today)
                {
                    _logger.LogWarning("DepartureDate is before today: {DepartureDate}", parsedDepartureDate);
                    TempData["Error"] = "Ngày đi không được trước ngày hiện tại.";
                    return RedirectToAction("Index");
                }

                bool isRoundTrip = tripType == "RoundTrip";
                DateTime? parsedReturnDate = null;
                if (isRoundTrip)
                {
                    if (string.IsNullOrEmpty(returnDate))
                    {
                        _logger.LogWarning("ReturnDate is required for round trip.");
                        TempData["Error"] = "Vui lòng chọn ngày về cho chuyến khứ hồi.";
                        return RedirectToAction("Index");
                    }

                    if (!DateTime.TryParse(returnDate, out DateTime tempReturnDate))
                    {
                        _logger.LogWarning("Invalid ReturnDate format: {ReturnDate}", returnDate);
                        TempData["Error"] = "Ngày về không hợp lệ.";
                        return RedirectToAction("Index");
                    }

                    parsedReturnDate = tempReturnDate;
                    if (parsedReturnDate <= parsedDepartureDate)
                    {
                        _logger.LogWarning("ReturnDate must be after DepartureDate. DepartureDate: {DepartureDate}, ReturnDate: {ReturnDate}", parsedDepartureDate, parsedReturnDate);
                        TempData["Error"] = "Ngày về phải sau ngày đi.";
                        return RedirectToAction("Index");
                    }
                }

                int passengerCount = adultCount + childCount;
                _logger.LogInformation("PassengerCount: {PassengerCount}", passengerCount);

                if (!_context.Database.CanConnect())
                {
                    _logger.LogError("Cannot connect to the database.");
                    TempData["Error"] = "Không thể kết nối đến cơ sở dữ liệu. Vui lòng thử lại sau.";
                    return RedirectToAction("Index");
                }

                _logger.LogInformation("Querying airports: fromIata={FromIata}, toIata={ToIata}", fromIata, toIata);
                var departureAirport = _context.Airports.FirstOrDefault(a => a.IataCode == fromIata);
                var destinationAirport = _context.Airports.FirstOrDefault(a => a.IataCode == toIata);

                if (departureAirport == null || destinationAirport == null)
                {
                    _logger.LogWarning("Airport not found: fromIata={FromIata}, toIata={ToIata}", fromIata, toIata);
                    TempData["Error"] = "Không tìm thấy sân bay. Vui lòng kiểm tra lại.";
                    return RedirectToAction("Index");
                }

                _logger.LogInformation("DepartureAirport: {DepartureAirportId}, DestinationAirport: {DestinationAirportId}",
                    departureAirport.AirportId, destinationAirport.AirportId);

                _logger.LogInformation("Querying flights: DepartureAirportId={DepartureAirportId}, DestinationAirportId={DestinationAirportId}, DepartureDate={DepartureDate}",
                    departureAirport.AirportId, destinationAirport.AirportId, parsedDepartureDate.Date);
                var flights = _context.Flights
                    .Include(f => f.DepartureAirport)
                    .Include(f => f.DestinationAirport)
                    .Where(f => f.DepartureAirportId == departureAirport.AirportId &&
                                f.DestinationAirportId == destinationAirport.AirportId &&
                                f.DepartureTime.Date == parsedDepartureDate.Date &&
                                f.AvailableSeats >= passengerCount &&
                                f.Status == "Scheduled")
                    .ToList();

                _logger.LogInformation("Found {FlightCount} outbound flights.", flights.Count);

                List<Flight> returnFlights = null;
                if (isRoundTrip && parsedReturnDate.HasValue)
                {
                    _logger.LogInformation("Querying return flights: DepartureAirportId={DepartureAirportId}, DestinationAirportId={DestinationAirportId}, ReturnDate={ReturnDate}",
                        destinationAirport.AirportId, departureAirport.AirportId, parsedReturnDate.Value.Date);
                    returnFlights = _context.Flights
                        .Include(f => f.DepartureAirport)
                        .Include(f => f.DestinationAirport)
                        .Where(f => f.DepartureAirportId == destinationAirport.AirportId &&
                                    f.DestinationAirportId == departureAirport.AirportId &&
                                    f.DepartureTime.Date == parsedReturnDate.Value.Date &&
                                    f.AvailableSeats >= passengerCount &&
                                    f.Status == "Scheduled")
                        .ToList();
                    _logger.LogInformation("Found {ReturnFlightCount} return flights.", returnFlights?.Count ?? 0);
                }

                ViewBag.DepartureAirport = departureAirport;
                ViewBag.DestinationAirport = destinationAirport;
                ViewBag.DepartureDate = parsedDepartureDate;
                ViewBag.ReturnDate = parsedReturnDate;
                ViewBag.PassengerCount = passengerCount;
                ViewBag.AdultCount = adultCount;
                ViewBag.ChildCount = childCount;
                ViewBag.IsRoundTrip = isRoundTrip;
                ViewBag.ReturnFlights = returnFlights;

                if (!flights.Any())
                {
                    _logger.LogWarning("No outbound flights found for route {FromIata} to {ToIata} on {DepartureDate}.", fromIata, toIata, parsedDepartureDate.Date);
                    TempData["Error"] = $"Không có chuyến bay từ {departureAirport.City} ({fromIata}) đến {destinationAirport.City} ({toIata}) vào ngày {parsedDepartureDate:dd/MM/yyyy}.";
                    return RedirectToAction("Index");
                }

                _logger.LogInformation("Rendering SearchResults view with {FlightCount} flights.", flights.Count);
                return View("SearchResults", flights);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in SearchFlights: {Message}, InnerException: {InnerException}, StackTrace: {StackTrace}",
                    ex.Message, ex.InnerException?.Message, ex.StackTrace);
                TempData["Error"] = "Có lỗi xảy ra khi tìm kiếm chuyến bay: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        public IActionResult GetLocations(string term, string type)
        {
            try
            {
                var airports = _context.Airports.ToList().AsQueryable();

                var vietnamAirports = new List<string>
                {
                    "HAN", "SGN", "DAD", "HPH", "NHA", "PQC", "VCA", "HUI"
                };

                if (string.IsNullOrEmpty(term))
                {
                    airports = airports.Where(a => vietnamAirports.Contains(a.IataCode));
                }
                else
                {
                    string normalizedTerm = RemoveDiacritics(term.ToLower());
                    airports = airports.Where(a => RemoveDiacritics(a.City).ToLower().Contains(normalizedTerm) ||
                                                  RemoveDiacritics(a.Name).ToLower().Contains(normalizedTerm) ||
                                                  a.IataCode.ToLower().Contains(normalizedTerm));
                }

                var suggestions = airports
                    .OrderBy(a => a.City)
                    .Select(a => new
                    {
                        label = $"{a.City} ({a.IataCode}) - {a.Name}",
                        value = a.IataCode
                    })
                    .Take(10)
                    .ToList();

                return Json(suggestions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetLocations: {Message}, InnerException: {InnerException}, StackTrace: {StackTrace}",
                    ex.Message, ex.InnerException?.Message, ex.StackTrace);
                return Json(new List<object> { new { label = "Lỗi khi tìm kiếm sân bay", value = "" } });
            }
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string normalized = text.Normalize(NormalizationForm.FormD);
            StringBuilder builder = new StringBuilder();

            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        [Authorize(Policy = "CustomerOnly")]
        [Authorize]
        [HttpGet]
        public IActionResult Book(int flightId, int? returnFlightId, int passengerCount, int adultCount, int childCount, bool isRoundTrip)
        {
            _logger.LogInformation("Book (GET) called with: flightId={FlightId}, returnFlightId={ReturnFlightId}, passengerCount={PassengerCount}, adultCount={AdultCount}, childCount={ChildCount}, isRoundTrip={IsRoundTrip}",
                flightId, returnFlightId, passengerCount, adultCount, childCount, isRoundTrip);

            try
            {
                if (!_context.Database.CanConnect())
                {
                    _logger.LogError("Cannot connect to the database.");
                    TempData["Error"] = "Không thể kết nối đến cơ sở dữ liệu. Vui lòng thử lại sau.";
                    return RedirectToAction("Index");
                }

                var flight = _context.Flights
                    .Include(f => f.DepartureAirport)
                    .Include(f => f.DestinationAirport)
                    .FirstOrDefault(f => f.FlightId == flightId);

                if (flight == null)
                {
                    _logger.LogWarning("Flight not found: flightId={FlightId}", flightId);
                    TempData["Error"] = "Không tìm thấy chuyến bay đi.";
                    return RedirectToAction("Index");
                }

                if (flight.AvailableSeats < passengerCount)
                {
                    _logger.LogWarning("Not enough available seats for flightId={FlightId}. AvailableSeats={AvailableSeats}, PassengerCount={PassengerCount}",
                        flightId, flight.AvailableSeats, passengerCount);
                    TempData["Error"] = "Không đủ ghế trống cho chuyến bay đi.";
                    return RedirectToAction("Index");
                }

                Flight returnFlight = null;
                if (isRoundTrip && returnFlightId.HasValue)
                {
                    returnFlight = _context.Flights
                        .Include(f => f.DepartureAirport)
                        .Include(f => f.DestinationAirport)
                        .FirstOrDefault(f => f.FlightId == returnFlightId.Value);

                    if (returnFlight == null)
                    {
                        _logger.LogWarning("Return flight not found: returnFlightId={ReturnFlightId}", returnFlightId);
                        TempData["Error"] = "Không tìm thấy chuyến bay về.";
                        return RedirectToAction("Index");
                    }

                    if (returnFlight.AvailableSeats < passengerCount)
                    {
                        _logger.LogWarning("Not enough available seats for returnFlightId={ReturnFlightId}. AvailableSeats={AvailableSeats}, PassengerCount={PassengerCount}",
                            returnFlightId, returnFlight.AvailableSeats, passengerCount);
                        TempData["Error"] = "Không đủ ghế trống cho chuyến bay về.";
                        return RedirectToAction("Index");
                    }
                }

                ViewBag.Flight = flight;
                ViewBag.ReturnFlight = returnFlight;
                ViewBag.PassengerCount = passengerCount;
                ViewBag.AdultCount = adultCount;
                ViewBag.ChildCount = childCount;
                ViewBag.IsRoundTrip = isRoundTrip;

                _logger.LogInformation("Rendering Book view for flightId={FlightId}", flightId);
                return View("Book");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Book (GET): {Message}, InnerException: {InnerException}, StackTrace: {StackTrace}",
                    ex.Message, ex.InnerException?.Message, ex.StackTrace);
                TempData["Error"] = "Có lỗi xảy ra khi hiển thị trang đặt vé: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        [Authorize]
        [HttpPost]
        public IActionResult ConfirmBooking(BookingFormViewModel model)
        {
            _logger.LogInformation("ConfirmBooking (POST) called with flightId={FlightId}, contactEmail={ContactEmail}, contactPhone={ContactPhone}",
                model.FlightId, model.ContactEmail, model.ContactPhone);

            _logger.LogInformation("Form data: {@Model}", model);

            try
            {
                if (model.FlightId <= 0)
                {
                    _logger.LogWarning("Invalid FlightId: {FlightId}. Redirecting to Index.", model.FlightId);
                    TempData["Error"] = "Chuyến bay không hợp lệ. Vui lòng chọn lại.";
                    return RedirectToAction("Index");
                }

                if (string.IsNullOrEmpty(model.ContactEmail) || string.IsNullOrEmpty(model.ContactPhone) ||
                    string.IsNullOrEmpty(model.ContactName) || string.IsNullOrEmpty(model.ContactGender))
                {
                    _logger.LogWarning("Contact information is missing. Redirecting to Book.");
                    TempData["Error"] = "Vui lòng điền đầy đủ thông tin liên hệ.";
                    return RedirectToAction("Book", new
                    {
                        flightId = model.FlightId,
                        returnFlightId = model.ReturnFlightId,
                        passengerCount = model.PassengerCount,
                        adultCount = model.AdultCount,
                        childCount = model.ChildCount,
                        isRoundTrip = model.IsRoundTrip
                    });
                }

                if (model.PassengerNames == null || model.PassengerNames.Count != model.PassengerCount ||
                    model.PassengerGenders == null || model.PassengerGenders.Count != model.PassengerCount)
                {
                    _logger.LogWarning("Passenger information is missing or incorrect. Redirecting to Book.");
                    TempData["Error"] = "Vui lòng điền đầy đủ thông tin hành khách.";
                    return RedirectToAction("Book", new
                    {
                        flightId = model.FlightId,
                        returnFlightId = model.ReturnFlightId,
                        passengerCount = model.PassengerCount,
                        adultCount = model.AdultCount,
                        childCount = model.ChildCount,
                        isRoundTrip = model.IsRoundTrip
                    });
                }

                var flight = _context.Flights
                    .Include(f => f.DepartureAirport)
                    .Include(f => f.DestinationAirport)
                    .FirstOrDefault(f => f.FlightId == model.FlightId && f.Status == "Scheduled");

                if (flight == null)
                {
                    _logger.LogWarning("Flight not found or not in Scheduled status: flightId={FlightId}", model.FlightId);
                    TempData["Error"] = $"Không tìm thấy chuyến bay đi với ID {model.FlightId} hoặc chuyến bay không còn khả dụng.";
                    return RedirectToAction("Index");
                }

                if (flight.AvailableSeats < model.PassengerCount)
                {
                    _logger.LogWarning("Not enough available seats for flightId={FlightId}. AvailableSeats={AvailableSeats}, PassengerCount={PassengerCount}",
                        model.FlightId, flight.AvailableSeats, model.PassengerCount);
                    TempData["Error"] = "Không đủ ghế trống cho chuyến bay đi.";
                    return RedirectToAction("Index");
                }

                Flight returnFlight = null;
                if (model.IsRoundTrip && model.ReturnFlightId.HasValue)
                {
                    returnFlight = _context.Flights
                        .Include(f => f.DepartureAirport)
                        .Include(f => f.DestinationAirport)
                        .FirstOrDefault(f => f.FlightId == model.ReturnFlightId.Value && f.Status == "Scheduled");

                    if (returnFlight == null)
                    {
                        _logger.LogWarning("Return flight not found or not in Scheduled status: returnFlightId={ReturnFlightId}", model.ReturnFlightId);
                        TempData["Error"] = $"Không tìm thấy chuyến bay về với ID {model.ReturnFlightId} hoặc chuyến bay không còn khả dụng.";
                        return RedirectToAction("Index");
                    }

                    if (returnFlight.AvailableSeats < model.PassengerCount)
                    {
                        _logger.LogWarning("Not enough available seats for returnFlightId={ReturnFlightId}. AvailableSeats={AvailableSeats}, PassengerCount={PassengerCount}",
                            model.ReturnFlightId, returnFlight.AvailableSeats, model.PassengerCount);
                        TempData["Error"] = "Không đủ ghế trống cho chuyến bay về.";
                        return RedirectToAction("Index");
                    }
                }

                int userId;
                try
                {
                    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userIdClaim))
                    {
                        throw new Exception("UserId claim is missing.");
                    }
                    userId = int.Parse(userIdClaim);
                    _logger.LogInformation("UserId retrieved: {UserId}", userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve UserId from claims.");
                    TempData["Error"] = "Không thể xác định người dùng. Vui lòng đăng nhập lại.";
                    return RedirectToAction("Login", "Account");
                }

                decimal totalPrice = CalculateTotalPrice(model);
                _logger.LogInformation("TotalPrice calculated: {TotalPrice}", totalPrice);

                var passengers = new List<PassengerDetails>();
                for (int i = 0; i < model.PassengerNames.Count; i++)
                {
                    string gender = model.PassengerGenders[i];
                    if (gender != "Male" && gender != "Female")
                    {
                        _logger.LogWarning("Invalid Gender value: {Gender}", gender);
                        TempData["Error"] = "Giới tính không hợp lệ.";
                        return RedirectToAction("Book", new
                        {
                            flightId = model.FlightId,
                            returnFlightId = model.ReturnFlightId,
                            passengerCount = model.PassengerCount,
                            adultCount = model.AdultCount,
                            childCount = model.ChildCount,
                            isRoundTrip = model.IsRoundTrip
                        });
                    }
                    string dbGender = gender == "Male" ? "M" : "F";

                    string idCountry = i < model.AdultCount ? model.IdCountryAdult[i] : model.IdCountryChild[i - model.AdultCount];
                    string nationality = i < model.AdultCount ? model.NationalityAdult[i] : model.NationalityChild[i - model.AdultCount];
                    if (string.IsNullOrEmpty(idCountry) || string.IsNullOrEmpty(nationality))
                    {
                        _logger.LogWarning("IdCountry or Nationality is empty for passenger {Index}", i);
                        TempData["Error"] = "Quốc gia cấp hoặc quốc tịch không được để trống.";
                        return RedirectToAction("Book", new
                        {
                            flightId = model.FlightId,
                            returnFlightId = model.ReturnFlightId,
                            passengerCount = model.PassengerCount,
                            adultCount = model.AdultCount,
                            childCount = model.ChildCount,
                            isRoundTrip = model.IsRoundTrip
                        });
                    }
                    string dbIdCountry = idCountry == "Vietnam" ? "VN" : idCountry;
                    string dbNationality = nationality == "Vietnam" ? "VN" : nationality;

                    string idType = i < model.AdultCount ? model.IdTypeAdult[i] : model.IdTypeChild[i - model.AdultCount];
                    if (!string.IsNullOrEmpty(idType) && idType.Length > 50)
                    {
                        _logger.LogWarning("IdType exceeds maximum length of 50 characters: {IdType}", idType);
                        TempData["Error"] = "Giấy tờ tùy thân không được vượt quá 50 ký tự.";
                        return RedirectToAction("Book", new
                        {
                            flightId = model.FlightId,
                            returnFlightId = model.ReturnFlightId,
                            passengerCount = model.PassengerCount,
                            adultCount = model.AdultCount,
                            childCount = model.ChildCount,
                            isRoundTrip = model.IsRoundTrip
                        });
                    }

                    DateTime? dob = null;
                    if (!string.IsNullOrEmpty(model.PassengerDob[i]) && DateTime.TryParse(model.PassengerDob[i], out DateTime parsedDob))
                    {
                        dob = parsedDob;
                    }

                    DateTime? expiry = null;
                    if (i < model.AdultCount && !string.IsNullOrEmpty(model.IdExpiryAdult[i]) && DateTime.TryParse(model.IdExpiryAdult[i], out DateTime parsedExpiry))
                    {
                        expiry = parsedExpiry;
                    }

                    var passenger = new PassengerDetails
                    {
                        FullName = model.PassengerNames[i],
                        DateOfBirth = dob,
                        Gender = dbGender,
                        IdType = idType,
                        IdExpiry = expiry,
                        IdCountry = dbIdCountry,
                        Nationality = dbNationality,
                        LuggageFee = i < model.AdultCount ? decimal.Parse(model.LuggageAdult[i] ?? "0") : decimal.Parse(model.LuggageChild[i - model.AdultCount] ?? "0")
                    };
                    passengers.Add(passenger);
                }

                var confirmationViewModel = new BookingConfirmationViewModel
                {
                    BookingForm = model,
                    Flight = flight,
                    ReturnFlight = returnFlight,
                    Passengers = passengers,
                    TotalPrice = totalPrice,
                    UserId = userId
                };

                HttpContext.Session.SetString("BookingConfirmationData", JsonConvert.SerializeObject(confirmationViewModel, new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Formatting = Formatting.Indented
                }));

                // Reset payment status and method in session
                HttpContext.Session.SetString("PaymentStatus", "Pending");
                HttpContext.Session.SetString("PaymentMethod", "");
                HttpContext.Session.SetString("QRCodeUrl", "https://via.placeholder.com/200?text=QR+Code");
                HttpContext.Session.SetString("BookingId", "0");

                _logger.LogInformation("Redirecting to ConfirmBooking (GET) for review.");
                return RedirectToAction("ConfirmBooking");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ConfirmBooking (POST): {Message}", ex.Message);
                TempData["Error"] = "Có lỗi xảy ra khi xử lý thông tin đặt vé: " + ex.Message;
                return RedirectToAction("Book", new
                {
                    flightId = model?.FlightId ?? 0,
                    returnFlightId = model?.ReturnFlightId,
                    passengerCount = model?.PassengerCount ?? 0,
                    adultCount = model?.AdultCount ?? 0,
                    childCount = model?.ChildCount ?? 0,
                    isRoundTrip = model?.IsRoundTrip ?? false
                });
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        private decimal CalculateTotalPrice(BookingFormViewModel model)
        {
            decimal totalPrice = 0;
            var flight = _context.Flights.Find(model.FlightId);
            if (flight == null)
            {
                throw new Exception($"Flight with ID {model.FlightId} not found.");
            }
            totalPrice += flight.Price * model.PassengerCount;

            if (model.IsRoundTrip && model.ReturnFlightId.HasValue)
            {
                var returnFlight = _context.Flights.Find(model.ReturnFlightId.Value);
                if (returnFlight == null)
                {
                    throw new Exception($"Return flight with ID {model.ReturnFlightId} not found.");
                }
                totalPrice += returnFlight.Price * model.PassengerCount;
            }

            for (int i = 0; i < model.AdultCount; i++)
            {
                totalPrice += decimal.Parse(model.LuggageAdult[i] ?? "0");
            }
            for (int i = 0; i < model.ChildCount; i++)
            {
                totalPrice += decimal.Parse(model.LuggageChild[i] ?? "0");
            }

            return totalPrice;
        }

        [Authorize(Policy = "CustomerOnly")]
        [HttpGet]
        public async Task<IActionResult> ConfirmBooking(string orderId, string paymentMethod)
        {
            try
            {
                var confirmationData = HttpContext.Session.GetString("BookingConfirmationData");
                if (string.IsNullOrEmpty(confirmationData))
                {
                    _logger.LogWarning("BookingConfirmationData not found in Session.");
                    TempData["Error"] = "Không tìm thấy thông tin đặt vé. Vui lòng thử lại từ đầu.";
                    return RedirectToAction("Index");
                }

                var confirmationViewModel = JsonConvert.DeserializeObject<BookingConfirmationViewModel>(confirmationData, new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                });

                if (confirmationViewModel == null || confirmationViewModel.BookingForm == null || confirmationViewModel.BookingForm.FlightId <= 0)
                {
                    _logger.LogWarning("Invalid BookingConfirmationViewModel or FlightId: {@ConfirmationViewModel}", confirmationViewModel);
                    TempData["Error"] = "Thông tin đặt vé không hợp lệ. Vui lòng thử lại từ đầu.";
                    return RedirectToAction("Index");
                }

                // Lấy token từ session và truyền vào ViewBag
                ViewBag.JWToken = _httpContextAccessor.HttpContext?.Session.GetString("JWToken") ?? "";
                ViewBag.PaymentStatus = HttpContext.Session.GetString("PaymentStatus") ?? "Pending";
                ViewBag.PaymentMethod = HttpContext.Session.GetString("PaymentMethod") ?? "";
                ViewBag.QRCodeUrl = HttpContext.Session.GetString("QRCodeUrl") ?? "https://via.placeholder.com/200?text=QR+Code";

                // Kiểm tra trạng thái thanh toán từ PayOS nếu có orderId
                if (!string.IsNullOrEmpty(orderId) && paymentMethod == "PayOS")
                {
                    if (long.TryParse(orderId, out long orderCode))
                    {
                        try
                        {
                            PaymentLinkInformation paymentInfo = await _payOS.getPaymentLinkInformation(orderCode);
                            if (paymentInfo.status == "PAID")
                            {
                                ViewBag.PaymentStatus = "Completed";
                                ViewBag.PaymentMethod = "PayOS";
                                HttpContext.Session.SetString("PaymentStatus", "Completed");
                                HttpContext.Session.SetString("PaymentMethod", "PayOS");
                                HttpContext.Session.SetString("CurrentOrderId", orderId); // Lưu orderId để sử dụng sau nếu cần
                                _logger.LogInformation("Payment confirmed for orderId={OrderId}", orderId);
                            }
                            else if (paymentInfo.status == "CANCELLED" || paymentInfo.status == "EXPIRED")
                            {
                                ViewBag.PaymentStatus = "Failed";
                                HttpContext.Session.SetString("PaymentStatus", "Failed");
                                _logger.LogWarning("Payment failed or cancelled for orderId={OrderId}", orderId);
                                TempData["Error"] = "Thanh toán đã bị hủy hoặc hết hạn. Vui lòng thử lại.";
                            }
                            else
                            {
                                ViewBag.PaymentStatus = "Pending";
                                _logger.LogInformation("Payment still pending for orderId={OrderId}", orderId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error checking PayOS payment status for orderId={OrderId}", orderId);
                            ViewBag.PaymentStatus = "Pending";
                            TempData["Error"] = "Không thể xác minh trạng thái thanh toán từ PayOS. Vui lòng thử lại.";
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Invalid orderId format: {OrderId}", orderId);
                        TempData["Error"] = "Mã đơn hàng không hợp lệ.";
                    }
                }

                return View(confirmationViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ConfirmBooking (GET): {Message}", ex.Message);
                TempData["Error"] = "Có lỗi xảy ra khi hiển thị thông tin đặt vé: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        [HttpGet]
        public IActionResult GenerateVNPayQRCode(decimal amount, int bookingId)
        {
            try
            {
                _logger.LogInformation("GenerateVNPayQRCode called with amount={Amount}, bookingId={BookingId}", amount, bookingId);

                string vnp_TxnRef = bookingId == 0 ? "ORDER" + DateTime.Now.Ticks.ToString() : "ORDER" + bookingId;
                string vnp_CreateDate = DateTime.Now.ToString("yyyyMMddHHmmss");
                string vnp_IpAddr = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

                var vnp_Params = new Dictionary<string, string>
        {
            { "vnp_Version", "2.1.0" },
            { "vnp_Command", "pay" },
            { "vnp_TmnCode", VNPAY_TMN_CODE },
            { "vnp_Amount", (amount * 100).ToString("F0") },
            { "vnp_CreateDate", vnp_CreateDate },
            { "vnp_CurrCode", "VND" },
            { "vnp_IpAddr", vnp_IpAddr },
            { "vnp_Locale", "vn" },
            { "vnp_OrderInfo", "Thanh toan don hang " + vnp_TxnRef },
            { "vnp_OrderType", "250000" },
            { "vnp_ReturnUrl", VNPAY_RETURN_URL },
            { "vnp_TxnRef", vnp_TxnRef },
            { "vnp_ExpireDate", DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss") }
        };

                string hashData = string.Join("&", vnp_Params.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                string vnp_SecureHash = HmacSHA512(VNPAY_HASH_SECRET, hashData);
                vnp_Params["vnp_SecureHash"] = vnp_SecureHash;

                string paymentUrl = VNPAY_URL + "?" + string.Join("&", vnp_Params.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                _logger.LogInformation("Generated VNPay payment URL: {PaymentUrl}", paymentUrl);

                string qrCodeBase64 = GenerateQRCodeBase64(paymentUrl);
                if (string.IsNullOrEmpty(qrCodeBase64))
                {
                    _logger.LogError("Failed to generate QR code for paymentUrl: {PaymentUrl}", paymentUrl);
                    return Json(new { success = false, message = "Không thể tạo mã QR từ URL thanh toán." });
                }

                string qrCodeUrl = $"data:image/png;base64,{qrCodeBase64}";
                HttpContext.Session.SetString("QRCodeUrl", qrCodeUrl);

                return Json(new { success = true, qrCodeUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating VNPay QR code: {Message}", ex.Message);
                return Json(new { success = false, message = $"Không thể tạo mã QR VNPay: {ex.Message}" });
            }
        }
        [Authorize(Policy = "CustomerOnly")]
        [HttpGet]
        public IActionResult GenerateBookingQRCode(int bookingId)
        {
            try
            {
                // Lấy thông tin đặt vé từ cơ sở dữ liệu
                var booking = _context.Bookings
                    .Include(b => b.Flight)
                        .ThenInclude(f => f.DepartureAirport)
                    .Include(b => b.Flight)
                        .ThenInclude(f => f.DestinationAirport)
                    .Include(b => b.ReturnFlight)
                        .ThenInclude(f => f.DepartureAirport)
                    .Include(b => b.ReturnFlight)
                        .ThenInclude(f => f.DestinationAirport)
                    .Include(b => b.Passengers)
                    .FirstOrDefault(b => b.BookingId == bookingId);

                if (booking == null)
                {
                    _logger.LogWarning("Booking not found for QR code generation: bookingId={BookingId}", bookingId);
                    return Json(new { success = false, error = "Không tìm thấy thông tin đặt vé." });
                }

                // Tạo nội dung QR Code (có thể tùy chỉnh định dạng)
                var qrContent = new StringBuilder();
                qrContent.AppendLine($"BookingId: {booking.BookingId}");
                qrContent.AppendLine($"Contact: {booking.ContactName} ({booking.ContactEmail})");
                qrContent.AppendLine($"Flight: {booking.Flight.Airline} {booking.Flight.FlightNumber}");
                qrContent.AppendLine($"From: {booking.Flight.DepartureAirport.City} ({booking.Flight.DepartureAirport.IataCode}) at {booking.Flight.DepartureTime:dd/MM/yyyy HH:mm}");
                qrContent.AppendLine($"To: {booking.Flight.DestinationAirport.City} ({booking.Flight.DestinationAirport.IataCode}) at {booking.Flight.ArrivalTime:dd/MM/yyyy HH:mm}");
                if (booking.IsRoundTrip && booking.ReturnFlight != null)
                {
                    qrContent.AppendLine($"Return Flight: {booking.ReturnFlight.Airline} {booking.ReturnFlight.FlightNumber}");
                    qrContent.AppendLine($"From: {booking.ReturnFlight.DepartureAirport.City} ({booking.ReturnFlight.DepartureAirport.IataCode}) at {booking.ReturnFlight.DepartureTime:dd/MM/yyyy HH:mm}");
                    qrContent.AppendLine($"To: {booking.ReturnFlight.DestinationAirport.City} ({booking.ReturnFlight.DestinationAirport.IataCode}) at {booking.ReturnFlight.ArrivalTime:dd/MM/yyyy HH:mm}");
                }
                qrContent.AppendLine($"Passengers: {string.Join(", ", booking.Passengers.Select(p => p.FullName))}");
                qrContent.AppendLine($"Total Price: {booking.TotalPrice:N0} VNĐ");

                // Tạo mã QR từ nội dung
                string qrCodeBase64 = GenerateQRCodeBase64(qrContent.ToString());
                string qrCodeUrl = $"data:image/png;base64,{qrCodeBase64}";

                return Json(new { success = true, qrCodeUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating Booking QR code: {Message}", ex.Message);
                return Json(new { success = false, error = "Không thể tạo mã QR đặt vé." });
            }
        }
        [Authorize(Policy = "CustomerOnly")]
        [HttpGet]
        public IActionResult GenerateBankTransferQRCode(decimal amount, int bookingId)
        {
            try
            {
                // Thông tin tài khoản nhận (có thể thay đổi theo thông tin thực tế của bạn)
                string bankAccount = "0352808225";
                string bankName = "MB Bank";
                string accountHolder = "Le Truong Luat";

                // Tạo nội dung QR Code (theo chuẩn VietQR hoặc định dạng tùy chỉnh)
                string qrContent = $"BankTransfer|Account:{bankAccount}|Bank:{bankName}|Holder:{accountHolder}|Amount:{amount}|OrderId:{bookingId}";
                _logger.LogInformation("Bank Transfer QR Code Content: {QrContent}", qrContent);

                // Tạo mã QR từ nội dung
                string qrCodeBase64 = GenerateQRCodeBase64(qrContent);
                string qrCodeUrl = $"data:image/png;base64,{qrCodeBase64}";

                // Lưu vào session để sử dụng sau
                HttpContext.Session.SetString("QRCodeUrl", qrCodeUrl);
                HttpContext.Session.SetString("BookingId", bookingId.ToString());

                return Json(new { success = true, qrCodeUrl, amount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating Bank Transfer QR code: {Message}", ex.Message);
                return Json(new { success = false, error = "Không thể tạo mã QR chuyển khoản." });
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        private string GenerateQRCodeBase64(string content)
        {
            try
            {
                var qrCodeWriter = new BarcodeWriterPixelData
                {
                    Format = BarcodeFormat.QR_CODE,
                    Options = new QrCodeEncodingOptions
                    {
                        ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                        Width = 300,
                        Height = 300
                    }
                };

                var pixelData = qrCodeWriter.Write(content);
                using (var bitmap = new Bitmap(pixelData.Width, pixelData.Height, PixelFormat.Format32bppRgb))
                {
                    using (var ms = new MemoryStream())
                    {
                        var bitmapData = bitmap.LockBits(new Rectangle(0, 0, pixelData.Width, pixelData.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                        try
                        {
                            System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmapData.Scan0, pixelData.Pixels.Length);
                        }
                        finally
                        {
                            bitmap.UnlockBits(bitmapData);
                        }

                        bitmap.Save(ms, ImageFormat.Png);
                        byte[] byteImage = ms.ToArray();
                        return Convert.ToBase64String(byteImage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo mã QR với ZXing: {Message}", ex.Message);
                return null;
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        [HttpPost]
        public IActionResult ProcessCreditCardPayment([FromBody] CreditCardPaymentRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.CardNumber) || string.IsNullOrEmpty(request.CardHolder) ||
                    string.IsNullOrEmpty(request.ExpiryDate) || string.IsNullOrEmpty(request.Cvv))
                {
                    return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin thẻ." });
                }

                // Giả lập thanh toán thành công (thay thế bằng tích hợp cổng thanh toán thực tế nếu cần)
                HttpContext.Session.SetString("PaymentStatus", "Completed");
                HttpContext.Session.SetString("PaymentMethod", "CreditCard");
                return Json(new { success = true, message = "Thanh toán thành công!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing credit card payment: {Message}", ex.Message);
                return Json(new { success = false, message = "Có lỗi xảy ra khi xử lý thanh toán." });
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        [HttpPost]
        public IActionResult ProcessVNPayPayment([FromBody] VNPayPaymentRequest request)
        {
            try
            {
                // Giả lập thanh toán thành công (thay thế bằng logic thực tế nếu cần)
                HttpContext.Session.SetString("PaymentStatus", "Completed");
                HttpContext.Session.SetString("PaymentMethod", "VNPayQR");
                return Json(new { success = true, message = "Thanh toán thành công!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing VNPay payment: {Message}", ex.Message);
                return Json(new { success = false, message = "Có lỗi xảy ra khi xử lý thanh toán VNPay." });
            }
        }

       /* [Authorize(Policy = "CustomerOnly")]
        [HttpGet]
        public JsonResult CheckPaymentStatus(int bookingId)
        {
            var booking = _context.Bookings.FirstOrDefault(b => b.BookingId == bookingId);
            if (booking == null)
            {
                return Json(new { status = "NotFound" });
            }

            // Giả lập trạng thái thanh toán (thay thế bằng logic thực tế nếu bạn có hệ thống xác nhận thanh toán)
            string status = booking.Status switch
            {
                "Paid" => "Success",
                "Pending" => "Pending",
                _ => "Failed"
            };

            return Json(new { status });
        }*/

        [Authorize(Policy = "CustomerOnly")]
        [HttpPost]
        public IActionResult ConfirmBankTransferPayment(int bookingId)
        {
            try
            {
                var booking = _context.Bookings.FirstOrDefault(b => b.BookingId == bookingId);
                if (booking == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy thông tin đặt vé." });
                }

                // Giả lập xác nhận thanh toán (thay thế bằng logic thực tế nếu bạn có hệ thống xác nhận)
                booking.Status = "Paid";
                var payment = new Payment
                {
                    BookingId = bookingId,
                    PaymentMethod = "BankTransfer",
                    Amount = booking.TotalPrice,
                    PaymentDate = DateTime.Now,
                    Status = "Completed"
                };
                _context.Payments.Add(payment);
                _context.SaveChanges();

                BackgroundJob.Schedule(() => SendBookingEmail(bookingId), TimeSpan.FromMinutes(2));

                return Json(new { success = true, message = "Thanh toán đã được xác nhận!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming bank transfer payment: {Message}", ex.Message);
                return Json(new { success = false, message = "Có lỗi xảy ra khi xác nhận thanh toán." });
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        [Authorize]
        [HttpPost]
        public IActionResult CompleteBooking([FromForm] BookingFormViewModel model, decimal totalPrice, int userId, string paymentMethod, string paymentStatus)
        {
            _logger.LogInformation("CompleteBooking (POST) called with flightId={FlightId}, contactEmail={ContactEmail}, contactPhone={ContactPhone}, totalPrice={TotalPrice}, userId={UserId}, paymentMethod={PaymentMethod}, paymentStatus={PaymentStatus}",
                model.FlightId, model.ContactEmail, model.ContactPhone, totalPrice, userId, paymentMethod, paymentStatus);

            _logger.LogInformation("Form data: {@Model}", model);

            try
            {
                // Kiểm tra trạng thái thanh toán từ Session
                string sessionPaymentStatus = HttpContext.Session.GetString("PaymentStatus") ?? "Pending";
                if (sessionPaymentStatus != "Completed")
                {
                    _logger.LogWarning("Payment not completed. PaymentStatus: {PaymentStatus}", sessionPaymentStatus);
                    TempData["Error"] = "Bạn cần thanh toán trước khi hoàn tất đặt vé.";
                    return RedirectToAction("ConfirmBooking");
                }

                // Kiểm tra dữ liệu đầu vào
                if (model.FlightId <= 0)
                {
                    _logger.LogWarning("Invalid FlightId: {FlightId}. Redirecting to Index.", model.FlightId);
                    TempData["Error"] = "Chuyến bay không hợp lệ. Vui lòng chọn lại.";
                    return RedirectToAction("Index");
                }

                if (string.IsNullOrEmpty(model.ContactEmail) || string.IsNullOrEmpty(model.ContactPhone) ||
                    string.IsNullOrEmpty(model.ContactName) || string.IsNullOrEmpty(model.ContactGender))
                {
                    _logger.LogWarning("Contact information is missing.");
                    TempData["Error"] = "Vui lòng điền đầy đủ thông tin liên hệ.";
                    return RedirectToAction("Index");
                }

                if (model.PassengerNames == null || model.PassengerNames.Count != model.PassengerCount ||
                    model.PassengerGenders == null || model.PassengerGenders.Count != model.PassengerCount)
                {
                    _logger.LogWarning("Passenger information is missing or incorrect.");
                    TempData["Error"] = "Vui lòng điền đầy đủ thông tin hành khách.";
                    return RedirectToAction("Index");
                }

                var flight = _context.Flights
                    .Include(f => f.DepartureAirport)
                    .Include(f => f.DestinationAirport)
                    .FirstOrDefault(f => f.FlightId == model.FlightId && f.Status == "Scheduled");

                if (flight == null)
                {
                    _logger.LogWarning("Flight not found or not in Scheduled status: flightId={FlightId}", model.FlightId);
                    TempData["Error"] = $"Không tìm thấy chuyến bay đi với ID {model.FlightId} hoặc chuyến bay không còn khả dụng.";
                    return RedirectToAction("Index");
                }

                if (flight.AvailableSeats < model.PassengerCount)
                {
                    _logger.LogWarning("Not enough available seats for flightId={FlightId}. AvailableSeats={AvailableSeats}, PassengerCount={PassengerCount}",
                        model.FlightId, flight.AvailableSeats, model.PassengerCount);
                    TempData["Error"] = "Không đủ ghế trống cho chuyến bay đi.";
                    return RedirectToAction("Index");
                }

                Flight returnFlight = null;
                if (model.IsRoundTrip && model.ReturnFlightId.HasValue)
                {
                    returnFlight = _context.Flights
                        .Include(f => f.DepartureAirport)
                        .Include(f => f.DestinationAirport)
                        .FirstOrDefault(f => f.FlightId == model.ReturnFlightId.Value && f.Status == "Scheduled");

                    if (returnFlight == null)
                    {
                        _logger.LogWarning("Return flight not found or not in Scheduled status: returnFlightId={ReturnFlightId}", model.ReturnFlightId);
                        TempData["Error"] = $"Không tìm thấy chuyến bay về với ID {model.ReturnFlightId} hoặc chuyến bay không còn khả dụng.";
                        return RedirectToAction("Index");
                    }

                    if (returnFlight.AvailableSeats < model.PassengerCount)
                    {
                        _logger.LogWarning("Not enough available seats for returnFlightId={ReturnFlightId}. AvailableSeats={AvailableSeats}, PassengerCount={PassengerCount}",
                            model.ReturnFlightId, returnFlight.AvailableSeats, model.PassengerCount);
                        TempData["Error"] = "Không đủ ghế trống cho chuyến bay về.";
                        return RedirectToAction("Index");
                    }
                }

                var passengers = new List<PassengerDetails>();
                for (int i = 0; i < model.PassengerNames.Count; i++)
                {
                    string gender = model.PassengerGenders[i];
                    if (gender != "Male" && gender != "Female")
                    {
                        _logger.LogWarning("Invalid Gender value: {Gender}", gender);
                        TempData["Error"] = "Giới tính không hợp lệ.";
                        return RedirectToAction("Index");
                    }
                    string dbGender = gender == "Male" ? "M" : "F";

                    string idCountry = i < model.AdultCount ? model.IdCountryAdult[i] : model.IdCountryChild[i - model.AdultCount];
                    string nationality = i < model.AdultCount ? model.NationalityAdult[i] : model.NationalityChild[i - model.AdultCount];
                    if (string.IsNullOrEmpty(idCountry) || string.IsNullOrEmpty(nationality))
                    {
                        _logger.LogWarning("IdCountry or Nationality is empty for passenger {Index}", i);
                        TempData["Error"] = "Quốc gia cấp hoặc quốc tịch không được để trống.";
                        return RedirectToAction("Index");
                    }
                    string dbIdCountry = idCountry == "Vietnam" ? "VN" : idCountry;
                    string dbNationality = nationality == "Vietnam" ? "VN" : nationality;

                    string idType = i < model.AdultCount ? model.IdTypeAdult[i] : model.IdTypeChild[i - model.AdultCount];
                    if (!string.IsNullOrEmpty(idType) && idType.Length > 50)
                    {
                        _logger.LogWarning("IdType exceeds maximum length of 50 characters: {IdType}", idType);
                        TempData["Error"] = "Giấy tờ tùy thân không được vượt quá 50 ký tự.";
                        return RedirectToAction("Index");
                    }

                    DateTime? dob = null;
                    if (!string.IsNullOrEmpty(model.PassengerDob[i]) && DateTime.TryParse(model.PassengerDob[i], out DateTime parsedDob))
                    {
                        dob = parsedDob;
                    }

                    DateTime? expiry = null;
                    if (i < model.AdultCount && !string.IsNullOrEmpty(model.IdExpiryAdult[i]) && DateTime.TryParse(model.IdExpiryAdult[i], out DateTime parsedExpiry))
                    {
                        expiry = parsedExpiry;
                    }

                    var passenger = new PassengerDetails
                    {
                        FullName = model.PassengerNames[i],
                        DateOfBirth = dob,
                        Gender = dbGender,
                        IdType = idType,
                        IdExpiry = expiry,
                        IdCountry = dbIdCountry,
                        Nationality = dbNationality,
                        LuggageFee = i < model.AdultCount ? decimal.Parse(model.LuggageAdult[i] ?? "0") : decimal.Parse(model.LuggageChild[i - model.AdultCount] ?? "0")
                    };
                    passengers.Add(passenger);
                }

                Booking booking = null;
                using (var transaction = _context.Database.BeginTransaction())
                {
                    try
                    {
                        booking = new Booking
                        {
                            UserId = userId,
                            FlightId = model.FlightId,
                            ReturnFlightId = model.ReturnFlightId,
                            IsRoundTrip = model.IsRoundTrip,
                            PassengerCount = model.PassengerCount,
                            ContactEmail = model.ContactEmail,
                            ContactPhone = model.ContactPhone,
                            ContactName = model.ContactName,
                            ContactGender = model.ContactGender,
                            BookingDate = DateTime.Now,
                            Status = "Pending",
                            TotalPrice = totalPrice,
                            PaymentMethod = paymentMethod
                        };

                        _logger.LogInformation("Adding Booking to context: {@Booking}", booking);
                        _context.Bookings.Add(booking);
                        _context.SaveChanges();
                        _logger.LogInformation("Booking saved successfully with BookingId: {BookingId}", booking.BookingId);

                        foreach (var passengerDetails in passengers)
                        {
                            var passenger = new Passenger
                            {
                                BookingId = booking.BookingId,
                                FullName = passengerDetails.FullName,
                                DateOfBirth = passengerDetails.DateOfBirth,
                                Gender = passengerDetails.Gender,
                                IdType = passengerDetails.IdType,
                                IdExpiry = passengerDetails.IdExpiry,
                                IdCountry = passengerDetails.IdCountry,
                                Nationality = passengerDetails.Nationality,
                                LuggageFee = passengerDetails.LuggageFee
                            };
                            _context.Passengers.Add(passenger);
                        }
                        _context.SaveChanges();
                        _logger.LogInformation("Passengers saved successfully for BookingId: {BookingId}", booking.BookingId);

                        if (model.InvoiceRequest)
                        {
                            var invoice = new Invoice
                            {
                                BookingId = booking.BookingId,
                                CompanyName = model.CompanyName,
                                CompanyAddress = model.CompanyAddress,
                                CompanyCity = model.CompanyCity,
                                TaxCode = model.TaxCode,
                                InvoiceRecipient = model.InvoiceRecipient,
                                InvoicePhone = model.InvoicePhone,
                                InvoiceEmail = model.InvoiceEmail
                            };
                            _logger.LogInformation("Adding Invoice to context: {@Invoice}", invoice);
                            _context.Invoices.Add(invoice);
                            _context.SaveChanges();
                            _logger.LogInformation("Invoice saved successfully for BookingId: {BookingId}", booking.BookingId);
                        }

                        flight.AvailableSeats -= model.PassengerCount;
                        if (model.IsRoundTrip && model.ReturnFlightId.HasValue)
                        {
                            returnFlight.AvailableSeats -= model.PassengerCount;
                        }

                        var payment = new Payment
                        {
                            BookingId = booking.BookingId,
                            PaymentMethod = paymentMethod,
                            Amount = booking.TotalPrice,
                            PaymentDate = DateTime.Now,
                            Status = "Completed"
                        };
                        _context.Payments.Add(payment);

                        booking.Status = "Paid";
                        _context.SaveChanges();

                        transaction.Commit();
                        _logger.LogInformation("Booking confirmed successfully: BookingId={BookingId}", booking.BookingId);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _logger.LogError(ex, "Error occurred while confirming booking: {Message}, InnerException: {InnerException}, StackTrace: {StackTrace}",
                            ex.Message, ex.InnerException?.Message, ex.StackTrace);
                        TempData["Error"] = "Có lỗi xảy ra khi hoàn tất đặt vé: " + ex.Message;
                        return RedirectToAction("ConfirmBooking");
                    }
                }

                HttpContext.Session.Remove("BookingConfirmationData");
                HttpContext.Session.Remove("PaymentStatus");
                HttpContext.Session.Remove("PaymentMethod");
                HttpContext.Session.Remove("QRCodeUrl");
                HttpContext.Session.Remove("BookingId");

                BackgroundJob.Schedule(() => SendBookingEmail(booking.BookingId), TimeSpan.FromMinutes(2));

                var confirmationViewModel = new BookingConfirmationViewModel
                {
                    BookingForm = model,
                    Flight = flight,
                    ReturnFlight = returnFlight,
                    Passengers = passengers,
                    TotalPrice = totalPrice,
                    UserId = userId,
                    IsBookingSuccessful = true
                };

                TempData["Success"] = "Đặt vé của bạn đã được hoàn tất. Bạn sẽ nhận được email xác nhận sau ít phút.";
                return View("ConfirmBooking", confirmationViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in CompleteBooking (POST): {Message}, InnerException: {InnerException}, StackTrace: {StackTrace}",
                    ex.Message, ex.InnerException?.Message, ex.StackTrace);
                TempData["Error"] = "Có lỗi xảy ra khi hoàn tất đặt vé: " + ex.Message;
                return RedirectToAction("ConfirmBooking");
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        public IActionResult CreateVNPayPayment(int bookingId)
        {
            _logger.LogInformation("CreateVNPayPayment called with BookingId: {BookingId}", bookingId);

            try
            {
                var booking = _context.Bookings.FirstOrDefault(b => b.BookingId == bookingId);
                if (booking == null)
                {
                    _logger.LogWarning("Booking not found: bookingId={BookingId}", bookingId);
                    TempData["Error"] = "Không tìm thấy thông tin đặt vé.";
                    return RedirectToAction("Index");
                }

                if (booking.Status == "Paid")
                {
                    _logger.LogWarning("Booking already paid: bookingId={BookingId}", bookingId);
                    TempData["Error"] = "Đặt vé này đã được thanh toán.";
                    return RedirectToAction("Index");
                }

                string vnp_TxnRef = bookingId.ToString();
                string vnp_OrderInfo = $"Thanh toan don hang {bookingId}";
                string vnp_Amount = ((int)(booking.TotalPrice * 100)).ToString();
                string vnp_CreateDate = DateTime.Now.ToString("yyyyMMddHHmmss");
                string vnp_IpAddr = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

                var vnp_Params = new Dictionary<string, string>
                {
                    { "vnp_Version", "2.1.0" },
                    { "vnp_Command", "pay" },
                    { "vnp_TmnCode", VNPAY_TMN_CODE },
                    { "vnp_Amount", vnp_Amount },
                    { "vnp_CreateDate", vnp_CreateDate },
                    { "vnp_CurrCode", "VND" },
                    { "vnp_IpAddr", vnp_IpAddr },
                    { "vnp_Locale", "vn" },
                    { "vnp_OrderInfo", vnp_OrderInfo },
                    { "vnp_OrderType", "250000" },
                    { "vnp_ReturnUrl", VNPAY_RETURN_URL },
                    { "vnp_TxnRef", vnp_TxnRef },
                    { "vnp_ExpireDate", DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss") }
                };

                string hashData = string.Join("&", vnp_Params.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                string vnp_SecureHash = HmacSHA512(VNPAY_HASH_SECRET, hashData);
                vnp_Params["vnp_SecureHash"] = vnp_SecureHash;

                string paymentUrl = VNPAY_URL + "?" + string.Join("&", vnp_Params.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

                _logger.LogInformation("VNPay Payment URL generated: {PaymentUrl}", paymentUrl);

                ViewBag.PaymentUrl = paymentUrl;
                ViewBag.BookingId = bookingId;
                return View("VNPayPayment");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateVNPayPayment: {Message}", ex.Message);
                TempData["Error"] = "Có lỗi xảy ra khi tạo thanh toán VNPay: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        public async Task<IActionResult> VNPayReturn()
        {
            _logger.LogInformation("VNPayReturn called with query: {QueryString}", Request.QueryString.ToString());

            try
            {
                var vnpayData = Request.Query;
                string vnp_SecureHash = vnpayData["vnp_SecureHash"];
                string vnp_TxnRef = vnpayData["vnp_TxnRef"];
                string vnp_ResponseCode = vnpayData["vnp_ResponseCode"];

                var vnp_Params = new Dictionary<string, string>();
                foreach (var key in vnpayData.Keys)
                {
                    if (key != "vnp_SecureHash")
                    {
                        vnp_Params[key] = vnpayData[key];
                    }
                }

                string hashData = string.Join("&", vnp_Params.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                string checkSum = HmacSHA512(VNPAY_HASH_SECRET, hashData);

                if (checkSum != vnp_SecureHash)
                {
                    _logger.LogWarning("Invalid checksum for VNPay return: {Vnp_SecureHash}", vnp_SecureHash);
                    TempData["Error"] = "Xác minh thanh toán không hợp lệ.";
                    return RedirectToAction("Index");
                }

                int bookingId = int.Parse(vnp_TxnRef);
                var booking = _context.Bookings.FirstOrDefault(b => b.BookingId == bookingId);
                if (booking == null)
                {
                    _logger.LogWarning("Booking not found: bookingId={BookingId}", bookingId);
                    TempData["Error"] = "Không tìm thấy thông tin đặt vé.";
                    return RedirectToAction("Index");
                }

                if (vnp_ResponseCode == "00")
                {
                    using (var transaction = _context.Database.BeginTransaction())
                    {
                        try
                        {
                            var payment = _context.Payments.FirstOrDefault(p => p.BookingId == bookingId);
                            if (payment != null)
                            {
                                payment.Status = "Completed";
                                payment.PaymentDate = DateTime.Now;
                            }
                            else
                            {
                                payment = new Payment
                                {
                                    BookingId = bookingId,
                                    PaymentMethod = "VNPayQR",
                                    Amount = booking.TotalPrice,
                                    PaymentDate = DateTime.Now,
                                    Status = "Completed"
                                };
                                _context.Payments.Add(payment);
                            }

                            booking.Status = "Paid";
                            await _context.SaveChangesAsync();
                            transaction.Commit();

                            BackgroundJob.Schedule(() => SendBookingEmail(bookingId), TimeSpan.FromMinutes(10));

                            TempData["Success"] = "Thanh toán thành công! Bạn sẽ nhận được email xác nhận sau 10 phút.";
                            return RedirectToAction("PaymentSuccess", new { bookingId });
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError(ex, "Error updating payment status: {Message}", ex.Message);
                            TempData["Error"] = "Có lỗi xảy ra khi cập nhật trạng thái thanh toán: " + ex.Message;
                            return RedirectToAction("Index");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Payment failed with response code: {ResponseCode}", vnp_ResponseCode);
                    TempData["Error"] = "Thanh toán không thành công. Vui lòng thử lại.";
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in VNPayReturn: {Message}", ex.Message);
                TempData["Error"] = "Có lỗi xảy ra khi xử lý thanh toán: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        [HttpPost]
        public async Task<IActionResult> VNPayIPN()
        {
            _logger.LogInformation("VNPayIPN called with query: {QueryString}", Request.QueryString.ToString());

            try
            {
                var vnpayData = Request.Query;
                string vnp_SecureHash = vnpayData["vnp_SecureHash"];
                string vnp_TxnRef = vnpayData["vnp_TxnRef"];
                string vnp_ResponseCode = vnpayData["vnp_ResponseCode"];

                var vnp_Params = new Dictionary<string, string>();
                foreach (var key in vnpayData.Keys)
                {
                    if (key != "vnp_SecureHash")
                    {
                        vnp_Params[key] = vnpayData[key];
                    }
                }

                string hashData = string.Join("&", vnp_Params.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                string checkSum = HmacSHA512(VNPAY_HASH_SECRET, hashData);

                if (checkSum != vnp_SecureHash)
                {
                    _logger.LogWarning("Invalid checksum for VNPay IPN: {Vnp_SecureHash}", vnp_SecureHash);
                    return Json(new { RspCode = "97", Message = "Invalid checksum" });
                }

                int bookingId = int.Parse(vnp_TxnRef);
                var booking = _context.Bookings.FirstOrDefault(b => b.BookingId == bookingId);
                if (booking == null)
                {
                    _logger.LogWarning("Booking not found: bookingId={BookingId}", bookingId);
                    return Json(new { RspCode = "01", Message = "Order not found" });
                }

                if (vnp_ResponseCode == "00")
                {
                    using (var transaction = _context.Database.BeginTransaction())
                    {
                        try
                        {
                            var payment = _context.Payments.FirstOrDefault(p => p.BookingId == bookingId);
                            if (payment != null)
                            {
                                payment.Status = "Completed";
                                payment.PaymentDate = DateTime.Now;
                            }
                            else
                            {
                                payment = new Payment
                                {
                                    BookingId = bookingId,
                                    PaymentMethod = "VNPayQR",
                                    Amount = booking.TotalPrice,
                                    PaymentDate = DateTime.Now,
                                    Status = "Completed"
                                };
                                _context.Payments.Add(payment);
                            }

                            booking.Status = "Paid";
                            await _context.SaveChangesAsync();
                            transaction.Commit();

                            _logger.LogInformation("IPN processed successfully for bookingId={BookingId}", bookingId);
                            return Json(new { RspCode = "00", Message = "Confirm Success" });
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError(ex, "Error processing IPN: {Message}", ex.Message);
                            return Json(new { RspCode = "99", Message = "Unknown error" });
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("IPN payment failed with response code: {ResponseCode}", vnp_ResponseCode);
                    return Json(new { RspCode = "24", Message = "Payment failed" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in VNPayIPN: {Message}", ex.Message);
                return Json(new { RspCode = "99", Message = "Unknown error" });
            }
        }

        [Authorize(Policy = "CustomerOnly")]
        public IActionResult PaymentSuccess(int bookingId)
        {
            ViewBag.BookingId = bookingId;
            return View();
        }

        [Authorize(Policy = "CustomerOnly")]
        public void SendBookingEmail(int bookingId)
        {
            try
            {
                var booking = _context.Bookings
                    .Include(b => b.Flight)
                        .ThenInclude(f => f.DepartureAirport)
                    .Include(b => b.Flight)
                        .ThenInclude(f => f.DestinationAirport)
                    .Include(b => b.ReturnFlight)
                        .ThenInclude(f => f.DepartureAirport)
                    .Include(b => b.ReturnFlight)
                        .ThenInclude(f => f.DestinationAirport)
                    .Include(b => b.Passengers)
                    .Include(b => b.Payment)
                    .FirstOrDefault(b => b.BookingId == bookingId);

                if (booking == null)
                {
                    _logger.LogWarning("Booking not found for sending email: bookingId={BookingId}", bookingId);
                    return;
                }

                if (string.IsNullOrEmpty(booking.ContactEmail) || !new System.Text.RegularExpressions.Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$").IsMatch(booking.ContactEmail))
                {
                    _logger.LogWarning("Invalid email address for bookingId={BookingId}: {Email}", bookingId, booking.ContactEmail);
                    return;
                }

                string orderCode = $"DViVu{bookingId}{new Random().Next(1000, 9999)}";
                string bookingCode = $"{booking.BookingDate:yyMMdd}-{new Random().Next(10, 99)}";

                string emailBody = $@"
THÔNG TIN ĐẶT VÉ VÀ XÁC NHẬN HÀNH TRÌNH

Mã đơn hàng: {orderCode}

Tình trạng: Hoàn tất

Thông tin đặt chỗ của quý khách đã được ghi nhận.
Xin trân trọng cảm ơn!

{booking.Flight.DepartureAirport.City} → {booking.Flight.DestinationAirport.City}
Mã đặt chỗ: {bookingCode}

Chuyến bay: {booking.Flight.Airline} {booking.Flight.FlightNumber}
Xuất phát: Sân bay {booking.Flight.DepartureAirport.Name}, {booking.Flight.DepartureTime:HH:mm}, {booking.Flight.DepartureTime:dd/MM/yy}
Điểm đến: Sân bay {booking.Flight.DestinationAirport.Name}, {booking.Flight.ArrivalTime:HH:mm}, {booking.Flight.ArrivalTime:dd/MM/yy}";

                if (booking.IsRoundTrip && booking.ReturnFlight != null)
                {
                    emailBody += $@"

{booking.ReturnFlight.DepartureAirport.City} → {booking.ReturnFlight.DestinationAirport.City}
Mã đặt chỗ: {bookingCode}

Chuyến bay: {booking.ReturnFlight.Airline} {booking.ReturnFlight.FlightNumber}
Xuất phát: Sân bay {booking.ReturnFlight.DepartureAirport.Name}, {booking.ReturnFlight.DepartureTime:HH:mm}, {booking.ReturnFlight.DepartureTime:dd/MM/yy}
Điểm đến: Sân bay {booking.ReturnFlight.DestinationAirport.Name}, {booking.ReturnFlight.ArrivalTime:HH:mm}, {booking.ReturnFlight.ArrivalTime:dd/MM/yy}";
                }

                emailBody += $@"

Chi Tiết Giá Vé
Hành khách: {string.Join(", ", booking.Passengers.Select(p => p.FullName))}
Hành lý: {(booking.Passengers.Any(p => p.LuggageFee > 0) ? string.Join(", ", booking.Passengers.Where(p => p.LuggageFee > 0).Select(p => $"{p.FullName}: {p.LuggageFee:N0} VNĐ")) : "Không có")}
Giá vé: {booking.Flight.Price:N0} VNĐ
Tổng giá: {booking.TotalPrice:N0} VNĐ

Thông Tin Thanh Toán
Hình thức thanh toán: {(booking.PaymentMethod == "Thẻ Tín Dụng" ? "VNPay QR" : "Thanh toán khi nhận vé")}

Thông Tin Liên Hệ
Họ tên: {booking.ContactName}
Số điện thoại: {booking.ContactPhone}
Email: {booking.ContactEmail}
Yêu cầu đặc biệt: {(string.IsNullOrEmpty(booking.ContactEmail) ? "Không có" : "N/A")}";

                var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential("lluat91@gmail.com", "giom mcgu lulb bksi"),
                    EnableSsl = true,
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress("lluat91@gmail.com"),
                    Subject = "THÔNG TIN ĐẶT VÉ VÀ XÁC NHẬN HÀNH TRÌNH",
                    Body = emailBody,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(booking.ContactEmail);

                smtpClient.Send(mailMessage);
                _logger.LogInformation("Email sent successfully to {Email} for bookingId={BookingId}", booking.ContactEmail, bookingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email for bookingId={BookingId}: {Message}", bookingId, ex.Message);
            }
        }
        [Authorize(Policy = "CustomerOnly")]
        private string HmacSHA512(string key, string data)
        {
            try
            {
                var keyBytes = Encoding.UTF8.GetBytes(key);
                var dataBytes = Encoding.UTF8.GetBytes(data);
                using (var hmac = new HMACSHA512(keyBytes))
                {
                    var hash = hmac.ComputeHash(dataBytes);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating HMAC SHA512: {Message}", ex.Message);
                throw;
            }
        }
        [Authorize(Policy = "CustomerOnly")]
        public IActionResult Privacy()
        {
            return View();
        }


    }

    public class CreditCardPaymentRequest
    {
        public string CardNumber { get; set; }
        public string CardHolder { get; set; }
        public string ExpiryDate { get; set; }
        public string Cvv { get; set; }
    }

    public class VNPayPaymentRequest
    {
        public decimal Amount { get; set; }
    }
}
