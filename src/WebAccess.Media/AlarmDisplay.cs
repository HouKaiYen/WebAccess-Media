namespace WebAccess.Media;

public sealed record Alarm(string Point, string? FloorPlanUrl = null, string? Description = null);

public static class AlarmDisplay
{
    public static string? ResolveTextOverride(string? entered, string? suggested)
    {
        string? text = entered?.Trim();
        return string.IsNullOrEmpty(text) || text == suggested?.Trim() ? null : text;
    }

    public static (Alarm? Alarm, PointPlan? Marker) SelectRegistered(
        IReadOnlyList<Alarm> activeAlarms, IReadOnlyList<PointPlan> plans)
    {
        foreach (Alarm alarm in activeAlarms)
        {
            PointPlan? plan = plans.FirstOrDefault(plan => plan.Point == alarm.Point);
            if (plan is not null)
            {
                return (alarm with
                {
                    FloorPlanUrl = $"/engineering-media/{plan.File}",
                    Description = string.IsNullOrWhiteSpace(plan.AlarmText) ? alarm.Description : plan.AlarmText
                }, plan);
            }
        }
        return (null, null);
    }
}
