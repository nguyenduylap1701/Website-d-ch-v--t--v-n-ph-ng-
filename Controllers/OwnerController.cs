using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;
using TroTot.Web.Services;
using TroTot.Web.ViewModels;

namespace TroTot.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Owner))]
[Route("chu-tro")]
public class OwnerController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<OwnerController> _logger;

    public OwnerController(
        AppDbContext db,
        IPasswordHasher<AppUser> hasher,
        IWebHostEnvironment environment,
        ILogger<OwnerController> logger)
    {
        _db = db;
        _hasher = hasher;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet("ho-so")]
    public async Task<IActionResult> Profile()
    {
        var owner = await CurrentOwnerAsync();
        return View(new OwnerProfileViewModel { FullName = owner.FullName, Email = owner.Email, Phone = owner.Phone });
    }

    [HttpPost("ho-so")]
    public async Task<IActionResult> Profile(OwnerProfileViewModel form)
    {
        var owner = await CurrentOwnerAsync();
        var email = form.Email.Trim().ToLowerInvariant();
        var phone = NormalizePhone(form.Phone);
        if (await _db.Users.AnyAsync(user => user.Id != owner.Id && user.Email == email))
            ModelState.AddModelError(nameof(form.Email), "Email này đã được tài khoản khác sử dụng.");
        if (await _db.Users.AnyAsync(user => user.Id != owner.Id && user.Phone == phone))
            ModelState.AddModelError(nameof(form.Phone), "Số điện thoại này đã được tài khoản khác sử dụng.");
        if (!ModelState.IsValid) return View(form);

        owner.FullName = form.FullName.Trim();
        owner.Email = email;
        owner.Phone = phone;
        await _db.SaveChangesAsync();
        await RefreshSignInAsync(owner);
        TempData["Success"] = "Đã cập nhật hồ sơ cá nhân.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet("doi-mat-khau")]
    public IActionResult ChangePassword() => View(new OwnerPasswordViewModel());

    [HttpPost("doi-mat-khau")]
    public async Task<IActionResult> ChangePassword(OwnerPasswordViewModel form)
    {
        var owner = await CurrentOwnerAsync();
        if (_hasher.VerifyHashedPassword(owner, owner.PasswordHash, form.CurrentPassword) == PasswordVerificationResult.Failed)
            ModelState.AddModelError(nameof(form.CurrentPassword), "Mật khẩu hiện tại không đúng.");
        if (!ModelState.IsValid) return View(form);

        owner.PasswordHash = _hasher.HashPassword(owner, form.NewPassword);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã đổi mật khẩu.";
        return RedirectToAction(nameof(ChangePassword));
    }

    [HttpGet("tin-nhan")]
    public async Task<IActionResult> Messages()
    {
        var roomIds = OwnerRooms().Select(room => room.Id);
        var messages = await _db.ViewingRequests
            .Where(request => request.RoomId.HasValue && roomIds.Contains(request.RoomId.Value))
            .OrderByDescending(request => request.CreatedAt)
            .ToListAsync();
        return View(messages);
    }

    [HttpPost("tin-nhan/{id:int}/trang-thai")]
    public async Task<IActionResult> ToggleMessageStatus(int id)
    {
        var request = await _db.ViewingRequests.FirstOrDefaultAsync(item =>
            item.Id == id && item.RoomId.HasValue &&
            _db.Rooms.Any(room => room.OwnerId == OwnerId && room.Id == item.RoomId.Value));
        if (request is null) return NotFound();
        request.IsHandled = !request.IsHandled;
        await _db.SaveChangesAsync();
        TempData["Success"] = request.IsHandled ? "Đã đánh dấu yêu cầu đã xử lý." : "Đã mở lại yêu cầu.";
        return RedirectToAction(nameof(Messages));
    }

    [HttpGet("nguoi-thue")]
    public async Task<IActionResult> Tenants()
    {
        var tenants = await _db.RentalContracts
            .Where(contract => contract.OwnerId == OwnerId)
            .Select(contract => contract.TenantId)
            .Distinct()
            .Join(_db.Users, id => id, tenant => tenant.Id, (id, tenant) => tenant)
            .OrderBy(tenant => tenant.FullName)
            .ToListAsync();
        return View(tenants);
    }

    [HttpGet("phong")]
    public async Task<IActionResult> Rooms()
    {
        var rooms = await OwnerRooms()
            .OrderByDescending(room => room.CreatedAt)
            .ToListAsync();
        var unreadRooms = rooms.Where(room => room.HasUnreadOwnerNotification).ToList();
        if (unreadRooms.Count > 0)
        {
            foreach (var room in unreadRooms)
                room.HasUnreadOwnerNotification = false;
            await _db.SaveChangesAsync();
        }

        return View(rooms);
    }

    [HttpGet("phong/{id:int}/sua")]
    public async Task<IActionResult> EditRoom(int id)
    {
        var room = await OwnerRooms().Include(item => item.Images)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (room is null) return NotFound();

        return View(new EditRoomForm
        {
            Title = room.Title,
            RoomType = room.RoomType,
            Area = room.Area,
            Address = room.Address,
            District = room.District,
            Description = room.Description,
            Rules = room.Rules,
            VideoUrl = room.VideoUrl,
            Price = room.Price,
            DepositMonths = room.DepositMonths,
            ElectricPrice = room.ElectricPrice,
            WaterPrice = room.WaterPrice,
            InternetFee = room.InternetFee,
            ParkingFee = room.ParkingFee,
            Amenities = room.Amenities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Package = room.Package,
            ViewRoomImages = room.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.Id).ToList(),
            HasIdentityFront = !string.IsNullOrWhiteSpace(room.IdentityFrontDocument),
            HasIdentityBack = !string.IsNullOrWhiteSpace(room.IdentityBackDocument),
            HasPropertyDocument = !string.IsNullOrWhiteSpace(room.PropertyDocument),
            HasRoomLegalDocument = !string.IsNullOrWhiteSpace(room.RoomLegalDocument)
        });
    }

    [HttpPost("phong/{id:int}/sua")]
    [RequestSizeLimit(150_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 150_000_000)]
    public async Task<IActionResult> EditRoom(int id, EditRoomForm form)
    {
        var room = await OwnerRooms().Include(item => item.Images)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (room is null) return NotFound();

        var removedImages = room.Images.Where(image => form.RemoveImageIds.Contains(image.Id)).ToList();
        if (removedImages.Count != form.RemoveImageIds.Distinct().Count())
            ModelState.AddModelError(nameof(form.RemoveImageIds), "Danh sách ảnh cần xóa không hợp lệ.");

        var uploads = (form.Images ?? new List<IFormFile>()).Where(file => file.Length > 0).ToList();
        var maxImages = form.Package switch { "basic" => 10, "premium" => 20, _ => 5 };
        if (room.Images.Count - removedImages.Count + uploads.Count > maxImages)
            ModelState.AddModelError(nameof(form.Images), $"Gói đăng tin chỉ cho phép tối đa {maxImages} ảnh.");

        foreach (var image in uploads)
        {
            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(extension) || image.Length > 5 * 1024 * 1024)
                ModelState.AddModelError(nameof(form.Images), $"Ảnh \"{image.FileName}\" không hợp lệ (JPG/PNG/WEBP, tối đa 5MB).");
        }

        var legalUploads = new[]
        {
            (File: form.IdentityFront, Label: "CCCD mặt trước"),
            (File: form.IdentityBack, Label: "CCCD mặt sau"),
            (File: form.PropertyDocument, Label: "Sổ đỏ/hợp đồng thuê lại"),
            (File: form.RoomLegalDocument, Label: "Giấy tờ phòng thuê")
        };
        foreach (var (file, label) in legalUploads)
        {
            if (file is null || file.Length == 0) continue;
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp", ".pdf" }.Contains(extension) || file.Length > 10 * 1024 * 1024)
                ModelState.AddModelError(string.Empty, $"{label}: chỉ chấp nhận JPG/PNG/WEBP/PDF, tối đa 10MB.");
        }

        var retainedDocuments = new[]
        {
            (Old: room.IdentityFrontDocument, New: form.IdentityFront),
            (Old: room.IdentityBackDocument, New: form.IdentityBack),
            (Old: room.PropertyDocument, New: form.PropertyDocument),
            (Old: room.RoomLegalDocument, New: form.RoomLegalDocument)
        };
        if (retainedDocuments.Any(document =>
                string.IsNullOrWhiteSpace(document.Old) && (document.New is null || document.New.Length == 0)))
            ModelState.AddModelError(string.Empty, "Hãy tải lên các giấy tờ pháp lý còn thiếu trước khi cập nhật tin.");

        if (!ModelState.IsValid)
        {
            PopulateEditRoomViewModel(form, room);
            return View(form);
        }

        var previousLegalDocuments = new List<string?>();
        if (form.IdentityFront is { Length: > 0 })
        {
            previousLegalDocuments.Add(room.IdentityFrontDocument);
            room.IdentityFrontDocument = await SaveLegalDocumentAsync(form.IdentityFront);
        }
        if (form.IdentityBack is { Length: > 0 })
        {
            previousLegalDocuments.Add(room.IdentityBackDocument);
            room.IdentityBackDocument = await SaveLegalDocumentAsync(form.IdentityBack);
        }
        if (form.PropertyDocument is { Length: > 0 })
        {
            previousLegalDocuments.Add(room.PropertyDocument);
            room.PropertyDocument = await SaveLegalDocumentAsync(form.PropertyDocument);
        }
        if (form.RoomLegalDocument is { Length: > 0 })
        {
            previousLegalDocuments.Add(room.RoomLegalDocument);
            room.RoomLegalDocument = await SaveLegalDocumentAsync(form.RoomLegalDocument);
        }

        _db.RoomImages.RemoveRange(removedImages);
        foreach (var image in removedImages)
            room.Images.Remove(image);

        var roomImageDirectory = Path.Combine(_environment.WebRootPath, "uploads", "rooms");
        Directory.CreateDirectory(roomImageDirectory);
        foreach (var (image, index) in uploads.Select((file, index) => (file, index)))
        {
            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{extension}";
            await using var stream = new FileStream(
                Path.Combine(roomImageDirectory, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await image.CopyToAsync(stream, HttpContext.RequestAborted);
            room.Images.Add(new RoomImage
            {
                Path = $"/uploads/rooms/{fileName}",
                IsPrimary = room.Images.Count == 0 && index == 0
            });
        }

        if (room.Images.Count > 0 && room.Images.All(image => !image.IsPrimary))
            room.Images.OrderBy(image => image.Id).First().IsPrimary = true;

        room.Title = form.Title.Trim();
        room.RoomType = form.RoomType;
        room.Area = form.Area!.Value;
        room.Address = form.Address.Trim();
        room.District = form.District.Trim();
        room.Description = form.Description?.Trim();
        room.Rules = form.Rules?.Trim();
        room.VideoUrl = form.VideoUrl?.Trim();
        room.Price = form.Price!.Value;
        room.DepositMonths = form.DepositMonths ?? 2;
        room.ElectricPrice = form.ElectricPrice ?? 3500;
        room.WaterPrice = form.WaterPrice ?? 80000;
        room.InternetFee = form.InternetFee ?? 0;
        room.ParkingFee = form.ParkingFee ?? 150000;
        room.Amenities = form.Amenities is { Count: > 0 } ? string.Join(",", form.Amenities) : "";
        room.Package = form.Package;
        room.Status = RoomStatus.Pending;
        room.LegalStatus = LegalVerificationStatus.Pending;
        room.IsVerified = false;
        room.HasUnreadOwnerNotification = false;
        await _db.SaveChangesAsync();

        RoomFileCleanup.DeleteImages(removedImages, _environment, _logger);
        RoomFileCleanup.DeleteLegalDocuments(previousLegalDocuments, _environment, _logger);
        TempData["Success"] = "Đã cập nhật tin đăng. Tin đang chờ admin duyệt lại.";
        return RedirectToAction(nameof(Rooms));
    }

    private void PopulateEditRoomViewModel(EditRoomForm form, Room room)
    {
        form.ViewRoomImages = room.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.Id).ToList();
        form.HasIdentityFront = !string.IsNullOrWhiteSpace(room.IdentityFrontDocument);
        form.HasIdentityBack = !string.IsNullOrWhiteSpace(room.IdentityBackDocument);
        form.HasPropertyDocument = !string.IsNullOrWhiteSpace(room.PropertyDocument);
        form.HasRoomLegalDocument = !string.IsNullOrWhiteSpace(room.RoomLegalDocument);
    }

    private async Task<string> SaveLegalDocumentAsync(IFormFile document)
    {
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var directory = Path.Combine(_environment.ContentRootPath, "App_Data", "verification-documents");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await document.CopyToAsync(stream, HttpContext.RequestAborted);
        return fileName;
    }

    [HttpPost("phong/{id:int}/xoa")]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await OwnerRooms()
            .Include(item => item.Images)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (room is null) return NotFound();

        if (await _db.RentalContracts.AnyAsync(contract => contract.RoomId == id) ||
            await _db.UtilityBills.AnyAsync(bill => bill.RoomId == id) ||
            await _db.RepairRequests.AnyAsync(request => request.RoomId == id))
        {
            TempData["Error"] = "Không thể xóa tin này vì phòng đang có hợp đồng, hóa đơn hoặc yêu cầu sửa chữa được lưu.";
            return RedirectToAction(nameof(Rooms));
        }

        var reviews = await _db.RoomReviews.Where(review => review.RoomId == id).ToListAsync();
        var viewingRequests = await _db.ViewingRequests.Where(request => request.RoomId == id).ToListAsync();
        _db.RoomReviews.RemoveRange(reviews);
        _db.ViewingRequests.RemoveRange(viewingRequests);
        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync();

        RoomFileCleanup.Delete(room, _environment, _logger);
        TempData["Success"] = $"Đã xóa tin \"{room.Title}\".";
        return RedirectToAction(nameof(Rooms));
    }

    [HttpGet("hop-dong")]
    public async Task<IActionResult> Contracts()
    {
        await PopulateContractOptionsAsync();
        return View(await _db.RentalContracts.Where(contract => contract.OwnerId == OwnerId)
            .OrderByDescending(contract => contract.CreatedAt).ToListAsync());
    }

    [HttpPost("hop-dong")]
    public async Task<IActionResult> CreateContract(RentalContractForm form)
    {
        if (!await _db.Rooms.AnyAsync(room => room.Id == form.RoomId && room.OwnerId == OwnerId))
            ModelState.AddModelError(nameof(form.RoomId), "Phòng không thuộc tài khoản của bạn.");
        if (!await _db.Users.AnyAsync(user => user.Id == form.TenantId && user.Role == UserRole.Tenant && user.IsActive))
            ModelState.AddModelError(nameof(form.TenantId), "Người thuê không hợp lệ.");
        if (form.EndDate.HasValue && form.EndDate.Value.Date < form.StartDate.Date)
            ModelState.AddModelError(nameof(form.EndDate), "Ngày kết thúc phải sau ngày bắt đầu.");
        if (!ModelState.IsValid)
        {
            await PopulateContractOptionsAsync();
            return View("Contracts", await _db.RentalContracts.Where(contract => contract.OwnerId == OwnerId)
                .OrderByDescending(contract => contract.CreatedAt).ToListAsync());
        }

        _db.RentalContracts.Add(new RentalContract
        {
            OwnerId = OwnerId,
            RoomId = form.RoomId,
            TenantId = form.TenantId,
            StartDate = form.StartDate.Date,
            EndDate = form.EndDate?.Date,
            MonthlyRent = form.MonthlyRent,
            DepositAmount = form.DepositAmount
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo hợp đồng thuê.";
        return RedirectToAction(nameof(Contracts));
    }

    [HttpGet("hoa-don")]
    public async Task<IActionResult> Bills()
    {
        ViewBag.Rooms = await OwnerRooms().OrderBy(room => room.Title).ToListAsync();
        ViewBag.RoomTitles = await _db.Rooms.Where(room => room.OwnerId == OwnerId).ToDictionaryAsync(room => room.Id, room => room.Title);
        return View(await _db.UtilityBills.Where(bill => bill.OwnerId == OwnerId)
            .OrderByDescending(bill => bill.BillingMonth).ToListAsync());
    }

    [HttpPost("hoa-don")]
    public async Task<IActionResult> CreateBill(UtilityBillForm form)
    {
        if (!await _db.Rooms.AnyAsync(room => room.Id == form.RoomId && room.OwnerId == OwnerId))
            ModelState.AddModelError(nameof(form.RoomId), "Phòng không thuộc tài khoản của bạn.");
        var billingMonth = new DateTime(form.BillingMonth.Year, form.BillingMonth.Month, 1);
        if (await _db.UtilityBills.AnyAsync(bill =>
                bill.OwnerId == OwnerId && bill.RoomId == form.RoomId && bill.BillingMonth == billingMonth))
            ModelState.AddModelError(nameof(form.BillingMonth), "Phòng này đã có hóa đơn cho tháng được chọn.");
        if (!ModelState.IsValid)
        {
            ViewBag.Rooms = await OwnerRooms().OrderBy(room => room.Title).ToListAsync();
            ViewBag.RoomTitles = await _db.Rooms.Where(room => room.OwnerId == OwnerId).ToDictionaryAsync(room => room.Id, room => room.Title);
            return View("Bills", await _db.UtilityBills.Where(bill => bill.OwnerId == OwnerId)
                .OrderByDescending(bill => bill.BillingMonth).ToListAsync());
        }

        _db.UtilityBills.Add(new UtilityBill
        {
            OwnerId = OwnerId,
            RoomId = form.RoomId,
            BillingMonth = billingMonth,
            ElectricityKwh = form.ElectricityKwh,
            WaterM3 = form.WaterM3,
            OtherFees = form.OtherFees
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo hóa đơn điện nước.";
        return RedirectToAction(nameof(Bills));
    }

    [HttpPost("hoa-don/{id:int}/thanh-toan")]
    public async Task<IActionResult> ToggleBillStatus(int id)
    {
        var bill = await _db.UtilityBills.FirstOrDefaultAsync(item => item.Id == id && item.OwnerId == OwnerId);
        if (bill is null) return NotFound();
        bill.IsPaid = !bill.IsPaid;
        await _db.SaveChangesAsync();
        TempData["Success"] = bill.IsPaid ? "Đã xác nhận hóa đơn đã thanh toán." : "Đã chuyển hóa đơn về chưa thanh toán.";
        return RedirectToAction(nameof(Bills));
    }

    [HttpGet("sua-chua")]
    public async Task<IActionResult> Repairs()
    {
        ViewBag.Rooms = await OwnerRooms().OrderBy(room => room.Title).ToListAsync();
        ViewBag.RoomTitles = await _db.Rooms.Where(room => room.OwnerId == OwnerId).ToDictionaryAsync(room => room.Id, room => room.Title);
        return View(await _db.RepairRequests.Where(request => request.OwnerId == OwnerId)
            .OrderByDescending(request => request.CreatedAt).ToListAsync());
    }

    [HttpPost("sua-chua")]
    public async Task<IActionResult> CreateRepair(RepairRequestForm form)
    {
        if (!await _db.Rooms.AnyAsync(room => room.Id == form.RoomId && room.OwnerId == OwnerId))
            ModelState.AddModelError(nameof(form.RoomId), "Phòng không thuộc tài khoản của bạn.");
        if (!ModelState.IsValid)
        {
            ViewBag.Rooms = await OwnerRooms().OrderBy(room => room.Title).ToListAsync();
            ViewBag.RoomTitles = await _db.Rooms.Where(room => room.OwnerId == OwnerId).ToDictionaryAsync(room => room.Id, room => room.Title);
            return View("Repairs", await _db.RepairRequests.Where(request => request.OwnerId == OwnerId)
                .OrderByDescending(request => request.CreatedAt).ToListAsync());
        }

        _db.RepairRequests.Add(new RepairRequest
        {
            OwnerId = OwnerId,
            RoomId = form.RoomId,
            Title = form.Title.Trim(),
            Description = form.Description.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã ghi nhận yêu cầu sửa chữa.";
        return RedirectToAction(nameof(Repairs));
    }

    [HttpPost("sua-chua/{id:int}/trang-thai")]
    public async Task<IActionResult> SetRepairStatus(int id, RepairStatus status)
    {
        if (!Enum.IsDefined(status)) return BadRequest();
        var repair = await _db.RepairRequests.FirstOrDefaultAsync(item => item.Id == id && item.OwnerId == OwnerId);
        if (repair is null) return NotFound();
        repair.Status = status;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật trạng thái yêu cầu sửa chữa.";
        return RedirectToAction(nameof(Repairs));
    }

    [HttpGet("bao-cao")]
    public async Task<IActionResult> Reports()
    {
        var rooms = OwnerRooms();
        var report = new OwnerReportViewModel
        {
            TotalRooms = await rooms.CountAsync(),
            ActiveRooms = await rooms.CountAsync(room => room.Status == RoomStatus.Approved),
            PendingRooms = await rooms.CountAsync(room => room.Status == RoomStatus.Pending),
            TotalTenants = await _db.RentalContracts.Where(contract => contract.OwnerId == OwnerId)
                .Select(contract => contract.TenantId).Distinct().CountAsync(),
            ActiveContracts = await _db.RentalContracts.CountAsync(contract =>
                contract.OwnerId == OwnerId && (!contract.EndDate.HasValue || contract.EndDate >= DateTime.Today)),
            UnpaidBills = await _db.UtilityBills.CountAsync(bill => bill.OwnerId == OwnerId && !bill.IsPaid),
            OpenRepairs = await _db.RepairRequests.CountAsync(request =>
                request.OwnerId == OwnerId && request.Status != RepairStatus.Completed),
            MonthlyRevenue = await _db.RentalContracts.Where(contract =>
                    contract.OwnerId == OwnerId && (!contract.EndDate.HasValue || contract.EndDate >= DateTime.Today))
                .SumAsync(contract => (long?)contract.MonthlyRent) ?? 0
        };
        return View(report);
    }

    private int OwnerId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IQueryable<Room> OwnerRooms() => _db.Rooms.Where(room => room.OwnerId == OwnerId);

    private async Task<AppUser> CurrentOwnerAsync()
        => await _db.Users.SingleAsync(user => user.Id == OwnerId && user.Role == UserRole.Owner);

    private async Task PopulateContractOptionsAsync()
    {
        ViewBag.Rooms = await OwnerRooms().OrderBy(room => room.Title).ToListAsync();
        ViewBag.Tenants = await _db.Users.Where(user => user.Role == UserRole.Tenant && user.IsActive)
            .OrderBy(user => user.FullName).ToListAsync();
        ViewBag.RoomTitles = await _db.Rooms.Where(room => room.OwnerId == OwnerId)
            .ToDictionaryAsync(room => room.Id, room => room.Title);
        ViewBag.TenantNames = await _db.Users.Where(user => user.Role == UserRole.Tenant)
            .ToDictionaryAsync(user => user.Id, user => user.FullName);
    }

    private async Task RefreshSignInAsync(AppUser owner)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, owner.Id.ToString()),
            new Claim(ClaimTypes.Name, owner.FullName),
            new Claim(ClaimTypes.Email, owner.Email),
            new Claim(ClaimTypes.Role, owner.Role.ToString())
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    private static string NormalizePhone(string phone)
        => new(phone.Where(character => char.IsDigit(character) || character == '+').ToArray());
}
