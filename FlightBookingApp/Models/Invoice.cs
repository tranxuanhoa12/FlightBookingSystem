using System.ComponentModel.DataAnnotations;

namespace FlightBookingApp.Models
{
    public class Invoice
    {
        public int InvoiceId { get; set; }

        [Required]
        public int BookingId { get; set; }

        public string CompanyName { get; set; }

        public string CompanyAddress { get; set; }

        public string CompanyCity { get; set; }

        public string TaxCode { get; set; }

        public string InvoiceRecipient { get; set; }

        public string InvoicePhone { get; set; }

        public string InvoiceEmail { get; set; }

        public Booking Booking { get; set; }
    }
}