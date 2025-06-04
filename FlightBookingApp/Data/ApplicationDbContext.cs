using Microsoft.EntityFrameworkCore;
using FlightBookingApp.Models;
using System.Collections.Generic;

namespace FlightBookingApp.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Airport> Airports { get; set; }
        public DbSet<Flight> Flights { get; set; }
        public DbSet<FlightPriceHistory> FlightPriceHistory { get; set; }
        public DbSet<Users> Users { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Passenger> Passengers { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<WebsiteVisit> WebsiteVisits { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Explicitly configure the primary key for FlightPriceHistory
            modelBuilder.Entity<FlightPriceHistory>()
                .HasKey(fph => fph.PriceHistoryId);

            // Configure relationships for FlightPriceHistory
            modelBuilder.Entity<FlightPriceHistory>()
                .HasOne(fph => fph.Flight)
                .WithMany()
                .HasForeignKey(fph => fph.FlightId);

            // Configure precision for decimal properties
            modelBuilder.Entity<Booking>()
                .Property(b => b.TotalPrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Flight>()
                .Property(f => f.Price)
                .HasPrecision(18, 2);

            modelBuilder.Entity<FlightPriceHistory>()
                .Property(f => f.Price)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Payment>()
                .Property(p => p.Amount)
                .HasPrecision(18, 2);

            // Configure relationships for Booking
            modelBuilder.Entity<Booking>()
                .HasOne(b => b.User)
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Booking>()
                .HasOne(b => b.Flight)
                .WithMany()
                .HasForeignKey(b => b.FlightId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Booking>()
                .HasOne(b => b.ReturnFlight)
                .WithMany()
                .HasForeignKey(b => b.ReturnFlightId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure relationship between Flight and Airports
            modelBuilder.Entity<Flight>()
                .HasOne(f => f.DepartureAirport)
                .WithMany()
                .HasForeignKey(f => f.DepartureAirportId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Flight>()
                .HasOne(f => f.DestinationAirport)
                .WithMany()
                .HasForeignKey(f => f.DestinationAirportId) // Sử dụng DestinationAirportId để khớp với Flight.cs
                .OnDelete(DeleteBehavior.Restrict);

            // Configure relationship between Booking and Passenger (1-nhiều)
            modelBuilder.Entity<Passenger>()
                .HasOne(p => p.Booking)
                .WithMany(b => b.Passengers)
                .HasForeignKey(p => p.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure relationship between Booking and Payment (1-1)
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Booking)
                .WithOne(b => b.Payment)
                .HasForeignKey<Payment>(p => p.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure relationship between Booking and Invoice (1-nhiều)
            modelBuilder.Entity<Invoice>()
                .HasOne(i => i.Booking)
                .WithMany(b => b.Invoices)
                .HasForeignKey(i => i.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Users table
            modelBuilder.Entity<Users>().ToTable("Users");
            modelBuilder.Entity<Users>().HasKey(u => u.UserId);
            modelBuilder.Entity<Passenger>()
                        .Property(p => p.LuggageFee)
                        .HasColumnType("decimal(18,2)");
            base.OnModelCreating(modelBuilder);
        }
    }
}