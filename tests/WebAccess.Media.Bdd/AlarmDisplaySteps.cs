using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class AlarmDisplaySteps
{
    private readonly PointPlan[] _plans = [new("A", "a.png", 50, 50), new("B", "b.png", 50, 50)];
    private Alarm[] _alarms = [];

    [Given("目前沒有告警")]
    public void GivenNoAlarms() => _alarms = [];

    [When("收到 A 與 B 的有效告警")]
    public void WhenBothAlarmsAreActive() =>
        _alarms = [new("A"), new("B")];

    [When("A 解除而 B 仍有效")]
    public void WhenAlarmAClears() => _alarms = [new("B")];

    [When("所有告警解除")]
    public void WhenAllAlarmsClear() => _alarms = [];

    [Then("畫面顯示 (.*) 的平面圖")]
    public void ThenShowsFloorPlan(string point) =>
        Assert.That(AlarmDisplay.SelectRegistered(_alarms, _plans).Alarm?.FloorPlanUrl,
            Is.EqualTo($"/engineering-media/{point.ToLowerInvariant()}.png"));

    [Then("畫面顯示待機內容")]
    public void ThenShowsIdleContent() => Assert.That(AlarmDisplay.SelectRegistered(_alarms, _plans).Alarm, Is.Null);
}
