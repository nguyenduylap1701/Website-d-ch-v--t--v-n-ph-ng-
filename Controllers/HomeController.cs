using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;
using TroTot.Web.ViewModels;

namespace TroTot.Web.Controllers;

public class HomeController : Controller
{
    private static readonly string[] AllowedImageExt = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxImageBytes = 5 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public HomeController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // ================= GET pages =================
    [HttpGet("")]
    [HttpGet("index")]
    public IActionResult Index() => View();

    [HttpGet("rooms")]
    public async Task<IActionResult> Rooms(
        string? keyword,
        string? district,
        string? type,
        string[]? amenity,
        long? minPrice,
        long? maxPrice,
        string? priceRange,
        bool? verified,
        string? sort)
    {
        (minPrice, maxPrice) = priceRange switch
        {
            "under-3m" => ((long?)null, 2_999_999L),
            "3-5m" => (3_000_000L, 4_999_999L),
            "5-8m" => (5_000_000L, 7_999_999L),
            "over-8m" => (8_000_000L, (long?)null),
            _ => (minPrice, maxPrice)
        };

        var query = _db.Rooms
            .Include(r => r.Images)
            .Include(r => r.Owner)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(r => r.Title.Contains(kw) || r.Address.Contains(kw) || r.District.Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(district))
        {
            var area = district.Trim();
            query = query.Where(r => r.District == area || r.Address.Contains(area));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(r => r.RoomType == type);
        }

        var selectedAmenities = (amenity ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var selectedAmenity in selectedAmenities)
        {
            var exactAmenity = $",{selectedAmenity},";
            query = query.Where(r =>
                ("," + r.Amenities.Replace(", ", ",") + ",").Contains(exactAmenity));
        }

        if (minPrice.HasValue)
        {
            query = query.Where(r => r.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(r => r.Price <= maxPrice.Value);
        }

        if (verified == true)
        {
            query = query.Where(r => r.IsVerified);
        }

        query = sort switch
        {
            "price-asc" => query.OrderBy(r => r.Price),
            "price-desc" => query.OrderByDescending(r => r.Price),
            "newest" => query.OrderByDescending(r => r.CreatedAt),
            "rating" => query.OrderByDescending(r => r.IsVerified).ThenByDescending(r => r.CreatedAt),
            _ => query.OrderByDescending(r => r.IsVerified).ThenByDescending(r => r.CreatedAt)
        };

        var rooms = await query
            .Where(r => r.Status == RoomStatus.Approved)
            .ToListAsync();

        ViewBag.Districts = await _db.Rooms
            .Where(r => r.Status == RoomStatus.Approved)
            .Select(r => r.District)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync();
        ViewBag.Keyword = keyword;
        ViewBag.District = district;
        ViewBag.Type = type;
        ViewBag.Amenities = selectedAmenities;
        ViewBag.MinPrice = minPrice;
        ViewBag.MaxPrice = maxPrice;
        ViewBag.PriceRange = priceRange;
        ViewBag.VerifiedOnly = verified == true;
        ViewBag.Sort = sort;

        return View(rooms);
    }

    [HttpGet("phong/{id:int}")]
    public async Task<IActionResult> RoomDetail(int id)
    {
        var room = await _db.Rooms
            .Include(r => r.Images)
            .Include(r => r.Owner)
            .FirstOrDefaultAsync(r => r.Id == id && r.Status == RoomStatus.Approved);

        if (room == null)
        {
            return NotFound();
        }

        var relatedRooms = await _db.Rooms
            .Include(r => r.Images)
            .Where(r => r.Status == RoomStatus.Approved && r.Id != id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(3)
            .ToListAsync();

        ViewBag.RelatedRooms = relatedRooms;
        ViewBag.RoomReviews = await _db.RoomReviews
            .AsNoTracking()
            .Include(review => review.User)
            .Where(review => review.RoomId == id && review.IsPublished)
            .OrderByDescending(review => review.CreatedAt)
            .ToListAsync();
        return View(room);
    }

    [Authorize(Roles = $"{nameof(UserRole.Owner)},{nameof(UserRole.Admin)}")]
    [HttpGet("quan-ly")]
    public async Task<IActionResult> QuanLy()
    {
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var ownerId = isAdmin ? (int?)null : int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var roomsQuery = _db.Rooms.AsQueryable();
        if (ownerId.HasValue)
        {
            roomsQuery = roomsQuery.Where(r => r.OwnerId == ownerId.Value);
        }

        var stats = new
        {
            TotalRooms = await roomsQuery.CountAsync(),
            ActiveRooms = await roomsQuery.CountAsync(r => r.Status == RoomStatus.Approved),
            PendingRooms = await roomsQuery.CountAsync(r => r.Status == RoomStatus.Pending),
            NewRequests = isAdmin
                ? await _db.ViewingRequests.CountAsync(r => !r.IsHandled)
                : await _db.ViewingRequests.CountAsync(r =>
                    !r.IsHandled &&
                    roomsQuery.Select(room => room.Id).Contains(r.RoomId ?? 0)),
            TotalUsers = isAdmin ? await _db.Users.CountAsync() : 0,
            TotalOwners = isAdmin ? await _db.Users.CountAsync(user => user.Role == UserRole.Owner) : 0,
            TotalTenants = isAdmin ? await _db.Users.CountAsync(user => user.Role == UserRole.Tenant) : 0,
            TotalConsultations = isAdmin ? await _db.ConsultationRequests.CountAsync() : 0,
            TotalMessages = isAdmin ? await _db.ContactMessages.CountAsync() : 0,
            UnhandledMessages = isAdmin ? await _db.ContactMessages.CountAsync(message => !message.IsHandled) : 0,
            TotalReviews = isAdmin ? await _db.RoomReviews.CountAsync() : 0
        };

        var pendingRooms = isAdmin
            ? await _db.Rooms.Include(r => r.Owner)
                .Where(r => r.Status == RoomStatus.Pending)
                .OrderByDescending(r => r.CreatedAt)
                .Take(5)
                .ToListAsync()
            : new List<Room>();

        var recentRooms = await roomsQuery
            .Include(r => r.Images)
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .ToListAsync();

        var recentRequestsQuery = _db.ViewingRequests.AsQueryable();
        if (!isAdmin)
        {
            recentRequestsQuery = recentRequestsQuery.Where(request =>
                roomsQuery.Select(room => room.Id).Contains(request.RoomId ?? 0));
        }
        var recentRequests = await recentRequestsQuery
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .ToListAsync();

        ViewBag.Stats = stats;
        ViewBag.RecentRooms = recentRooms;
        ViewBag.RecentRequests = recentRequests;
        ViewBag.IsAdminDashboard = isAdmin;
        ViewBag.PendingRooms = pendingRooms;
        return View();
    }

    [HttpGet("blog")]
    public IActionResult Blog() => View();

    [HttpGet("lien-he")]
    public IActionResult LienHe() => View();

    [HttpGet("tu-van")]
    public async Task<IActionResult> TuVan()
    {
        var vm = new TuVanPageViewModel
        {
            Questions = await _db.Questions
                .Where(q => q.IsPublished)
                .OrderByDescending(q => q.Id)
                .ToListAsync()
        };
        return View(vm);
    }

    [Authorize(Roles = $"{nameof(UserRole.Owner)},{nameof(UserRole.Admin)}")]
    [HttpGet("dang-tin")]
    public IActionResult DangTin() => View();

    // ================= POST actions =================

    // Yêu cầu tư vấn (trang Tư vấn + form ở trang chủ)
    [HttpPost("tu-van")]
    public async Task<IActionResult> TuVan(ConsultationForm form, string? returnUrl)
    {
        if (!ModelState.IsValid) return Fail(returnUrl, "/tu-van");

        _db.ConsultationRequests.Add(new ConsultationRequest
        {
            Name = form.Name.Trim(),
            Phone = form.Phone.Trim(),
            District = form.District,
            Budget = form.Budget,
            RoomType = form.RoomType,
            MoveInTime = form.MoveInTime,
            Amenities = form.Amenities is { Count: > 0 } ? string.Join(", ", form.Amenities) : null,
            Workplace = form.Workplace?.Trim(),
            Note = form.Note?.Trim()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Cảm ơn {form.Name}! Chuyên viên sẽ liên hệ số {form.Phone} trong 30 phút.";
        return Back(returnUrl, "/tu-van");
    }

    // Đặt câu hỏi cho cộng đồng
    [HttpPost("hoi-dap")]
    public async Task<IActionResult> HoiDap(QuestionForm form)
    {
        if (!ModelState.IsValid) return Fail(null, "/tu-van");

        _db.Questions.Add(new Question
        {
            Topic = form.Topic,
            Content = form.Content.Trim(),
            IsPublished = true   // đổi thành false nếu muốn admin duyệt trước khi hiển thị
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã đăng câu hỏi! Chuyên gia TroTot sẽ giải đáp sớm.";
        return Redirect("/tu-van");
    }

    // Đặt lịch xem phòng (modal ở trang chủ và trang tìm phòng)
    [HttpPost("dat-lich-xem")]
    public async Task<IActionResult> DatLichXem(ViewingForm form, string? returnUrl)
    {
        if (form.Date.HasValue && form.Date.Value.Date < DateTime.Today)
            ModelState.AddModelError(nameof(form.Date), "Ngày xem không được ở quá khứ.");
        if (!ModelState.IsValid) return Fail(returnUrl, "/rooms");

        var matchingRoomIds = await _db.Rooms
            .Where(room => room.Status == RoomStatus.Approved && room.Title == form.RoomName)
            .Select(room => room.Id)
            .Take(2)
            .ToListAsync();
        _db.ViewingRequests.Add(new ViewingRequest
        {
            RoomId = matchingRoomIds.Count == 1 ? matchingRoomIds[0] : null,
            Name = form.Name.Trim(),
            Phone = form.Phone.Trim(),
            Date = form.Date!.Value.Date,
            Time = form.Time,
            RoomName = form.RoomName?.Trim() ?? "",
            Note = form.Note?.Trim()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã gửi yêu cầu xem phòng ngày {form.Date:dd/MM/yyyy} lúc {form.Time}. Chủ trọ sẽ liên hệ bạn sớm.";
        return Back(returnUrl, "/rooms");
    }

    [Authorize(Roles = nameof(UserRole.Tenant))]
    [HttpPost("phong/{id:int}/danh-gia")]
    public async Task<IActionResult> SubmitRoomReview(int id, int rating, string? content)
    {
        var roomExists = await _db.Rooms.AnyAsync(room =>
            room.Id == id && room.Status == RoomStatus.Approved);
        if (!roomExists) return NotFound();

        if (rating is < 1 or > 5 || string.IsNullOrWhiteSpace(content) || content.Trim().Length > 2000)
        {
            TempData["Error"] = "Vui lòng chọn số sao từ 1 đến 5 và nhập nội dung đánh giá (tối đa 2000 ký tự).";
            return Redirect($"/phong/{id}#reviews");
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        _db.RoomReviews.Add(new RoomReview
        {
            UserId = userId,
            RoomId = id,
            Rating = rating,
            Content = content.Trim(),
            IsPublished = false
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Cảm ơn bạn đã đánh giá. Nội dung sẽ hiển thị sau khi được quản trị viên duyệt.";
        return Redirect($"/phong/{id}#reviews");
    }

    // Liên hệ hỗ trợ
    [HttpPost("lien-he")]
    public async Task<IActionResult> LienHe(ContactForm form)
    {
        if (!ModelState.IsValid) return Fail(null, "/lien-he");

        _db.ContactMessages.Add(new ContactMessage
        {
            FullName = form.FullName.Trim(),
            Phone = form.Phone.Trim(),
            Content = form.Content.Trim()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "Cảm ơn bạn! Tin nhắn phản hồi đã được gửi thành công.";
        return Redirect("/lien-he");
    }

    // Đăng tin phòng trọ (cần đăng nhập)
    [Authorize(Roles = $"{nameof(UserRole.Owner)},{nameof(UserRole.Admin)}")]
    [HttpPost("dang-tin")]
    public async Task<IActionResult> DangTin(RoomForm form)
    {
        if (!ModelState.IsValid) return Fail(null, "/dang-tin");

        var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var maxImages = form.Package switch { "basic" => 10, "premium" => 20, _ => 5 };
        var images = (form.Images ?? new List<IFormFile>()).Where(f => f.Length > 0).ToList();
        if (images.Count > maxImages)
        {
            TempData["Error"] = $"Gói \"{form.Package}\" chỉ cho phép tối đa {maxImages} hình ảnh.";
            return Redirect("/dang-tin");
        }
        foreach (var img in images)
        {
            var ext = Path.GetExtension(img.FileName).ToLowerInvariant();
            if (!AllowedImageExt.Contains(ext) || img.Length > MaxImageBytes)
            {
                TempData["Error"] = $"Ảnh \"{img.FileName}\" không hợp lệ (chỉ JPG/PNG/WEBP, tối đa 5MB).";
                return Redirect("/dang-tin");
            }
        }

        var room = new Room
        {
            Title = form.Title.Trim(),
            RoomType = form.RoomType,
            Area = form.Area!.Value,
            Address = form.Address.Trim(),
            District = form.District,
            Description = form.Description?.Trim(),
            Rules = form.Rules?.Trim(),
            VideoUrl = form.VideoUrl?.Trim(),
            Price = form.Price!.Value,
            DepositMonths = form.DepositMonths ?? 2,
            ElectricPrice = form.ElectricPrice ?? 3500,
            WaterPrice = form.WaterPrice ?? 80000,
            InternetFee = form.InternetFee ?? 0,
            ParkingFee = form.ParkingFee ?? 150000,
            Amenities = form.Amenities is { Count: > 0 } ? string.Join(",", form.Amenities) : "",
            Package = form.Package,
            Status = RoomStatus.Pending,
            OwnerId = ownerId
        };

        if (images.Count > 0)
        {
            var dir = Path.Combine(_env.WebRootPath, "uploads", "rooms");
            Directory.CreateDirectory(dir);
            for (var i = 0; i < images.Count; i++)
            {
                var ext = Path.GetExtension(images[i].FileName).ToLowerInvariant();
                var name = $"{Guid.NewGuid():N}{ext}";
                await using (var fs = new FileStream(Path.Combine(dir, name), FileMode.Create))
                {
                    await images[i].CopyToAsync(fs);
                }
                room.Images.Add(new RoomImage { Path = $"/uploads/rooms/{name}", IsPrimary = i == 0 });
            }
        }

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Tin đăng đã được gửi! Đội ngũ sẽ xét duyệt trong 24h.";
        return Redirect("/");
    }

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost("quan-tri/phong/{id:int}/duyet")]
    public async Task<IActionResult> ApproveRoom(int id)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        room.Status = RoomStatus.Approved;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã duyệt tin đăng \"{room.Title}\".";
        return Redirect("/quan-ly");
    }

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost("quan-tri/phong/{id:int}/tu-choi")]
    public async Task<IActionResult> RejectRoom(int id)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        room.Status = RoomStatus.Rejected;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã từ chối tin đăng \"{room.Title}\".";
        return Redirect("/quan-ly");
    }

    // ================= helpers =================
    private IActionResult Back(string? returnUrl, string fallback)
        => Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : Redirect(fallback);

    private IActionResult Fail(string? returnUrl, string fallback)
    {
        var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m)).Distinct();
        TempData["Error"] = string.Join(" ", errors);
        return Back(returnUrl, fallback);
    }
}
