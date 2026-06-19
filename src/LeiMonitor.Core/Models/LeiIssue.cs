namespace LeiMonitor.Core.Models;

public class LeiIssue
{
    public Guid CustomerId { get; set; }
    public string LeiCode { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public DateTime? ExpirationDate { get; set; }
    public bool IsExpired { get; set; }
}
