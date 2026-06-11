namespace EFiling.Core.Enums;

/// <summary>
/// Standard party roles used across courts. Actual code values vary by court.
/// </summary>
public enum PartyRole
{
    Plaintiff,
    Defendant,
    Petitioner,
    Respondent,
    Appellant,
    Conservatee,
    Attorney,
    Other
}
