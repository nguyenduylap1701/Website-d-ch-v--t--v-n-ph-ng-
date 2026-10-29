using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;
using TroTot.Web.Services;

namespace TroTot.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin))]
[Route("quan-tri")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AdminController> _logger;

    public AdminController(AppDbContext db, IWebHostEnvironment env, ILogger<AdminController> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }
    [HttpGet("nguoi-dung")]
    public async Task<IActionResult> Users(string? search, UserRole? role, bool? active)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(user =>
                user.FullName.Contains(term) ||
                user.Email.Contains(term) ||
                user.Phone.Contains(term));
        }
        if (role.HasValue)
        {
            query = query.Where(user => user.Role == role.Value);
        }
        if (active.HasValue)
        {
            query = query.Where(user => user.IsActive == active.Value);
        }

        ViewBag.Search = search;
        ViewBag.Role = role;
        ViewBag.ActiveFilter = active;
        ViewBag.TotalUsers = await _db.Users.CountAsync();
        return View(await query.OrderByDescending(user => user.CreatedAt).ToListAsync());
    }
    [HttpPost("nguoi-dung/{id:int}/vai-tro")]
    public async Task<IActionResult> ChangeUserRole(int id, UserRole role)
    {
        if (role is not (UserRole.Tenant or UserRole.Owner))
        {
            TempData["Error"] = "Vai trò được chọn không hợp lệ.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.Role == UserRole.Admin)
        {
            TempData["Error"] = "Không thể thay đổi quyền của tài khoản quản trị viên tại đây.";
            return RedirectToAction(nameof(Users));
        }

        user.Role = role;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã cập nhật vai trò của {user.FullName}.";
        return RedirectToAction(nameof(Users));
    }
    [HttpPost("nguoi-dung/{id:int}/trang-thai")]
    public async Task<IActionResult> ToggleUserStatus(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(currentUserId, out var authenticatedId) && authenticatedId == id)
        {
            TempData["Error"] = "Bạn không thể khóa tài khoản đang sử dụng.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.Role == UserRole.Admin)
        {
            TempData["Error"] = "Không thể khóa tài khoản quản trị viên tại đây.";
            return RedirectToAction(nameof(Users));
        }

        user.IsActive = !user.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = user.IsActive
            ? $"Đã mở khóa tài khoản {user.FullName}."
            : $"Đã khóa tài khoản {user.FullName}.";
        return RedirectToAction(nameof(Users));
    }
    [HttpGet("dang-tin")]
    public async Task<IActionResult> Listings(RoomStatus? status)
    {
        var query = _db.Rooms.AsNoTracking().Include(room => room.Owner).AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(room => room.Status == status.Value);
        }

        ViewBag.Status = status;
        ViewBag.TotalRooms = await _db.Rooms.CountAsync();
        ViewBag.PendingRooms = await _db.Rooms.CountAsync(room => room.Status == RoomStatus.Pending);
        ViewBag.PendingLegalRooms = await _db.Rooms.CountAsync(room => room.LegalStatus == LegalVerificationStatus.Pending);
        return View(await query.OrderByDescending(room => room.CreatedAt).ToListAsync());
    }
    [HttpGet("dang-tin/{id:int}")]
    public async Task<IActionResult> ListingDetails(int id)
    {
        var room = await _db.Rooms.AsNoTracking()
            .Include(item => item.Owner)
            .Include(item => item.Images)
            .FirstOrDefaultAsync(item => item.Id == id);
        return room is null ? NotFound() : View(room);
    }
    [HttpPost("dang-tin/{id:int}/xoa")]
    public async Task<IActionResult> DeleteListing(int id)
    {
        var room = await _db.Rooms.Include(item => item.Images)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (room is null) return NotFound();

        if (await _db.RentalContracts.AnyAsync(contract => contract.RoomId == id) ||
            await _db.UtilityBills.AnyAsync(bill => bill.RoomId == id) ||
            await _db.RepairRequests.AnyAsync(request => request.RoomId == id))
        {
            TempData["Error"] = "Không thể xóa tin này vì phòng đang có hợp đồng, hóa đơn hoặc yêu cầu sửa chữa được lưu.";
            return RedirectToAction(nameof(ListingDetails), new { id });
        }

        var reviews = await _db.RoomReviews.Where(review => review.RoomId == id).ToListAsync();
        var viewingRequests = await _db.ViewingRequests.Where(request => request.RoomId == id).ToListAsync();
        _db.RoomReviews.RemoveRange(reviews);
        _db.ViewingRequests.RemoveRange(viewingRequests);
        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync();

        RoomFileCleanup.Delete(room, _env, _logger);
        TempData["Success"] = $"Đã xóa tin \"{room.Title}\".";
        return RedirectToAction(nameof(Listings));
    }
    [HttpGet("dang-tin/{id:int}/tai-lieu/{documentType}")]
    public async Task<IActionResult> LegalDocument(int id, string documentType)
    {
        var room = await _db.Rooms.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (room is null) return NotFound();

        var fileName = documentType switch
        {
            "cccd-truoc" => room.IdentityFrontDocument,
            "cccd-sau" => room.IdentityBackDocument,
            "giay-to-so-huu" => room.PropertyDocument,
            "giay-to-phong" => room.RoomLegalDocument,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            return NotFound();

        var path = Path.Combine(_env.ContentRootPath, "App_Data", "verification-documents", fileName);
        if (!System.IO.File.Exists(path)) return NotFound();

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => null
        };
        if (contentType is null) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, contentType);
    }

    [HttpPost("dang-tin/{id:int}/duyet")]
    public Task<IActionResult> ApproveListing(int id) => SetListingStatus(id, RoomStatus.Approved);

    [HttpPost("dang-tin/{id:int}/tu-choi")]
    public Task<IActionResult> RejectListing(int id) => SetListingStatus(id, RoomStatus.Rejected);

    [HttpPost("dang-tin/{id:int}/duyet-phap-ly")]
    public Task<IActionResult> ApproveLegalVerification(int id) =>
        SetLegalVerificationStatus(id, LegalVerificationStatus.Approved);

    [HttpPost("dang-tin/{id:int}/tu-choi-phap-ly")]
    public Task<IActionResult> RejectLegalVerification(int id) =>
        SetLegalVerificationStatus(id, LegalVerificationStatus.Rejected);

    [HttpGet("tin-nhan")]
    public async Task<IActionResult> Messages(bool? handled)
    {
        var query = _db.ContactMessages.AsNoTracking().AsQueryable();
        if (handled.HasValue)
        {
            query = query.Where(message => message.IsHandled == handled.Value);
        }

        ViewBag.Handled = handled;
        ViewBag.TotalMessages = await _db.ContactMessages.CountAsync();
        ViewBag.UnhandledMessages = await _db.ContactMessages.CountAsync(message => !message.IsHandled);
        return View(await query.OrderByDescending(message => message.CreatedAt).ToListAsync());
    }
    [HttpPost("tin-nhan/{id:int}/xu-ly")]
    public async Task<IActionResult> ToggleMessageStatus(int id)
    {
        var message = await _db.ContactMessages.FindAsync(id);
        if (message is null) return NotFound();

        message.IsHandled = !message.IsHandled;
        await _db.SaveChangesAsync();
        TempData["Success"] = message.IsHandled ? "Đã đánh dấu tin nhắn đã xử lý." : "Đã chuyển tin nhắn về trạng thái chưa xử lý.";
        return RedirectToAction(nameof(Messages));
    }
    [HttpGet("danh-gia")]
    public async Task<IActionResult> Reviews(int? rating, bool? published)
    {
        var query = _db.RoomReviews.AsNoTracking()
            .Include(review => review.User)
            .Include(review => review.Room)
            .AsQueryable();
        if (rating.HasValue && rating.Value is >= 1 and <= 5)
        {
            query = query.Where(review => review.Rating == rating.Value);
        }
        else if (rating.HasValue)
        {
            rating = null;
        }
        if (published.HasValue)
        {
            query = query.Where(review => review.IsPublished == published.Value);
        }

        var unseenReviews = await _db.RoomReviews
            .Where(review => !review.IsReviewed)
            .ToListAsync();
        if (unseenReviews.Count > 0)
        {
            foreach (var review in unseenReviews)
                review.IsReviewed = true;
            await _db.SaveChangesAsync();
        }

        ViewBag.Rating = rating;
        ViewBag.Published = published;
        ViewBag.TotalReviews = await _db.RoomReviews.CountAsync();
        ViewBag.AverageRating = await _db.RoomReviews.AnyAsync()
            ? await _db.RoomReviews.AverageAsync(review => (double)review.Rating)
            : 0d;
        return View(await query.OrderByDescending(review => review.CreatedAt).ToListAsync());
    }
    [HttpPost("danh-gia/{id:int}/trang-thai")]
    public async Task<IActionResult> ToggleReviewStatus(int id)
    {
        var review = await _db.RoomReviews.FindAsync(id);
        if (review is null) return NotFound();

        review.IsPublished = !review.IsPublished;
        await _db.SaveChangesAsync();
        TempData["Success"] = review.IsPublished ? "Đánh giá đã được hiển thị." : "Đánh giá đã được ẩn.";
        return RedirectToAction(nameof(Reviews));
    }

    private async Task<IActionResult> SetListingStatus(int id, RoomStatus status)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        var hasLegalDocuments =
            !string.IsNullOrWhiteSpace(room.IdentityFrontDocument) ||
            !string.IsNullOrWhiteSpace(room.IdentityBackDocument) ||
            !string.IsNullOrWhiteSpace(room.PropertyDocument) ||
            !string.IsNullOrWhiteSpace(room.RoomLegalDocument);
        if (status == RoomStatus.Approved && hasLegalDocuments &&
            room.LegalStatus != LegalVerificationStatus.Approved)
        {
            TempData["Error"] = "Cần xác nhận hồ sơ pháp lý trước khi duyệt tin đăng.";
            return RedirectToAction(nameof(ListingDetails), new { id });
        }

        room.Status = status;
        room.HasUnreadOwnerNotification = true;
        await _db.SaveChangesAsync();
        TempData["Success"] = status == RoomStatus.Approved
            ? $"Đã duyệt tin \"{room.Title}\"."
            : $"Đã từ chối tin \"{room.Title}\".";
        return RedirectToAction(nameof(Listings));
    }

    private async Task<IActionResult> SetLegalVerificationStatus(int id, LegalVerificationStatus status)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        if (status == LegalVerificationStatus.Approved &&
            (string.IsNullOrWhiteSpace(room.IdentityFrontDocument) ||
             string.IsNullOrWhiteSpace(room.IdentityBackDocument) ||
             string.IsNullOrWhiteSpace(room.PropertyDocument) ||
             string.IsNullOrWhiteSpace(room.RoomLegalDocument)))
        {
            TempData["Error"] = "Chưa thể xác nhận pháp lý vì hồ sơ còn thiếu giấy tờ.";
            return RedirectToAction(nameof(ListingDetails), new { id });
        }

        room.LegalStatus = status;
        room.IsVerified = status == LegalVerificationStatus.Approved;
        room.HasUnreadOwnerNotification = true;
        if (status == LegalVerificationStatus.Rejected && room.Status == RoomStatus.Approved)
            room.Status = RoomStatus.Rejected;
        await _db.SaveChangesAsync();
        TempData["Success"] = status == LegalVerificationStatus.Approved
            ? $"Đã xác nhận hồ sơ pháp lý của tin \"{room.Title}\"."
            : $"Đã từ chối hồ sơ pháp lý của tin \"{room.Title}\".";
        return RedirectToAction(nameof(ListingDetails), new { id });
    }
}
