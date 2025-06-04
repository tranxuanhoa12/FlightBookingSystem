using FlightBookingApp.Models;

namespace FlightBookingApp.ViewModel
{
    public class BookingConfirmationViewModel
    {
        public BookingFormViewModel BookingForm { get; set; }
        public FlightBookingApp.Models.Flight Flight { get; set; }
        public FlightBookingApp.Models.Flight ReturnFlight { get; set; }
        public List<PassengerDetails> Passengers { get; set; } = new List<PassengerDetails>();
        public decimal TotalPrice { get; set; }
        public int UserId { get; set; } // Thêm UserId
        public string PaymentMethod { get; set; }
        public bool IsBookingSuccessful { get; set; }
        public int BookingId { get; set; }
    }

    public class PassengerDetails
    {
        public string FullName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string IdType { get; set; }
        public DateTime? IdExpiry { get; set; }
        public string IdCountry { get; set; }
        public string Nationality { get; set; }
        public decimal LuggageFee { get; set; }
    }
    // POST: Home/CompleteBooking
    public class CompleteBookingViewModel
    {
        public int FlightId { get; set; }
        public int? ReturnFlightId { get; set; }
        public decimal TotalPrice { get; set; }
        public string ContactName { get; set; }
        public string ContactPhone { get; set; }
        public string ContactEmail { get; set; }
        public string ContactGender { get; set; }
        public bool IsRoundTrip { get; set; }
        public int PassengerCount { get; set; }
        public List<PassengerDetails> Passengers { get; set; } = new List<PassengerDetails>();
        public string PaymentMethod { get; set; }
    }
    public class ManageBookingsViewModel
    {
        public List<BookingViewModel> Bookings { get; set; } = new List<BookingViewModel>();
        
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class BookingViewModel
    {
        public int BookingId { get; set; }
        public int UserId { get; set; }
        public string ContactName { get; set; }
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }
        public string ContactGender { get; set; }
        public FlightViewModel OutboundFlight { get; set; }
        public FlightViewModel ReturnFlight { get; set; }
        public bool IsRoundTrip { get; set; }
        public List<PassengerViewModel> Passengers { get; set; }
        public decimal TotalPrice { get; set; }
        public string PaymentStatus { get; set; }
        public DateTime BookingDate { get; set; }
        public bool InvoiceRequest { get; set; }
        public string CompanyName { get; set; }
        public string CompanyAddress { get; set; }
        public string CompanyCity { get; set; }
        public string TaxCode { get; set; }
        public string InvoiceRecipient { get; set; }
        public string InvoicePhone { get; set; }
        public string InvoiceEmail { get; set; }
    }

    public class FlightViewModel
    {
        public int FlightId { get; set; }
        public string Airline { get; set; }
        public string FlightNumber { get; set; }
        public AirportViewModel DepartureAirport { get; set; }
        public AirportViewModel DestinationAirport { get; set; }
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public decimal Price { get; set; }
    }

    public class AirportViewModel
    {
        public string IataCode { get; set; }
        public string City { get; set; }
    }

    public class PassengerViewModel
    {
        public string FullName { get; set; }
        public string Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Nationality { get; set; }
        public string IdType { get; set; }
        public DateTime? IdExpiry { get; set; }
        public string IdCountry { get; set; }
        public decimal LuggageFee { get; set; }
        public bool IsAdult { get; set; }
    }
    public class ManageFlightsViewModel
    {
        public List<Flight> Flights { get; set; } = new List<Flight>();
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
}