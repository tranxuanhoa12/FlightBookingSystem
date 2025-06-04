using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using FlightBookingApp.Data;
using FlightBookingApp.Models;
using System.IO;

namespace FlightBookingApp.Services
{
    public class FlightDataService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ApplicationDbContext _context;
        private const int Limit = 3000;
        private readonly Random _random = new Random();
        private const double AverageSpeedKmh = 800;
        private readonly Dictionary<string, (string Name, string City, string Country)> _airportData;

        private readonly Dictionary<string, decimal> _airlinePriceFactors = new Dictionary<string, decimal>
        {
            { "Vietnam Airlines", 1.2m }, { "Vietjet Air", 0.9m }, { "Bamboo Airways", 1.0m },
            { "Delta Air Lines", 1.3m }, { "American Airlines", 1.4m }, { "Emirates", 1.5m },
            { "Unknown Airline", 1.0m }
        };

        public FlightDataService(IConfiguration configuration, HttpClient httpClient, ApplicationDbContext context)
        {
            _configuration = configuration;
            _httpClient = httpClient;
            _context = context;
            _airportData = LoadAirportData();
        }

        private Dictionary<string, (string Name, string City, string Country)> LoadAirportData()
        {
            var airportMap = new Dictionary<string, (string Name, string City, string Country)>();
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Data", "airports.dat");
            if (!File.Exists(path))
            {
                Console.WriteLine($"File airports.dat not found at {path}");
                return airportMap;
            }

            var lines = File.ReadAllLines(path);
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length >= 5)
                {
                    string iata = parts[4].Trim('"');
                    string name = parts[1].Trim('"');
                    string city = parts[2].Trim('"');
                    string country = parts[3].Trim('"');
                    if (iata != "\\N" && !string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(country) && !airportMap.ContainsKey(iata))
                    {
                        string normalizedCity = iata switch
                        {
                            "HAN" => "Hà Nội",
                            "DAD" => "Đà Nẵng",
                            "SGN" => "TP. Hồ Chí Minh",
                            "DAL" => "Đà Lạt",
                            "NHA" => "Nha Trang",
                            "HUI" => "Huế",
                            "VII" => "Vinh",
                            "PQC" => "Phú Quốc",
                            "CXR" => "Cam Ranh",
                            "VCA" => "Cần Thơ",
                            "DIN" => "Điện Biên Phủ",
                            "VDO" => "Vân Đồn",
                            "HPH" => "Hải Phòng",
                            "UIH" => "Quy Nhơn",
                            "TBB" => "Tuy Hòa",
                            "BMV" => "Buôn Ma Thuột",
                            "CAH" => "Cà Mau",
                            "VCS" => "Côn Đảo",
                            "VCL" => "Chu Lai",
                            "THD" => "Thanh Hóa",
                            _ => city
                        };
                        string normalizedCountry = country; // Giữ nguyên từ airports.dat, nhưng có thể chuẩn hóa nếu cần
                        airportMap[iata] = (name, normalizedCity, normalizedCountry);
                    }
                }
            }
            Console.WriteLine($"Loaded {airportMap.Count} airports from airports.dat");
            return airportMap;
        }

        public async Task FetchAndSaveAirportDataAsync()
        {
            Console.WriteLine("FlightDataService: FetchAndSaveAirportDataAsync started.");
            try
            {
                string apiKey = _configuration["AviationEdge:ApiKey"] ?? throw new InvalidOperationException("API Key is missing.");
                string baseUrl = _configuration["AviationEdge:BaseUrl"] ?? throw new InvalidOperationException("Base URL is missing.");

                string airportUrl = $"{baseUrl}/v2/public/airportDatabase?key={apiKey}";
                Console.WriteLine($"Calling Airport API: {airportUrl}");
                var airportResponse = await _httpClient.GetAsync(airportUrl);
                if (!airportResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Airport API call failed: {airportResponse.StatusCode} - {airportResponse.ReasonPhrase}");
                    return;
                }

                string airportJson = await airportResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"Raw JSON response (first 500 chars): {airportJson.Substring(0, Math.Min(airportJson.Length, 500))}");
                var airportData = JsonDocument.Parse(airportJson).RootElement;

                if (airportData.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine($"Invalid airport response: Not an array.");
                    return;
                }

                var airportsToAdd = new List<Airport>();
                foreach (var airport in airportData.EnumerateArray())
                {
                    string? iataCode = null;
                    try
                    {
                        iataCode = GetJsonProperty(airport, "codeIataAirport")?.GetString();
                        if (string.IsNullOrEmpty(iataCode)) continue;

                        string? nameFromApi = GetJsonProperty(airport, "nameAirport")?.GetString();
                        string? cityFromApi = GetJsonProperty(airport, "nameCity")?.GetString();
                        string? countryFromApi = GetJsonProperty(airport, "nameCountry")?.GetString();
                        double latitude = GetDoubleFromJsonElement(airport.GetProperty("latitudeAirport"));
                        double longitude = GetDoubleFromJsonElement(airport.GetProperty("longitudeAirport"));

                        string derivedName;
                        string derivedCity;
                        string derivedCountry;
                        if (_airportData.TryGetValue(iataCode, out var airportInfo))
                        {
                            derivedName = airportInfo.Name;
                            derivedCity = airportInfo.City;
                            derivedCountry = airportInfo.Country;
                        }
                        else
                        {
                            derivedName = nameFromApi ?? "Unknown Airport";
                            derivedCity = cityFromApi ?? (nameFromApi != null ?
                                nameFromApi.Replace(" International", "").Replace(" Airport", "").Trim() : "Unknown City");
                            derivedCountry = countryFromApi ?? "Unknown Country";
                        }

                        Console.WriteLine($"Airport {iataCode}: NameFromAPI={nameFromApi}, DerivedName={derivedName}, CityFromAPI={cityFromApi}, DerivedCity={derivedCity}, CountryFromAPI={countryFromApi}, DerivedCountry={derivedCountry}");

                        var existingAirport = await _context.Airports
                            .FirstOrDefaultAsync(a => a.IataCode == iataCode);

                        if (existingAirport == null)
                        {
                            var newAirport = new Airport
                            {
                                IataCode = iataCode,
                                Name = derivedName,
                                City = derivedCity,
                                Country = derivedCountry,
                                Latitude = latitude,
                                Longitude = longitude
                            };
                            airportsToAdd.Add(newAirport);
                        }
                        else
                        {
                            existingAirport.Name = derivedName;
                            existingAirport.City = derivedCity;
                            existingAirport.Country = derivedCountry;
                            existingAirport.Latitude = latitude;
                            existingAirport.Longitude = longitude;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing airport {iataCode ?? "unknown"}: {ex.Message}");
                    }
                }

                if (airportsToAdd.Any())
                {
                    await _context.Airports.AddRangeAsync(airportsToAdd);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"Total airports saved: {airportsToAdd.Count}");
                }
                else
                {
                    Console.WriteLine("No new airports to save.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in FetchAndSaveAirportDataAsync: {ex.Message}");
            }
        }

        // Các phương thức khác giữ nguyên
        public async Task FetchAndSaveGlobalFlightDataAsync()
        {
            Console.WriteLine("FlightDataService: FetchAndSaveGlobalFlightDataAsync started.");
            try
            {
                string apiKey = _configuration["AviationEdge:ApiKey"] ?? throw new InvalidOperationException("API Key is missing.");
                string baseUrl = _configuration["AviationEdge:BaseUrl"] ?? throw new InvalidOperationException("Base URL is missing.");

                var airportIds = await _context.Airports
                    .ToDictionaryAsync(a => a.IataCode, a => a.AirportId);

                if (!airportIds.Any())
                {
                    Console.WriteLine("No airports found in database. Syncing airports first...");
                    await FetchAndSaveAirportDataAsync();
                    airportIds = await _context.Airports
                        .ToDictionaryAsync(a => a.IataCode, a => a.AirportId);
                }

                if (!airportIds.Any())
                {
                    Console.WriteLine("No airports retrieved. Aborting flight data fetch.");
                    return;
                }

                int totalProcessed = 0;
                foreach (var departureAirportCode in airportIds.Keys)
                {
                    int offset = 0;
                    while (true)
                    {
                        string timetableUrl = $"{baseUrl}/v2/public/timetable?key={apiKey}&iataCode={departureAirportCode}&type=departure&offset={offset}&limit={Limit}";
                        Console.WriteLine($"Fetching flights for {departureAirportCode} (Offset: {offset}): {timetableUrl}");
                        var response = await _httpClient.GetAsync(timetableUrl);
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"Failed to fetch timetable for {departureAirportCode}: {response.StatusCode}");
                            break;
                        }

                        string json = await response.Content.ReadAsStringAsync();
                        var timetableData = JsonDocument.Parse(json).RootElement;

                        if (timetableData.ValueKind != JsonValueKind.Array)
                        {
                            Console.WriteLine($"Invalid response for {departureAirportCode}: Not an array. Raw response: {json.Substring(0, Math.Min(json.Length, 500))}...");
                            break;
                        }

                        int processedThisBatch = await SaveTimetableToDatabaseAsync(timetableData, departureAirportCode, airportIds);
                        totalProcessed += processedThisBatch;

                        if (!timetableData.EnumerateArray().Any() || processedThisBatch == 0)
                        {
                            Console.WriteLine($"No more data for {departureAirportCode} at offset {offset}.");
                            break;
                        }

                        offset += Limit;
                    }
                }

                Console.WriteLine($"Total flights processed and saved worldwide: {totalProcessed}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching global flight data: {ex.Message}");
            }
        }

        private async Task<int> SaveTimetableToDatabaseAsync(JsonElement data, string departureAirportCode, Dictionary<string, int> airportIds)
        {
            if (!airportIds.ContainsKey(departureAirportCode)) return 0;

            var flightsToAdd = new List<Flight>();
            int departureAirportId = airportIds[departureAirportCode];

            foreach (var flight in data.EnumerateArray())
            {
                try
                {
                    string? flightNumber = GetJsonProperty(flight, "flight", "iataNumber")?.GetString() ?? $"FL{_random.Next(1000, 9999)}";
                    string? destinationAirportCode = GetJsonProperty(flight, "arrival", "iataCode")?.GetString();
                    if (string.IsNullOrEmpty(destinationAirportCode) || !airportIds.ContainsKey(destinationAirportCode))
                    {
                        Console.WriteLine($"Flight {flightNumber} from {departureAirportCode}: Invalid destination {destinationAirportCode}, skipping.");
                        continue;
                    }

                    int destinationAirportId = airportIds[destinationAirportCode];
                    string airline = GetJsonProperty(flight, "airline", "name")?.GetString() ?? "Unknown Airline";
                    string status = GetJsonProperty(flight, "status")?.GetString() ?? "Scheduled";

                    DateTime now = DateTime.UtcNow;
                    int randomDays = _random.Next(0, 730);
                    int randomHours = _random.Next(0, 24);
                    int randomMinutes = _random.Next(0, 60);
                    DateTime departureTime = now.AddDays(randomDays).AddHours(randomHours).AddMinutes(randomMinutes);

                    double distanceKm = await CalculateDistanceAsync(departureAirportCode, destinationAirportCode);
                    double flightHours = distanceKm / AverageSpeedKmh + 0.5;
                    DateTime arrivalTime = departureTime.AddHours(Math.Max(1.0, flightHours));

                    decimal price = CalculatePriceBasedOnDistanceAndAirline(distanceKm, airline);
                    int stops = distanceKm > 2000 ? _random.Next(0, 2) : 0;

                    var newFlight = new Flight
                    {
                        FlightNumber = flightNumber,
                        DepartureAirportId = departureAirportId,
                        DestinationAirportId = destinationAirportId,
                        DepartureTime = departureTime,
                        ArrivalTime = arrivalTime,
                        Airline = airline,
                        Price = price,
                        AvailableSeats = 100,
                        Stops = stops,
                        Status = status
                    };

                    flightsToAdd.Add(newFlight);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing flight from {departureAirportCode}: {ex.Message}");
                }
            }

            if (flightsToAdd.Any())
            {
                try
                {
                    await _context.Flights.AddRangeAsync(flightsToAdd);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"Processed {flightsToAdd.Count} flights from {departureAirportCode}");
                    return flightsToAdd.Count;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving flights to database: {ex.Message}");
                    return 0;
                }
            }

            Console.WriteLine($"No flights to save for {departureAirportCode}");
            return 0;
        }

        private async Task<double> CalculateDistanceAsync(string departureAirportCode, string destinationAirportCode)
        {
            var departureAirport = await _context.Airports.FirstOrDefaultAsync(a => a.IataCode == departureAirportCode);
            var destinationAirport = await _context.Airports.FirstOrDefaultAsync(a => a.IataCode == destinationAirportCode);

            if (departureAirport == null || destinationAirport == null)
            {
                return 1000;
            }

            double lat1 = departureAirport.Latitude;
            double lon1 = departureAirport.Longitude;
            double lat2 = destinationAirport.Latitude;
            double lon2 = destinationAirport.Longitude;

            const double R = 6371;
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180;
        }

        private decimal CalculatePriceBasedOnDistanceAndAirline(double distance, string airline)
        {
            decimal basePrice = 1000000m;
            decimal pricePerKm = 2000m;
            decimal baseDistancePrice = basePrice + (decimal)distance * pricePerKm;
            decimal airlineFactor = _airlinePriceFactors.ContainsKey(airline) ? _airlinePriceFactors[airline] : 1.0m;
            return Math.Max(1500000m, baseDistancePrice * airlineFactor);
        }

        private JsonElement? GetJsonProperty(JsonElement element, params string[] propertyPath)
        {
            JsonElement current = element;
            foreach (var prop in propertyPath)
            {
                if (!current.TryGetProperty(prop, out current)) return null;
            }
            return current;
        }

        private double GetDoubleFromJsonElement(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetDouble(out double value) ? value : 0;
            if (element.ValueKind == JsonValueKind.String)
                return double.TryParse(element.GetString(), out double value) ? value : 0;
            return 0;
        }
    }
}