using System.Net.Mail;
using System.Net;
using FlightBookingApp.Data;
using FlightBookingApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks; // Thêm namespace này

namespace FlightBookingApp.Services
{
    public class EmailService
    {
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly ApplicationDbContext _context;

        public EmailService(string smtpServer, int smtpPort, string smtpUsername, string smtpPassword, ApplicationDbContext context)
        {
            _smtpServer = smtpServer;
            _smtpPort = smtpPort;
            _smtpUsername = smtpUsername;
            _smtpPassword = smtpPassword;
            _context = context;
        }

        // Phương thức đồng bộ (giữ nguyên)
        public void SendEmail(string toEmail, string subject, string body)
        {
            try
            {
                using (var smtpClient = new SmtpClient(_smtpServer)
                {
                    Port = _smtpPort,
                    Credentials = new NetworkCredential(_smtpUsername, _smtpPassword),
                    EnableSsl = true,
                })
                {
                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpUsername),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true, // Cho phép HTML trong email
                    };
                    mailMessage.To.Add(toEmail);

                    smtpClient.Send(mailMessage);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send email to {toEmail}: {ex.Message}", ex);
            }
        }

        // Phương thức bất đồng bộ (mới)
        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                using (var smtpClient = new SmtpClient(_smtpServer)
                {
                    Port = _smtpPort,
                    Credentials = new NetworkCredential(_smtpUsername, _smtpPassword),
                    EnableSsl = true,
                })
                {
                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(_smtpUsername),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true, // Cho phép HTML trong email
                    };
                    mailMessage.To.Add(toEmail);

                    await smtpClient.SendMailAsync(mailMessage);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send email to {toEmail}: {ex.Message}", ex);
            }
        }

        public void SendBookingEmail(int bookingId)
        {
            var booking = _context.Bookings
                .Include(b => b.Flight).ThenInclude(f => f.DepartureAirport)
                .Include(b => b.Flight).ThenInclude(f => f.DestinationAirport)
                .Include(b => b.ReturnFlight).ThenInclude(f => f.DepartureAirport)
                .Include(b => b.ReturnFlight).ThenInclude(f => f.DestinationAirport)
                .Include(b => b.Passengers)
                .Include(b => b.Payment)
                .FirstOrDefault(b => b.BookingId == bookingId);

            if (booking == null)
            {
                throw new Exception($"Booking not found for sending email: bookingId={bookingId}");
            }

            // Tạo mã đơn hàng
            string orderCode = $"DAVIA{bookingId}{new Random().Next(1000, 9999)}";
            string bookingCode = $"{booking.BookingDate:yyMMdd}-{new Random().Next(10, 99)}";

            // Tạo nội dung email
            string emailBody = $@"
THÔNG TIN ĐẶT VÉ VÀ XÁC NHẬN HÀNH TRÌNH

Mã đơn hàng: {orderCode}

Tình trạng: Lợi

Thông tin đặt chỗ của quý khách đã được ghi nhận.
Nhân viên CSKH của Avia sẽ liên hệ với quý khách để xác nhận đơn hàng.
Xin trân trọng cảm ơn!

{booking.Flight.DepartureAirport.City} → {booking.Flight.DestinationAirport.City}
Mã đặt chỗ: {bookingCode}

Chuyến bay: {booking.Flight.Airline} {booking.Flight.FlightNumber}
Xuất phát: Sân bay {booking.Flight.DepartureAirport.Name}, {booking.Flight.DepartureTime:HH:mm}, {booking.Flight.DepartureTime:dd/MM/yy}
Điểm đến: Sân bay {booking.Flight.DestinationAirport.Name}, {booking.Flight.ArrivalTime:HH:mm}, {booking.Flight.ArrivalTime:dd/MM/yy}

Chi Tiết Giá Vé
Hành khách: {string.Join(", ", booking.Passengers.Select(p => p.FullName))}
Hành lý: {(booking.Passengers.Any(p => p.LuggageFee > 0) ? string.Join(", ", booking.Passengers.Where(p => p.LuggageFee > 0).Select(p => $"{p.FullName}: {p.LuggageFee:N0} VNĐ")) : "Không có")}
Giá vé: {booking.TotalPrice:N0} VNĐ
Tổng giá: {booking.TotalPrice:N0} VNĐ

Thông Tin Thanh Toán
Hình thức thanh toán: {(booking.Payment.PaymentMethod == "VNPayQR" ? "VNPay QR" : "Thanh toán khi nhận vé")}

Thông Tin Liên Hệ
Họ tên: {booking.ContactName}
Số điện thoại: {booking.ContactPhone}
Email: {booking.ContactEmail}
Yêu cầu đặc biệt: {(string.IsNullOrEmpty(booking.ContactEmail) ? "Không có" : "N/A")}";

            SendEmail(booking.ContactEmail, "THÔNG TIN ĐẶT VÉ VÀ XÁC NHẬN HÀNH TRÌNH", emailBody);
        }
    }
}