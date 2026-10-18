using Microsoft.EntityFrameworkCore;
using TroTot.Web.Models;

namespace TroTot.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomImage> RoomImages => Set<RoomImage>();
    public DbSet<ConsultationRequest> ConsultationRequests => Set<ConsultationRequest>();
    public DbSet<ViewingRequest> ViewingRequests => Set<ViewingRequest>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<RoomReview> RoomReviews => Set<RoomReview>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<RentalContract> RentalContracts => Set<RentalContract>();
    public DbSet<UtilityBill> UtilityBills => Set<UtilityBill>();
    public DbSet<RepairRequest> RepairRequests => Set<RepairRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Phone).IsUnique();
        });

        b.Entity<Room>(e =>
        {
            e.HasOne(r => r.Owner).WithMany(u => u.Rooms).HasForeignKey(r => r.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Images).WithOne(i => i.Room).HasForeignKey(i => i.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.Status, r.District });
        });

        b.Entity<RoomReview>(e =>
        {
            e.HasOne(review => review.User).WithMany().HasForeignKey(review => review.UserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(review => review.Room).WithMany().HasForeignKey(review => review.RoomId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(review => new { review.IsPublished, review.CreatedAt });
            e.ToTable(table => table.HasCheckConstraint("CK_RoomReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5"));
        });

        b.Entity<ViewingRequest>(e =>
        {
            e.HasOne<Room>().WithMany().HasForeignKey(request => request.RoomId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(request => new { request.RoomId, request.IsHandled, request.CreatedAt });
        });

        b.Entity<RentalContract>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(contract => contract.OwnerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Room>().WithMany().HasForeignKey(contract => contract.RoomId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AppUser>().WithMany().HasForeignKey(contract => contract.TenantId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(contract => new { contract.OwnerId, contract.RoomId });
        });

        b.Entity<UtilityBill>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(bill => bill.OwnerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Room>().WithMany().HasForeignKey(bill => bill.RoomId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(bill => new { bill.OwnerId, bill.BillingMonth });
            e.HasIndex(bill => new { bill.OwnerId, bill.RoomId, bill.BillingMonth }).IsUnique();
        });

        b.Entity<RepairRequest>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(request => request.OwnerId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Room>().WithMany().HasForeignKey(request => request.RoomId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(request => new { request.OwnerId, request.Status, request.CreatedAt });
        });

        var seedDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        b.Entity<AppUser>().HasData(
            new AppUser
            {
                Id = 1,
                FullName = "Nguyễn Minh",
                Email = "owner@trotot.vn",
                Phone = "0901234567",
                PasswordHash = "seeded-owner",
                Role = UserRole.Owner,
                CreatedAt = seedDate
            }
        );

        b.Entity<Room>().HasData(
            new Room
            {
                Id = 1,
                Title = "Phòng Studio Mới Xây, View Đẹp",
                RoomType = "single",
                Area = 28,
                Address = "Số 24, ngõ 18, Phố Hoàng Quốc Việt",
                District = "Cầu Giấy",
                Description = "Phòng mới xây, thang máy, view đẹp, phù hợp sinh viên và người đi làm.",
                Rules = "Không hút thuốc trong phòng, giờ giấc tự do, giữ vệ sinh chung.",
                VideoUrl = "https://www.youtube.com/watch?v=QH2-TGUlwu4",
                Price = 4800000,
                DepositMonths = 2,
                ElectricPrice = 3500,
                WaterPrice = 80000,
                InternetFee = 150000,
                ParkingFee = 150000,
                Amenities = "Máy lạnh,Wifi,Gác lửng,Chỗ để xe",
                Package = "premium",
                Status = RoomStatus.Approved,
                IsVerified = true,
                OwnerId = 1,
                CreatedAt = seedDate.AddDays(-3)
            },
            new Room
            {
                Id = 2,
                Title = "Phòng Ở Ghép Sinh Viên, Giá Rẻ",
                RoomType = "shared",
                Area = 18,
                Address = "78 Trần Duy Hưng, P. Trung Hoà",
                District = "Đống Đa",
                Description = "Phòng ở ghép sạch sẽ, an ninh tốt, gần trường và trung tâm mua sắm.",
                Rules = "Giữ vệ sinh, không nuôi thú, giờ giấc linh hoạt.",
                Price = 2500000,
                DepositMonths = 1,
                ElectricPrice = 3500,
                WaterPrice = 70000,
                InternetFee = 100000,
                ParkingFee = 0,
                Amenities = "Wifi,Giờ giấc tự do",
                Package = "basic",
                Status = RoomStatus.Approved,
                IsVerified = true,
                OwnerId = 1,
                CreatedAt = seedDate.AddDays(-1)
            },
            new Room
            {
                Id = 3,
                Title = "Căn Hộ Mini Nội Thất 2PN",
                RoomType = "mini",
                Area = 40,
                Address = "128 Lê Văn Lương, P. Nhân Chính",
                District = "Nam Từ Liêm",
                Description = "Căn hộ mini đầy đủ nội thất, phù hợp gia đình trẻ hoặc người đi làm lâu năm.",
                Rules = "Không tổ chức tiệc lớn, gửi xe riêng, bảo trì đồ dùng đúng quy định.",
                Price = 6800000,
                DepositMonths = 2,
                ElectricPrice = 3800,
                WaterPrice = 90000,
                InternetFee = 200000,
                ParkingFee = 200000,
                Amenities = "Máy lạnh,Wifi,Nuôi thú cưng,Chỗ để xe",
                Package = "premium",
                Status = RoomStatus.Approved,
                IsVerified = true,
                OwnerId = 1,
                CreatedAt = seedDate.AddDays(-7)
            },
            new Room
            {
                Id = 4,
                Title = "Phòng Cao Cấp Gần Trường Đại Học",
                RoomType = "single",
                Area = 32,
                Address = "Đường Quang Trung, Phường Hà Cầu",
                District = "Hà Đông",
                Description = "Phòng có gác lửng, hệ thống máy lạnh trung tâm, khu sinh hoạt rộng.",
                Rules = "Phòng sạch sẽ, có máy giặt chung, không hút thuốc.",
                Price = 5500000,
                DepositMonths = 1,
                ElectricPrice = 4000,
                WaterPrice = 85000,
                InternetFee = 120000,
                ParkingFee = 50000,
                Amenities = "Máy lạnh,Wifi,Gác lửng,Giờ giấc tự do",
                Package = "basic",
                Status = RoomStatus.Pending,
                IsVerified = false,
                OwnerId = 1,
                CreatedAt = seedDate.AddDays(-2)
            },
            new Room
            {
                Id = 5,
                Title = "Phòng Khép Kín, An Ninh Tốt",
                RoomType = "single",
                Area = 22,
                Address = "91 Tô Ký, P. Trung Mỹ Tây",
                District = "Hà Đông",
                Description = "Phòng khép kín, an ninh 24/7, vị trí thuận tiện đi làm và đi học.",
                Rules = "Tự quản, không hút thuốc trong khu vực chung, giữ kín thông tin.",
                Price = 3200000,
                DepositMonths = 1,
                ElectricPrice = 3500,
                WaterPrice = 75000,
                InternetFee = 90000,
                ParkingFee = 0,
                Amenities = "Wifi,Chỗ để xe",
                Package = "free",
                Status = RoomStatus.Approved,
                IsVerified = true,
                OwnerId = 1,
                CreatedAt = seedDate.AddDays(-5)
            }
        );

        b.Entity<RoomImage>().HasData(
            new RoomImage { Id = 1, RoomId = 1, Path = "https://images.unsplash.com/photo-1505693416388-ac5ce068fe85?auto=format&fit=crop&w=1200&q=80", IsPrimary = true },
            new RoomImage { Id = 2, RoomId = 1, Path = "https://images.unsplash.com/photo-1494526585095-c41746248156?auto=format&fit=crop&w=1200&q=80", IsPrimary = false },
            new RoomImage { Id = 3, RoomId = 2, Path = "https://images.unsplash.com/photo-1494526585095-c41746248156?auto=format&fit=crop&w=1200&q=80", IsPrimary = true },
            new RoomImage { Id = 4, RoomId = 3, Path = "https://images.unsplash.com/photo-1484154218962-a197022b5858?auto=format&fit=crop&w=1200&q=80", IsPrimary = true },
            new RoomImage { Id = 5, RoomId = 4, Path = "https://images.unsplash.com/photo-1448630360428-65456885c650?auto=format&fit=crop&w=1200&q=80", IsPrimary = true },
            new RoomImage { Id = 6, RoomId = 5, Path = "https://images.unsplash.com/photo-1502672260266-1c1ef2d93688?auto=format&fit=crop&w=1200&q=80", IsPrimary = true }
        );

        // Dữ liệu mẫu cho phần Hỏi đáp ở trang Tư vấn
        b.Entity<Question>().HasData(
            new Question
            {
                Id = 1, Topic = "contract", CreatedAt = seedDate,
                Content = "Có nên đặt cọc giữ chỗ khi chưa trực tiếp vào xem bên trong phòng không?",
                Answer = "Tuyệt đối KHÔNG. Đây là chiêu trò lừa đảo phổ biến nhất hiện nay. Kẻ gian thường đăng ảnh phòng đẹp giá rẻ bất ngờ, yêu cầu bạn chuyển khoản 500k - 2 triệu để \"giữ chân\" rồi chặn liên lạc. Chỉ đặt cọc khi bạn đã đến tận nơi, xem phòng thực tế, gặp trực tiếp chủ nhà hoặc người có giấy tờ ủy quyền hợp pháp."
            },
            new Question
            {
                Id = 2, Topic = "price", CreatedAt = seedDate,
                Content = "Tiền điện 3.500đ đến 4.000đ/kWh ở phòng trọ có bị tính đắt hơn quy định không?",
                Answer = "Hiện nay tại các thành phố lớn như TP.HCM và Hà Nội, mức giá điện thương lượng phổ biến tại các nhà trọ tư nhân dao động từ 3.500đ – 4.000đ/kWh (đã bù trừ cho hao hụt đường dây, bơm nước, đèn chiếu sáng hành lang và camera). Nếu chủ trọ thu trên 4.500đ/kWh, bạn nên cân nhắc đàm phán lại hoặc tìm phòng khác có công tơ điện tử riêng biệt."
            },
            new Question
            {
                Id = 3, Topic = "contract", CreatedAt = seedDate,
                Content = "Chủ nhà yêu cầu cọc 2 tháng tiền phòng có đúng luật không?",
                Answer = "Pháp luật hiện hành không quy định mức trần tiền đặt cọc thuê nhà mà do 2 bên tự thỏa thuận. Thông thường đối với phòng trọ bình dân chỉ cần cọc 1 tháng; đối với căn hộ mini hoặc studio có nhiều đồ dùng nội thất giá trị cao (máy lạnh, tủ lạnh, tivi, sofa), chủ trọ thường yêu cầu cọc 1.5 – 2 tháng để bảo đảm tài sản. Bạn cần yêu cầu biên nhận cọc và điều khoản hoàn trả rõ ràng trong hợp đồng."
            },
            new Question
            {
                Id = 4, Topic = "other", CreatedAt = seedDate,
                Content = "Nên chọn phòng trọ có gác lửng hay phòng không gác?",
                Answer = "Phòng có gác lửng giúp bạn tăng gấp đôi không gian sử dụng (tầng trệt làm phòng khách/bếp/bàn học, trên gác làm chỗ ngủ riêng tư). Tuy nhiên nếu ở tầng trên cùng mái tôn, gác lửng có thể khá nóng vào buổi trưa mùa hè. Bạn nên ưu tiên phòng có gắn máy lạnh hoặc trần cao từ 3.2m trở lên."
            }
        );
    }
}
