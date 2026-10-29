namespace TroTot.Web.ViewModels;

public sealed record NotificationBellItem(string Label, string Url, int Count);

public sealed record NotificationBellViewModel(
    bool IsDashboard,
    int TotalCount,
    IReadOnlyList<NotificationBellItem> Items);
