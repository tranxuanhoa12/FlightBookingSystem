namespace FlightBookingApp.ViewModel
{
   public class BookingFormViewModel
{
    public int FlightId { get; set; }
    public int? ReturnFlightId { get; set; }
    public int PassengerCount { get; set; }
    public int AdultCount { get; set; }
    public int ChildCount { get; set; }
    public bool IsRoundTrip { get; set; }
    public string ContactEmail { get; set; }
    public string ContactPhone { get; set; }
    public string ContactName { get; set; }
    public string ContactGender { get; set; }
    public List<string> PassengerNames { get; set; }
    public List<string> PassengerDob { get; set; }
    public List<string> PassengerGenders { get; set; }
    public List<string> AddIdAdult { get; set; }
    public List<string> AddIdChild { get; set; }
    public List<string> IdTypeAdult { get; set; }
    public List<string> IdExpiryAdult { get; set; }
    public List<string> IdCountryAdult { get; set; }
    public List<string> NationalityAdult { get; set; }
    public List<string> IdTypeChild { get; set; }
    public List<string> IdExpiryChild { get; set; }
    public List<string> IdCountryChild { get; set; }
    public List<string> NationalityChild { get; set; }
    public List<string> LuggageAdult { get; set; }
    public List<string> LuggageChild { get; set; }
    public bool InvoiceRequest { get; set; }
    public string CompanyName { get; set; }
    public string CompanyAddress { get; set; }
    public string CompanyCity { get; set; }
    public string TaxCode { get; set; }
    public string InvoiceRecipient { get; set; }
    public string InvoicePhone { get; set; }
    public string InvoiceEmail { get; set; }
}
}
