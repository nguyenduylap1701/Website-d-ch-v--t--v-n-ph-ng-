using System.ComponentModel.DataAnnotations;
using TroTot.Web.Models;

namespace TroTot.Web.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email hoặc số điện thoại.")]
    public string LoginId { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên."), StringLength(100)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại."),
     RegularExpression(@"^(0|\+84)\d{9}$", ErrorMessage = "Số điện thoại không hợp lệ (ví dụ 0901234567).")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."),
     MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>tenant | owner</summary>
    public string Role { get; set; } = "tenant";

    [Range(typeof(bool), "true", "true", ErrorMessage = "Bạn cần đồng ý với Điều khoản dịch vụ.")]
    public bool AgreeTerms { get; set; }
}

public class ConsultationForm
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(100)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại."), StringLength(20)]
    public string Phone { get; set; } = "";

    public string? District { get; set; }
    public int? Budget { get; set; }
    public string? RoomType { get; set; }
    public string? MoveInTime { get; set; }
    public List<string>? Amenities { get; set; }
    public string? Workplace { get; set; }
    public string? Note { get; set; }
}

public class ViewingForm
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(100)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại."), StringLength(20)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng chọn ngày xem.")]
    public DateTime? Date { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn giờ xem.")]
    public string Time { get; set; } = "";

    public string? RoomName { get; set; }
    public string? Note { get; set; }
}

public class ContactForm
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(100)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại."), StringLength(20)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập nội dung."), StringLength(2000)]
    public string Content { get; set; } = "";
}

public class QuestionForm
{
    public string Topic { get; set; } = "other";

    [Required(ErrorMessage = "Vui lòng nhập nội dung câu hỏi."), StringLength(1000)]
    public string Content { get; set; } = "";
}

public class RoomListingForm
{
    [Required(ErrorMessage = "Vui lòng nhập tiêu đề tin đăng."), StringLength(200)]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng chọn loại phòng.")]
    public string RoomType { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập diện tích."), Range(1, 1000, ErrorMessage = "Diện tích không hợp lệ.")]
    public int? Area { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ."), StringLength(300)]
    public string Address { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng chọn quận/huyện.")]
    public string District { get; set; } = "";

    public string? Description { get; set; }
    public string? Rules { get; set; }
    public string? VideoUrl { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giá thuê."), Range(100000, 1000000000, ErrorMessage = "Giá thuê không hợp lệ.")]
    public long? Price { get; set; }

    public int? DepositMonths { get; set; }
    public int? ElectricPrice { get; set; }
    public int? WaterPrice { get; set; }
    public int? InternetFee { get; set; }
    public int? ParkingFee { get; set; }

    public List<string>? Amenities { get; set; }
    public string Package { get; set; } = "free";
    public List<IFormFile>? Images { get; set; }
}

public class RoomForm : RoomListingForm
{

    [Required(ErrorMessage = "Vui lòng tải ảnh CCCD mặt trước.")]
    public IFormFile? IdentityFront { get; set; }

    [Required(ErrorMessage = "Vui lòng tải ảnh CCCD mặt sau.")]
    public IFormFile? IdentityBack { get; set; }

    [Required(ErrorMessage = "Vui lòng tải sổ đỏ hoặc hợp đồng thuê lại.")]
    public IFormFile? PropertyDocument { get; set; }

    [Required(ErrorMessage = "Vui lòng tải giấy tờ liên quan đến phòng thuê.")]
    public IFormFile? RoomLegalDocument { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Bạn cần xác nhận thông tin và giấy tờ là chính xác.")]
    public bool LegalDeclaration { get; set; }
}

public class EditRoomForm : RoomListingForm
{
    public List<int> RemoveImageIds { get; set; } = new();
    public List<RoomImage> ViewRoomImages { get; set; } = new();
    public bool HasIdentityFront { get; set; }
    public bool HasIdentityBack { get; set; }
    public bool HasPropertyDocument { get; set; }
    public bool HasRoomLegalDocument { get; set; }
    public IFormFile? IdentityFront { get; set; }
    public IFormFile? IdentityBack { get; set; }
    public IFormFile? PropertyDocument { get; set; }
    public IFormFile? RoomLegalDocument { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Bạn cần xác nhận thông tin cập nhật là chính xác.")]
    public bool ConfirmAccuracy { get; set; }
}

public class TuVanPageViewModel
{
    public List<Question> Questions { get; set; } = new();
}

public class OwnerProfileViewModel
{
    [Required, StringLength(100)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = "";

    [Required, RegularExpression(@"^(0|\+84)\d{9}$")]
    public string Phone { get; set; } = "";
}

public class OwnerPasswordViewModel
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = "";

    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = "";
}

public class RentalContractForm
{
    [Range(1, int.MaxValue)]
    public int RoomId { get; set; }

    [Range(1, int.MaxValue)]
    public int TenantId { get; set; }

    [Required]
    public DateTime StartDate { get; set; } = DateTime.Today;

    public DateTime? EndDate { get; set; }

    [Range(0, 1_000_000_000)]
    public long MonthlyRent { get; set; }

    [Range(0, 1_000_000_000)]
    public long DepositAmount { get; set; }
}

public class UtilityBillForm
{
    [Range(1, int.MaxValue)]
    public int RoomId { get; set; }

    [Required]
    public DateTime BillingMonth { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [Range(0, 1_000_000)]
    public int ElectricityKwh { get; set; }

    [Range(0, 1_000_000)]
    public int WaterM3 { get; set; }

    [Range(0, 1_000_000_000)]
    public long OtherFees { get; set; }
}

public class RepairRequestForm
{
    [Range(1, int.MaxValue)]
    public int RoomId { get; set; }

    [Required, StringLength(160)]
    public string Title { get; set; } = "";

    [Required, StringLength(2000)]
    public string Description { get; set; } = "";
}

public class OwnerReportViewModel
{
    public int TotalRooms { get; set; }
    public int ActiveRooms { get; set; }
    public int PendingRooms { get; set; }
    public int TotalTenants { get; set; }
    public int ActiveContracts { get; set; }
    public int UnpaidBills { get; set; }
    public int OpenRepairs { get; set; }
    public long MonthlyRevenue { get; set; }
}
