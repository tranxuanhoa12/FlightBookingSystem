using FlightBookingApp.Data;
using FlightBookingApp.Models;
using FlightBookingApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure DbContext with SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add HttpClient for FlightDataService
builder.Services.AddHttpClient();

// Load SMTP settings from appsettings.json
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));

// Register EmailService with dependency injection and validate SMTP settings
builder.Services.AddTransient<EmailService>(sp =>
{
    var smtpSettings = sp.GetRequiredService<IOptions<SmtpSettings>>().Value;
    var context = sp.GetRequiredService<ApplicationDbContext>();

    if (string.IsNullOrEmpty(smtpSettings.Server))
        throw new InvalidOperationException("SMTP Server is not configured in appsettings.json.");
    if (smtpSettings.Port <= 0)
        throw new InvalidOperationException("SMTP Port must be a positive non-zero value in appsettings.json.");
    if (string.IsNullOrEmpty(smtpSettings.Username))
        throw new InvalidOperationException("SMTP Username is not configured in appsettings.json.");
    if (string.IsNullOrEmpty(smtpSettings.Password))
        throw new InvalidOperationException("SMTP Password is not configured in appsettings.json.");

    return new EmailService(
        smtpSettings.Server,
        smtpSettings.Port,
        smtpSettings.Username,
        smtpSettings.Password,
        context
    );
});
builder.Services.AddHttpContextAccessor();
// Register FlightDataService with all required dependencies
builder.Services.AddScoped<FlightDataService>();

// Add Hangfire services
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new SqlServerStorageOptions
        {
            CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
            SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
            QueuePollInterval = TimeSpan.Zero,
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks = true
        }));

// Add Hangfire server
builder.Services.AddHangfireServer();

// Add Authentication and Authorization
builder.Services.AddAuthentication()
    .AddCookie("AdminCookieAuth", options =>
    {
        options.LoginPath = "/Admin/Login";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(500);
        options.Cookie.Name = "AdminAuthCookie";
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect("/Admin/Login");
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.Redirect("/Account/AccessDenied");
            return Task.CompletedTask;
        };
    })
    .AddCookie("CustomerCookieAuth", options =>
    {
        options.LoginPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.Cookie.Name = "CustomerAuthCookie";
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect("/Account/Login");
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.Redirect("/Account/AccessDenied");
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("Admin")
        .AddAuthenticationSchemes("AdminCookieAuth"));
    options.AddPolicy("CustomerOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("Customer")
        .AddAuthenticationSchemes("CustomerCookieAuth"));
});

// Thêm dịch vụ Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(500);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Tắt Browser Link và Browser Refresh
builder.Services.AddRazorPages().AddViewOptions(options =>
{
    options.HtmlHelperOptions.ClientValidationEnabled = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers["BrowserLink-Enabled"] = "false";
        context.Response.Headers["AspNetCore-BrowserRefresh-Enabled"] = "false";
        await next();
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Thêm middleware Session
app.UseSession();

// Middleware để ghi lại lượt truy cập
app.Use(async (context, next) =>
{
    // Chỉ ghi lại các request đến các action (bỏ qua các file tĩnh như CSS, JS, hình ảnh)
    var path = context.Request.Path.Value?.ToLower();
    if (!string.IsNullOrEmpty(path) &&
        !path.StartsWith("/lib/") && // Bỏ qua thư mục lib (thường chứa JS, CSS)
        !path.StartsWith("/css/") && // Bỏ qua CSS
        !path.StartsWith("/js/") &&  // Bỏ qua JS
        !path.StartsWith("/images/") && // Bỏ qua hình ảnh
        !path.EndsWith(".css") &&
        !path.EndsWith(".js") &&
        !path.EndsWith(".png") &&
        !path.EndsWith(".jpg") &&
        !path.EndsWith(".jpeg") &&
        !path.EndsWith(".gif"))
    {
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.WebsiteVisits.Add(new WebsiteVisit
            {
                VisitDate = DateTime.Now
            });
            await dbContext.SaveChangesAsync();
        }
    }

    await next();
});

// Sử dụng middleware xác thực tùy chỉnh để chọn scheme dựa trên đường dẫn
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Admin"))
    {
        var authService = context.RequestServices.GetRequiredService<IAuthenticationService>();
        var result = await authService.AuthenticateAsync(context, "AdminCookieAuth");
        if (result?.Principal != null)
        {
            context.User = result.Principal;
        }
    }
    else
    {
        var authService = context.RequestServices.GetRequiredService<IAuthenticationService>();
        var result = await authService.AuthenticateAsync(context, "CustomerCookieAuth");
        if (result?.Principal != null)
        {
            context.User = result.Principal;
        }
    }
    await next();
});

app.UseAuthorization();

// Use Hangfire Dashboard and Server
app.UseHangfireDashboard();
app.UseHangfireServer();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();