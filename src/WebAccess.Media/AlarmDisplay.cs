namespace WebAccess.Media;

public sealed record Alarm(string Point, string? FloorPlanUrl = null, string? Description = null);

public static class AlarmDisplay
{
    public static (Alarm? Alarm, PointPlan? Marker) SelectRegistered(
        IReadOnlyList<Alarm> activeAlarms, IReadOnlyList<PointPlan> plans)
    {
        foreach (Alarm alarm in activeAlarms)
        {
            PointPlan? plan = plans.FirstOrDefault(plan => plan.Point == alarm.Point);
            if (plan is not null)
            {
                return (alarm with { FloorPlanUrl = $"/media/{plan.File}" }, plan);
            }
        }
        return (null, null);
    }
}
