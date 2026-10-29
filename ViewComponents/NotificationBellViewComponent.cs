using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;
using TroTot.Web.ViewModels;

namespace TroTot.Web.ViewComponents;

public sealed class NotificationBellViewComponent(AppDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(bool isDashboard)
    {
        var items = new List<NotificationBellItem>();
        if (UserClaimsPrincipal.IsInRole(nameof(UserRole.Admin)))
        {
            var pendingListings = await db.Rooms.CountAsync(room => room.Status == RoomStatus.Pending);
            var pendingReviews = await db.RoomReviews.CountAsync(review => !review.IsReviewed);
            items.Add(new NotificationBellItem("Tin đăng chờ duyệt", "/quan-tri/dang-tin", pendingListings));
            items.Add(new NotificationBellItem("Đánh giá mới", "/quan-tri/danh-gia?published=false", pendingReviews));
        }
        else if (UserClaimsPrincipal.IsInRole(nameof(UserRole.Owner)))
        {
            var ownerId = int.Parse(UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var unreadResults = await db.Rooms.CountAsync(room =>
                room.OwnerId == ownerId && room.HasUnreadOwnerNotification);
            var roomIds = db.Rooms.Where(room => room.OwnerId == ownerId).Select(room => room.Id);
            var newViewingRequests = await db.ViewingRequests.CountAsync(request =>
                !request.IsHandled && request.RoomId.HasValue && roomIds.Contains(request.RoomId.Value));
            items.Add(new NotificationBellItem("Kết quả duyệt tin", "/chu-tro/phong", unreadResults));
            items.Add(new NotificationBellItem("Yêu cầu xem phòng mới", "/chu-tro/tin-nhan", newViewingRequests));
        }

        var model = new NotificationBellViewModel(
            isDashboard,
            items.Sum(item => item.Count),
            items);
        return View("Default", model);
    }
}
