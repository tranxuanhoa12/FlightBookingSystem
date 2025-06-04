using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlightBookingApp.Data;
using FlightBookingApp.Models;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using BCrypt.Net;
using FlightBookingApp.Services;
using System;

namespace FlightBookingApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;

        public AccountController(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View(new LoginViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (model == null)
            {
                Console.WriteLine("Mô hình LoginViewModel là null");
                return BadRequest("Mô hình là null");
            }

            Console.WriteLine($"Email: {model.Email}");
            Console.WriteLine($"Password: {model.Password}");

            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Console.WriteLine($"Lỗi xác thực: {error.ErrorMessage}");
                }
                return View(model);
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null)
            {
                TempData["Error"] = "Email không tồn tại.";
                return View(model);
            }

            if (!BCrypt.Net.BCrypt.Verify(model.Password, user.Password))
            {
                Console.WriteLine($"Mật khẩu không đúng cho UserId: {user.UserId}");
                TempData["Error"] = "Mật khẩu không đúng.";
                return View(model);
            }

            // Kiểm tra vai trò: chỉ cho phép Customer đăng nhập
            if (user.Role != "Customer")
            {
                Console.WriteLine($"Người dùng không phải là Customer: Role={user.Role}");
                TempData["Error"] = "Tài khoản này không được phép đăng nhập tại đây.";
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var claimsIdentity = new ClaimsIdentity(claims, "CustomerCookieAuth");
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync("CustomerCookieAuth", claimsPrincipal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            });

            TempData["Success"] = "Đăng nhập thành công!";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (model == null)
            {
                Console.WriteLine("Mô hình RegisterViewModel là null");
                return BadRequest("Mô hình là null");
            }

            Console.WriteLine($"FullName: {model.FullName}");
            Console.WriteLine($"Email: {model.Email}");
            Console.WriteLine($"PhoneNumber: {model.PhoneNumber}");
            Console.WriteLine($"Password: {model.Password}");
            Console.WriteLine($"ConfirmPassword: {model.ConfirmPassword}");

            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Console.WriteLine($"Lỗi xác thực: {error.ErrorMessage}");
                }
                return View(model);
            }

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (existingUser != null)
            {
                TempData["Error"] = "Email đã được sử dụng.";
                return View(model);
            }

            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(model.Password);

            var user = new Users
            {
                FullName = model.FullName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                Password = hashedPassword,
                Role = "Customer"
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Đăng ký thành công! Vui lòng đăng nhập.";
            return RedirectToAction("Login");
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (model == null)
            {
                Console.WriteLine("Mô hình ForgotPasswordViewModel là null");
                return BadRequest("Mô hình là null");
            }

            if (!ModelState.IsValid)
            {
                return View("Login", new LoginViewModel());
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null)
            {
                TempData["Error"] = "Email không tồn tại.";
                return View("Login", new LoginViewModel());
            }

            if (string.IsNullOrEmpty(model.VerificationCode))
            {
                Random random = new Random();
                string verificationCode = random.Next(10000, 99999).ToString();

                var resetToken = new PasswordResetToken
                {
                    Email = model.Email,
                    Token = verificationCode,
                    ExpiryDate = DateTime.UtcNow.AddMinutes(15)
                };

                var oldToken = await _context.PasswordResetTokens
                    .FirstOrDefaultAsync(t => t.Email == model.Email);
                if (oldToken != null)
                {
                    _context.PasswordResetTokens.Remove(oldToken);
                }

                _context.PasswordResetTokens.Add(resetToken);
                await _context.SaveChangesAsync();

                string subject = "Mã xác nhận để đặt lại mật khẩu";
                string body = $"Mã xác nhận của bạn là: <strong>{verificationCode}</strong>. Mã này có hiệu lực trong 15 phút.";
                _emailService.SendEmail(model.Email, subject, body);

                TempData["Success"] = "Mã xác nhận đã được gửi đến email của bạn. Vui lòng nhập mã để tiếp tục.";
                TempData["ShowCodeInput"] = true;
                TempData["Email"] = model.Email;
                return View("Login", new LoginViewModel());
            }
            else
            {
                var resetToken = await _context.PasswordResetTokens
                    .FirstOrDefaultAsync(t => t.Email == model.Email && t.Token == model.VerificationCode);

                if (resetToken == null)
                {
                    TempData["Error"] = "Mã xác nhận không hợp lệ.";
                    TempData["ShowCodeInput"] = true;
                    TempData["Email"] = model.Email;
                    return View("Login", new LoginViewModel());
                }

                if (resetToken.ExpiryDate < DateTime.UtcNow)
                {
                    TempData["Error"] = "Mã xác nhận đã hết hạn. Vui lòng yêu cầu mã mới.";
                    TempData["ShowCodeInput"] = true;
                    TempData["Email"] = model.Email;
                    return View("Login", new LoginViewModel());
                }

                TempData["ShowResetPasswordForm"] = true;
                TempData["Email"] = model.Email;
                return View("Login", new LoginViewModel());
            }
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (model == null)
            {
                Console.WriteLine("Mô hình ResetPasswordViewModel là null");
                return BadRequest("Mô hình là null");
            }

            if (!ModelState.IsValid)
            {
                TempData["ShowResetPasswordForm"] = true;
                TempData["Email"] = model.Email;
                return View("Login", new LoginViewModel());
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null)
            {
                TempData["Error"] = "Email không tồn tại.";
                return View("Login", new LoginViewModel());
            }

            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            user.Password = hashedPassword;

            var resetToken = await _context.PasswordResetTokens
                .FirstOrDefaultAsync(t => t.Email == model.Email);
            if (resetToken != null)
            {
                _context.PasswordResetTokens.Remove(resetToken);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Mật khẩu đã được đặt lại thành công. Vui lòng đăng nhập.";
            return View("Login", new LoginViewModel());
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("CustomerCookieAuth");
            TempData["Success"] = "Đăng xuất thành công!";
            return RedirectToAction("Index", "Home");
        }
        [Authorize(Policy = "CustomerOnly")]
        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            Console.WriteLine("Profile (GET) called.");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                Console.WriteLine("UserId claim is missing.");
                TempData["Error"] = "Không thể xác định người dùng. Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }

            int userId;
            try
            {
                userId = int.Parse(userIdClaim);
                Console.WriteLine($"UserId retrieved: {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse UserId from claim: {userIdClaim}. Error: {ex.Message}");
                TempData["Error"] = "Không thể xác định người dùng. Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }

            var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
            if (user == null)
            {
                Console.WriteLine($"User not found for UserId: {userId}");
                TempData["Error"] = "Không tìm thấy thông tin người dùng.";
                return RedirectToAction("Login");
            }
            Console.WriteLine($"User found: UserId={user.UserId}, FullName={user.FullName}, Email={user.Email}, PhoneNumber={user.PhoneNumber}, Role={user.Role}");

            return View(user);
        }
        [Authorize(Policy = "CustomerOnly")]
        [Authorize]
        [HttpGet]
        public IActionResult EditProfile()
        {
            Console.WriteLine("EditProfile (GET) called.");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                Console.WriteLine("UserId claim is missing.");
                TempData["Error"] = "Không thể xác định người dùng. Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }

            int userId;
            try
            {
                userId = int.Parse(userIdClaim);
                Console.WriteLine($"UserId retrieved: {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse UserId from claim: {userIdClaim}. Error: {ex.Message}");
                TempData["Error"] = "Không thể xác định người dùng. Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }

            var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
            if (user == null)
            {
                Console.WriteLine($"User not found for UserId: {userId}");
                TempData["Error"] = "Không tìm thấy thông tin người dùng.";
                return RedirectToAction("Login");
            }
            Console.WriteLine($"User found: UserId={user.UserId}, FullName={user.FullName}, Email={user.Email}, PhoneNumber={user.PhoneNumber}, Role={user.Role}");

            return View(user);
        }
        [Authorize(Policy = "CustomerOnly")]
        [Authorize]
        [HttpPost]
        public IActionResult EditProfile(Users model)
        {
            Console.WriteLine("EditProfile (POST) called.");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("Model state is invalid.");
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Console.WriteLine($"Validation error: {error.ErrorMessage}");
                }
                return View(model);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                Console.WriteLine("UserId claim is missing.");
                TempData["Error"] = "Không thể xác định người dùng. Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }

            int userId = int.Parse(userIdClaim);
            var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
            if (user == null)
            {
                Console.WriteLine($"User not found for UserId: {userId}");
                TempData["Error"] = "Không tìm thấy thông tin người dùng.";
                return RedirectToAction("Login");
            }

            var existingUserWithEmail = _context.Users
                .FirstOrDefault(u => u.Email == model.Email && u.UserId != userId);
            if (existingUserWithEmail != null)
            {
                ModelState.AddModelError("Email", "Email đã được sử dụng bởi người dùng khác.");
                return View(model);
            }

            user.FullName = model.FullName;
            user.PhoneNumber = model.PhoneNumber;
            user.Email = model.Email;

            if (!string.IsNullOrEmpty(model.Password))
            {
                user.Password = BCrypt.Net.BCrypt.HashPassword(model.Password);
            }

            _context.Users.Update(user);
            _context.SaveChanges();

            Console.WriteLine($"User updated: UserId={user.UserId}, FullName={user.FullName}, Email={user.Email}, PhoneNumber={user.PhoneNumber}");
            TempData["Success"] = "Cập nhật thông tin thành công!";
            return RedirectToAction("Profile");
        }
    }
}