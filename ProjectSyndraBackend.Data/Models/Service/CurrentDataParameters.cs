namespace ProjectSyndraBackend.Data.Models.Service;

public class CurrentDataParameters
{
    public int CurrentDataParametersId { get; set; }
    public string? Patch { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}