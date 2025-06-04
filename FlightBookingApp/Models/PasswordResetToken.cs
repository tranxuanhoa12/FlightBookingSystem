using System.ComponentModel.DataAnnotations;

namespace FlightBookingApp.Models
{
    public class PasswordResetToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Email { get; set; }

        [Required]
        public string Token { get; set; }

        public DateTime ExpiryDate { get; set; }
    }
}