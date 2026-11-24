using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class AlarmDisplaySteps
{
    private AlarmDisplay _display = new();

    [Given("目前沒有告警")]
    public void GivenNoAlarms() => _display = new AlarmDisplay();

    [When("收到 A 與 B 的有效告警")]
    public void WhenBothAlarmsAreActive() =>
        _display.Update([new("A", "/a.png"), new("B", "/b.png")]);

    [When("A 解除而 B 仍有效")]
    public void WhenAlarmAClears() => _display.Update([new("B", "/b.png")]);

    [When("所有告警解除")]
    public void WhenAllAlarmsClear() => _display.Update([]);

    [Then("畫面顯示 (.*) 的平面圖")]
    public void ThenShowsFloorPlan(string point) =>
        Assert.That(_display.Current?.FloorPlanUrl, Is.EqualTo($"/{point.ToLowerInvariant()}.png"));

    [Then("畫面顯示待機內容")]
    public void ThenShowsIdleContent() => Assert.That(_display.Current, Is.Null);
}
