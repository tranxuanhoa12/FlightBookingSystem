using System.ComponentModel.DataAnnotations;

namespace FlightBookingApp.Models
{
    public class Passenger
    {
        public int PassengerId { get; set; }
        public int BookingId { get; set; }
        public string FullName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string IdType { get; set; }
        public DateTime? IdExpiry { get; set; }
        public string IdCountry { get; set; }
        public string Nationality { get; set; }
        public decimal? LuggageFee { get; set; }

        public Booking Booking { get; set; }
    }
}