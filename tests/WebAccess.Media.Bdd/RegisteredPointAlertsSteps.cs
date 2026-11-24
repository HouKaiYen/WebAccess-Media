using NUnit.Framework;
using Reqnroll;
using WebAccess.Media;

[Binding]
public sealed class RegisteredPointAlertsSteps
{
    private PointPlan[] _plans = [];
    private Alarm[] _active = [];

    [Given("工程設定只新增測點 B")]
    public void GivenRegisteredPoint() => _plans = [new("B", "b.png", 25, 75)];

    [When("WebAccess 回報測點 A 告警")]
    public void WhenUnregisteredAlarm() => _active = [new("A", "/legacy-a.png")];

    [When("WebAccess 依序回報測點 A 與 B 告警")]
    public void WhenBothAlarms() => _active = [new("A"), new("B", Description: "測試告警")];

    [Then("顯示頁維持待機輪播")]
    public void ThenStandby()
    {
        (Alarm? alarm, PointPlan? marker) = AlarmDisplay.SelectRegistered(_active, _plans);
        Assert.That(alarm, Is.Null);
        Assert.That(marker, Is.Null);
    }

    [Then("顯示頁顯示測點 B 的平面圖與標記")]
    public void ThenShowsRegisteredAlarm()
    {
        (Alarm? alarm, PointPlan? marker) = AlarmDisplay.SelectRegistered(_active, _plans);
        Assert.That(alarm, Is.EqualTo(new Alarm("B", "/media/b.png", "測試告警")));
        Assert.That(marker, Is.EqualTo(_plans[0]));
    }
}
