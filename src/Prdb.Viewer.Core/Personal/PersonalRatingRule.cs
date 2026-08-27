namespace Prdb.Viewer.Core.Personal;

public static class PersonalRatingRule
{
    public const int Minimum = 1;

    public const int Maximum = 5;

    public static bool IsValid(int rating) => rating is >= Minimum and <= Maximum;
}
