using System.ComponentModel.DataAnnotations;

namespace FlightBookingApp.Models
{
    public class FlightPriceHistory
    {
        [Key]
        public int PriceHistoryId { get; set; } // Primary key
        public int FlightId { get; set; }
        public decimal Price { get; set; }
        public DateTime RecordedDate { get; set; }

        // Navigation property
        public Flight Flight { get; set; }
    }
}
