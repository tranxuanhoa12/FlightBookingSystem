public class WebsiteVisit
{
    public int Id { get; set; }
    public DateTime VisitDate { get; set; }
    public int? UserId { get; set; }
    public string IpAddress { get; set; }
    public string Path { get; set; }
}