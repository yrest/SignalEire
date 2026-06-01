namespace IrelandLiveSignals.Core.Models;

public class ServiceCalendar
{
    public string ServiceId { get; set; } = string.Empty;
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public bool Sunday { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public class ServiceCalendarDate
{
    public string ServiceId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    // 1 = service added, 2 = service removed
    public int ExceptionType { get; set; }
}
