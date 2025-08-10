namespace Transcendence.Data.Models.Service;

public class CurrentDataParameters
{
    public Guid CurrentDataParametersId { get; set; }
    public string? Patch { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}