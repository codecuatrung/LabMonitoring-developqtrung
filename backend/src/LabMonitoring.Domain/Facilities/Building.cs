namespace LabMonitoring.Domain.Facilities;

public class Building
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;

    public ICollection<Room> Rooms { get; set; } = [];
}
