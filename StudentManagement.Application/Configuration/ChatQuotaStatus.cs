namespace StudentManagement.Application.Configuration;

// Snapshot of a caller's daily token budget. Admins are unlimited; the day boundary
// is UTC midnight so every replica agrees on when the quota resets.
public sealed record ChatQuotaStatus(int UsedToday, int DailyLimit, bool IsUnlimited, DateTime ResetsAtUtc)
{
    public int Remaining => IsUnlimited ? int.MaxValue : Math.Max(0, DailyLimit - UsedToday);

    public bool Exceeded => !IsUnlimited && UsedToday >= DailyLimit;
}
