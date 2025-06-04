using System.ComponentModel.DataAnnotations;

namespace FlightBookingApp.Models
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Email là bắt buộc")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; }

        public string? VerificationCode { get; set; }
    }
}