namespace WebAccess.Media;

public sealed record Alarm(string Point, string? FloorPlanUrl = null, string? Description = null);

public sealed class AlarmDisplay
{
    public Alarm? Current { get; private set; }

    public void Update(IReadOnlyList<Alarm> activeAlarms)
    {
        Current = activeAlarms.FirstOrDefault();
    }
}
