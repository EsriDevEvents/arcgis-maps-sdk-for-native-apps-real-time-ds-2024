namespace DeliveryShared;

public enum MessageType { Add, Delete }

public enum CompanyColor { Black, Red, Blue, Green, Purple }

public class LocationMessage
{
    public LocationMessage()
    {
    }

    public int MessageType { get; set; } = 0; // 0 - Add, 1 - Delete

    public string EntityId { get; set; } = string.Empty;

    public int Company { get; set; }

    public string Name { get; set; } = string.Empty;

    public double PayloadWeight { get; set; }

    public double Speed { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Heading { get; set; }
}
