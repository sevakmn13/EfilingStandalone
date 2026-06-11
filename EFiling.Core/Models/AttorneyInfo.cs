namespace EFiling.Core.Models;

/// <summary>
/// Attorney information from the attorney list lookup.
/// </summary>
public class AttorneyInfo
{
    public string? Id { get; set; }
    public string? BarNumber { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? LastNameSuffix { get; set; }
    public string? FirmName { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? StatusCode { get; set; }
    public string? ParticipationStatus { get; set; }
}
