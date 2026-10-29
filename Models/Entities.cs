using System.ComponentModel.DataAnnotations;

namespace TroTot.Web.Models;

public enum UserRole
{
    Tenant = 0,   
    Owner = 1,   
    Admin = 2
}

public enum RoomStatus
{
    Pending = 0, 
    Approved = 1,
    Rejected = 2
}

public enum RoomAvailability
{
    Available = 0,      // Trống
    PendingDeposit = 1, // Đang chờ đặt cọc (Khóa tạm thời)
    Rented = 2          // Đã cho thuê
}

public enum LegalVerificationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum RepairStatus
{
    New = 0,
    InProgress = 1,
    Completed = 2
}

public class AppUser
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string FullName { get; set; } = "";

    [MaxLength(150)]
    public string Email { get; set; } = "";

    [MaxLength(20)]
    public string Phone { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public UserRole Role { get; set; } = UserRole.Tenant;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Room> Rooms { get; set; } = new();
}

public class Room
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(20)]
    public string RoomType { get; set; } = "single";

    public int Area { get; set; }

    [MaxLength(300)]
    public string Address { get; set; } = "";

    [MaxLength(60)]
    public string District { get; set; } = "";

    public string? Description { get; set; }
    public string? Rules { get; set; }
    public string? VideoUrl { get; set; }

    // Giá tính bằng VND
    public long Price { get; set; }
    public int DepositMonths { get; set; } = 2;
    public int ElectricPrice { get; set; } = 3500;
    public int WaterPrice { get; set; } = 80000;
    public int InternetFee { get; set; }
    public int ParkingFee { get; set; } = 150000;

    public string Amenities { get; set; } = "";

   
    [MaxLength(20)]
    public string Package { get; set; } = "free";

    public RoomStatus Status { get; set; } = RoomStatus.Pending;
    public RoomAvailability AvailabilityStatus { get; set; } = RoomAvailability.Available;
    public bool IsVerified { get; set; }
    [MaxLength(100)]
    public string? IdentityFrontDocument { get; set; }

    [MaxLength(100)]
    public string? IdentityBackDocument { get; set; }

    [MaxLength(100)]
    public string? PropertyDocument { get; set; }

    [MaxLength(100)]
    public string? RoomLegalDocument { get; set; }
    public LegalVerificationStatus LegalStatus { get; set; } = LegalVerificationStatus.Pending;
    public bool HasUnreadOwnerNotification { get; set; }

    public int OwnerId { get; set; }
    public AppUser? Owner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RoomImage> Images { get; set; } = new();
}

public class RoomImage
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }

    public string Path { get; set; } = "";
    public bool IsPrimary { get; set; }
}

public class ConsultationRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? District { get; set; }
    public int? Budget { get; set; }
    public string? RoomType { get; set; }
    public string? MoveInTime { get; set; }
    public string? Amenities { get; set; }
    public string? Workplace { get; set; }
    public string? Note { get; set; }
    public bool IsHandled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ViewingRequest
{
    public int Id { get; set; }
    public int? RoomId { get; set; }
    public string RoomName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTime Date { get; set; }
    public string Time { get; set; } = "";
    public string? Note { get; set; }
    public bool IsHandled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ContactMessage
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsHandled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RoomReview
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public AppUser? User { get; set; }
    public int? RoomId { get; set; }
    public Room? Room { get; set; }
    [Range(1, 5)]
    public int Rating { get; set; }
    [MaxLength(2000)]
    public string Content { get; set; } = "";
    [MaxLength(300)]
    public string? ImagePath { get; set; }
    public bool IsPublished { get; set; }
    public bool IsReviewed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Question
{
    public int Id { get; set; }
    public string Topic { get; set; } = "other";
    public string Content { get; set; } = "";
    public string? Answer { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RentalContract
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int RoomId { get; set; }
    public int TenantId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public long MonthlyRent { get; set; }
    public long DepositAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UtilityBill
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int RoomId { get; set; }
    public DateTime BillingMonth { get; set; }
    public int ElectricityKwh { get; set; }
    public int WaterM3 { get; set; }
    public long OtherFees { get; set; }
    public bool IsPaid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RepairRequest
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int RoomId { get; set; }
    [MaxLength(160)]
    public string Title { get; set; } = "";
    [MaxLength(2000)]
    public string Description { get; set; } = "";
    public RepairStatus Status { get; set; } = RepairStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RoomDeposit
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }
    public int TenantId { get; set; }
    public AppUser? Tenant { get; set; }
    
    public long Amount { get; set; }
    [MaxLength(80)]
    public string TransactionCode { get; set; } = "";
    public string OrderInfo { get; set; } = "";
    [MaxLength(100)]
    public string? VnPayTransactionId { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Success, Failed, Expired
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    [MaxLength(40)]
    public string? ReceiptCode { get; set; }
}
