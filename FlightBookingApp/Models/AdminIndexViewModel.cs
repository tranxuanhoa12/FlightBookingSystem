namespace FlightBookingApp.Models
{
    public class AdminIndexViewModel
    {
        // Thống kê lượt truy cập
        public int TotalVisits { get; set; }
        public int VisitsToday { get; set; }
        public int VisitsThisMonth { get; set; }

        // Tổng tiền
        public decimal TotalRevenueToday { get; set; }
        public decimal TotalRevenueThisMonth { get; set; }

        // Tổng số vé
        public int TotalTicketsToday { get; set; }
        public int TotalTicketsThisMonth { get; set; }

        // Tổng số chuyến bay
        public int TotalFlightsToday { get; set; }
        public int TotalFlightsThisMonth { get; set; }
        public int CompletedFlights { get; set; }
        public int ActiveFlights { get; set; }
        public int CanceledFlights { get; set; }
        public decimal TotalRevenue { get; set; }

        // Thống kê số vé bán ra theo ngày trong tuần
        public Dictionary<string, int> TicketSalesThisWeek { get; set; } // Key: Ngày (Sun, Mon, ...), Value: Số vé

        // Thống kê lịch trình chuyến bay trong 8 tháng gần nhất
        public Dictionary<string, int> FlightScheduleLast8Months { get; set; } // Key: Tháng (Jan, Feb, ...), Value: Số chuyến bay

        // Tỷ lệ các hãng hàng không phổ biến
        public Dictionary<string, double> PopularAirlines { get; set; } // Key: Tên hãng, Value: Tỷ lệ phần trăm

        // Điểm đến phổ biến
        public Dictionary<string, double> PopularDestinations { get; set; } // Key: Tên quốc gia, Value: Tỷ lệ phần trăm

        // Danh sách các hãng hàng không
        public List<AirlineStats> Airlines { get; set; }

        // Danh sách các tuyến bay hàng đầu
        public List<FlightRouteStats> TopFlightRoutes { get; set; }
    }
  
    public class AirlineStats
    {
        public string Name { get; set; }
        public int TotalFlights { get; set; }
        public DateTime NextFlightTime { get; set; }
    }

    public class FlightRouteStats
    {
        public string Route { get; set; } // Ví dụ: "Paris (CDG) - New York (JFK)"
        public double Distance { get; set; } // Khoảng cách (km)
        public TimeSpan Duration { get; set; } // Thời gian bay
        public int TotalPassengers { get; set; } // Tổng số hành khách
    }
    public class FlightSearchRequest
    {
        public string FlightNumber { get; set; }
    }
    public class TestEmailRequest
    {
        public string Email { get; set; }
    }
}