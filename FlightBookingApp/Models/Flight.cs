using FlightBookingApp.Models;

namespace FlightBookingApp.Models
{
    public class Flight
    {
        public int FlightId { get; set; }
        public string FlightNumber { get; set; }
        public int DepartureAirportId { get; set; }
        public Airport DepartureAirport { get; set; } // Navigation property
        public int DestinationAirportId { get; set; }
        public Airport DestinationAirport { get; set; } // Navigation property
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string Airline { get; set; }
        public decimal Price { get; set; }
        public int AvailableSeats { get; set; }
        public int Stops { get; set; }
        public string Status { get; set; }
    }
}