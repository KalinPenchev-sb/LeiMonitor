namespace LeiMonitor.Data.Notifications;

public class EmailAlertMessage
{
    public Guid CorrelationId { get; init; }
    public Guid NotificationId { get; init; }
    public Guid CustomerId { get; init; }
    public string LeiCode { get; init; } = string.Empty;
    public string LegalName { get; init; } = string.Empty;
    public DateTime? RenewalExpiryDate { get; init; }
    public int? ThresholdDays { get; init; }
    public int? DaysUntilExpiry { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public string RecipientGroup { get; init; } = string.Empty;
}
