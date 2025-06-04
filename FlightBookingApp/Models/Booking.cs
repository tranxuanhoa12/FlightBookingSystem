using System;
using System.Collections.Generic;

namespace FlightBookingApp.Models
{
    public class Booking
    {
        public int BookingId { get; set; }
        public int UserId { get; set; }
        public int FlightId { get; set; }
        public int? ReturnFlightId { get; set; }
        public bool IsRoundTrip { get; set; }
        public int PassengerCount { get; set; }
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }
        public string ContactName { get; set; }
        public string ContactGender { get; set; }
        public DateTime BookingDate { get; set; }
        public string Status { get; set; }
        public decimal TotalPrice { get; set; }
        public string PaymentMethod { get; set; }

        public Users User { get; set; }
        public Flight Flight { get; set; }
        public Flight ReturnFlight { get; set; }
        public ICollection<Passenger> Passengers { get; set; } = new List<Passenger>();
        public Payment Payment { get; set; }
        public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    }
}