using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;

var builder = WebApplication.CreateBuilder(args);

// Mọi action POST tự động kiểm tra AntiForgery token
builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default") ??
        "Server=localhost;Database=TroTotDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;"));

builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/dang-nhap";
        o.AccessDeniedPath = "/dang-nhap";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.Name = "TroTot.Auth";
        o.Events.OnValidatePrincipal = async context =>
        {
            var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = int.TryParse(userId, out var id)
                ? await db.Users.AsNoTracking().SingleOrDefaultAsync(account => account.Id == id)
                : null;
            var tokenRole = context.Principal?.FindFirstValue(ClaimTypes.Role);
            if (user is null || !user.IsActive || tokenRole != user.Role.ToString())
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

var app = builder.Build();

// Tạo database + dữ liệu mẫu nếu chưa có
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH(N'dbo.Users', N'IsActive') IS NULL
            ALTER TABLE dbo.Users ADD IsActive bit NOT NULL
                CONSTRAINT DF_Users_IsActive DEFAULT (1);

        IF COL_LENGTH(N'dbo.ViewingRequests', N'RoomId') IS NULL
            ALTER TABLE dbo.ViewingRequests ADD RoomId int NULL;

        IF COL_LENGTH(N'dbo.Rooms', N'Latitude') IS NULL
            ALTER TABLE dbo.Rooms ADD Latitude float NULL;

        IF COL_LENGTH(N'dbo.Rooms', N'Longitude') IS NULL
            ALTER TABLE dbo.Rooms ADD Longitude float NULL;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.foreign_key_columns AS fkColumn
            INNER JOIN sys.columns AS parentColumn
                ON parentColumn.object_id = fkColumn.parent_object_id
                AND parentColumn.column_id = fkColumn.parent_column_id
            INNER JOIN sys.columns AS referencedColumn
                ON referencedColumn.object_id = fkColumn.referenced_object_id
                AND referencedColumn.column_id = fkColumn.referenced_column_id
            WHERE fkColumn.parent_object_id = OBJECT_ID(N'dbo.ViewingRequests')
                AND fkColumn.referenced_object_id = OBJECT_ID(N'dbo.Rooms')
                AND parentColumn.name = N'RoomId'
                AND referencedColumn.name = N'Id'
        )
        BEGIN
            UPDATE request
            SET RoomId = NULL
            FROM dbo.ViewingRequests AS request
            WHERE request.RoomId IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.Rooms AS room
                    WHERE room.Id = request.RoomId
                );

            ALTER TABLE dbo.ViewingRequests WITH CHECK
                ADD CONSTRAINT FK_ViewingRequests_Rooms_RoomId
                FOREIGN KEY (RoomId) REFERENCES dbo.Rooms (Id)
                ON DELETE NO ACTION;
        END;

        IF OBJECT_ID(N'dbo.RoomReviews', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RoomReviews (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoomReviews PRIMARY KEY,
                UserId int NULL,
                RoomId int NULL,
                Rating int NOT NULL,
                Content nvarchar(2000) NOT NULL,
                IsPublished bit NOT NULL CONSTRAINT DF_RoomReviews_IsPublished DEFAULT (0),
                CreatedAt datetime2 NOT NULL,
                CONSTRAINT CK_RoomReviews_Rating CHECK (Rating >= 1 AND Rating <= 5),
                CONSTRAINT FK_RoomReviews_Users_UserId FOREIGN KEY (UserId)
                    REFERENCES dbo.Users (Id),
                CONSTRAINT FK_RoomReviews_Rooms_RoomId FOREIGN KEY (RoomId)
                    REFERENCES dbo.Rooms (Id)
            );
            CREATE INDEX IX_RoomReviews_IsPublished_CreatedAt
                ON dbo.RoomReviews (IsPublished, CreatedAt);
        END;

        IF OBJECT_ID(N'dbo.RentalContracts', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RentalContracts (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RentalContracts PRIMARY KEY,
                OwnerId int NOT NULL,
                RoomId int NOT NULL,
                TenantId int NOT NULL,
                StartDate datetime2 NOT NULL,
                EndDate datetime2 NULL,
                MonthlyRent bigint NOT NULL,
                DepositAmount bigint NOT NULL,
                CreatedAt datetime2 NOT NULL,
                CONSTRAINT FK_RentalContracts_Owner FOREIGN KEY (OwnerId) REFERENCES dbo.Users (Id),
                CONSTRAINT FK_RentalContracts_Room FOREIGN KEY (RoomId) REFERENCES dbo.Rooms (Id),
                CONSTRAINT FK_RentalContracts_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Users (Id),
                CONSTRAINT CK_RentalContracts_Amounts CHECK (MonthlyRent >= 0 AND DepositAmount >= 0)
            );
            CREATE INDEX IX_RentalContracts_OwnerId_RoomId ON dbo.RentalContracts (OwnerId, RoomId);
        END;

        IF OBJECT_ID(N'dbo.UtilityBills', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.UtilityBills (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UtilityBills PRIMARY KEY,
                OwnerId int NOT NULL,
                RoomId int NOT NULL,
                BillingMonth datetime2 NOT NULL,
                ElectricityKwh int NOT NULL,
                WaterM3 int NOT NULL,
                OtherFees bigint NOT NULL,
                IsPaid bit NOT NULL CONSTRAINT DF_UtilityBills_IsPaid DEFAULT (0),
                CreatedAt datetime2 NOT NULL,
                CONSTRAINT FK_UtilityBills_Owner FOREIGN KEY (OwnerId) REFERENCES dbo.Users (Id),
                CONSTRAINT FK_UtilityBills_Room FOREIGN KEY (RoomId) REFERENCES dbo.Rooms (Id),
                CONSTRAINT CK_UtilityBills_Amounts CHECK (ElectricityKwh >= 0 AND WaterM3 >= 0 AND OtherFees >= 0)
            );
            CREATE INDEX IX_UtilityBills_OwnerId_BillingMonth ON dbo.UtilityBills (OwnerId, BillingMonth);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UtilityBills_OwnerId_RoomId_BillingMonth' AND object_id = OBJECT_ID(N'dbo.UtilityBills'))
            CREATE UNIQUE INDEX IX_UtilityBills_OwnerId_RoomId_BillingMonth
                ON dbo.UtilityBills (OwnerId, RoomId, BillingMonth);

        IF OBJECT_ID(N'dbo.RepairRequests', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RepairRequests (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RepairRequests PRIMARY KEY,
                OwnerId int NOT NULL,
                RoomId int NOT NULL,
                Title nvarchar(160) NOT NULL,
                Description nvarchar(2000) NOT NULL,
                Status int NOT NULL CONSTRAINT DF_RepairRequests_Status DEFAULT (0),
                CreatedAt datetime2 NOT NULL,
                CONSTRAINT FK_RepairRequests_Owner FOREIGN KEY (OwnerId) REFERENCES dbo.Users (Id),
                CONSTRAINT FK_RepairRequests_Room FOREIGN KEY (RoomId) REFERENCES dbo.Rooms (Id),
                CONSTRAINT CK_RepairRequests_Status CHECK (Status BETWEEN 0 AND 2)
            );
            CREATE INDEX IX_RepairRequests_OwnerId_Status_CreatedAt ON dbo.RepairRequests (OwnerId, Status, CreatedAt);
        END;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        UPDATE request
        SET RoomId = matchedRoom.Id
        FROM dbo.ViewingRequests AS request
        CROSS APPLY (
            SELECT MIN(room.Id) AS Id
            FROM dbo.Rooms AS room
            WHERE room.Title = request.RoomName
            HAVING COUNT(*) = 1
        ) AS matchedRoom
        WHERE request.RoomId IS NULL;
        """);

    var additionalRooms = new[]
    {
        new Room
        {
            Title = "Studio Đầy Đủ Nội Thất Gần Công Viên",
            RoomType = "mini",
            Area = 30,
            Address = "Phố Đội Cấn, Phường Ngọc Hà",
            District = "Ba Đình",
            Description = "Studio thoáng sáng, đầy đủ nội thất, thuận tiện di chuyển đến các trường đại học tại Hà Nội.",
            Rules = "Giữ gìn vệ sinh, không hút thuốc trong phòng.",
            Price = 5200000,
            DepositMonths = 2,
            ElectricPrice = 3500,
            WaterPrice = 80000,
            InternetFee = 120000,
            ParkingFee = 100000,
            Amenities = "Máy lạnh,Wifi,Chỗ để xe",
            Package = "basic",
            Status = RoomStatus.Approved,
            IsVerified = true,
            OwnerId = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-4),
            Images = new List<RoomImage>
            {
                new() { Path = "https://images.unsplash.com/photo-1522708323590-d24dbb6b0267?auto=format&fit=crop&w=1200&q=80", IsPrimary = true }
            }
        },
        new Room
        {
            Title = "Phòng Trọ Có Gác Gần Chợ",
            RoomType = "single",
            Area = 24,
            Address = "Phố Thái Hà, Phường Trung Liệt",
            District = "Đống Đa",
            Description = "Phòng có gác rộng rãi, khu dân cư an ninh và gần chợ.",
            Rules = "Không nuôi thú cưng, giữ yên tĩnh sau 22 giờ.",
            Price = 3500000,
            DepositMonths = 1,
            ElectricPrice = 3500,
            WaterPrice = 75000,
            InternetFee = 100000,
            ParkingFee = 0,
            Amenities = "Wifi,Gác lửng,Chỗ để xe",
            Package = "basic",
            Status = RoomStatus.Approved,
            IsVerified = true,
            OwnerId = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            Images = new List<RoomImage>
            {
                new() { Path = "https://images.unsplash.com/photo-1505693416388-ac5ce068fe85?auto=format&fit=crop&w=1200&q=80", IsPrimary = true }
            }
        },
        new Room
        {
            Title = "Căn Hộ Mini Yên Tĩnh Tại Cầu Giấy",
            RoomType = "mini",
            Area = 35,
            Address = "Phố Cầu Giấy, Phường Quan Hoa",
            District = "Cầu Giấy",
            Description = "Căn hộ mini riêng tư, có bếp và cửa sổ lớn đón ánh sáng tự nhiên.",
            Rules = "Không tổ chức tiệc, bảo quản nội thất trong phòng.",
            Price = 6100000,
            DepositMonths = 2,
            ElectricPrice = 3800,
            WaterPrice = 85000,
            InternetFee = 150000,
            ParkingFee = 150000,
            Amenities = "Máy lạnh,Wifi,Nuôi thú cưng,Chỗ để xe",
            Package = "premium",
            Status = RoomStatus.Approved,
            IsVerified = true,
            OwnerId = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            Images = new List<RoomImage>
            {
                new() { Path = "https://images.unsplash.com/photo-1493809842364-78817add7ffb?auto=format&fit=crop&w=1200&q=80", IsPrimary = true }
            }
        },
        new Room
        {
            Title = "Phòng Ở Ghép Tiện Nghi Hoàng Mai",
            RoomType = "shared",
            Area = 20,
            Address = "Đường Giải Phóng, Phường Hoàng Liệt",
            District = "Hoàng Mai",
            Description = "Phòng ở ghép sạch sẽ, phù hợp sinh viên và người đi làm.",
            Rules = "Tôn trọng không gian chung, không hút thuốc.",
            Price = 2800000,
            DepositMonths = 1,
            ElectricPrice = 3500,
            WaterPrice = 70000,
            InternetFee = 90000,
            ParkingFee = 0,
            Amenities = "Wifi,Giờ giấc tự do,Chỗ để xe",
            Package = "free",
            Status = RoomStatus.Approved,
            IsVerified = true,
            OwnerId = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            Images = new List<RoomImage>
            {
                new() { Path = "https://images.unsplash.com/photo-1522771739844-6a9f6d5f14af?auto=format&fit=crop&w=1200&q=80", IsPrimary = true }
            }
        },
        new Room
        {
            Title = "Phòng Trọ Thoáng Mát Gần Đại Học",
            RoomType = "single",
            Area = 26,
            Address = "Đường Xuân Đỉnh, Phường Xuân Đỉnh",
            District = "Bắc Từ Liêm",
            Description = "Phòng riêng thoáng mát, gần tiện ích và các tuyến xe buýt.",
            Rules = "Giữ gìn tài sản, không gây ồn sau 22 giờ.",
            Price = 4000000,
            DepositMonths = 1,
            ElectricPrice = 3500,
            WaterPrice = 75000,
            InternetFee = 100000,
            ParkingFee = 50000,
            Amenities = "Máy lạnh,Wifi,Gác lửng,Chỗ để xe",
            Package = "basic",
            Status = RoomStatus.Approved,
            IsVerified = true,
            OwnerId = 1,
            CreatedAt = DateTime.UtcNow,
            Images = new List<RoomImage>
            {
                new() { Path = "https://images.unsplash.com/photo-1502672260266-1c1ef2d93688?auto=format&fit=crop&w=1200&q=80", IsPrimary = true }
            }
        }
    };

    var legacyRoomTitles = new Dictionary<string, string>
    {
        ["Căn Hộ Mini Yên Tĩnh Tại Thủ Đức"] = "Căn Hộ Mini Yên Tĩnh Tại Cầu Giấy",
        ["Căn Hộ Mini Yên Tĩnh Tại Long Biên"] = "Căn Hộ Mini Yên Tĩnh Tại Cầu Giấy",
        ["Phòng Ở Ghép Tiện Nghi Tân Bình"] = "Phòng Ở Ghép Tiện Nghi Hoàng Mai"
    };
    foreach (var (legacyTitle, currentTitle) in legacyRoomTitles)
    {
        var legacyRoom = await db.Rooms.SingleOrDefaultAsync(room => room.Title == legacyTitle);
        if (legacyRoom is null) continue;

        if (await db.Rooms.AnyAsync(room => room.Title == currentTitle))
        {
            db.Rooms.Remove(legacyRoom);
        }
        else
        {
            legacyRoom.Title = currentTitle;
        }
    }
    await db.SaveChangesAsync();

    foreach (var room in additionalRooms)
    {
        if (!await db.Rooms.AnyAsync(existing => existing.Title == room.Title))
        {
            db.Rooms.Add(room);
        }
    }

    var duplicateCauGiayRooms = await db.Rooms
        .Where(room => room.Title == "Căn Hộ Mini Yên Tĩnh Tại Cầu Giấy")
        .OrderBy(room => room.Id)
        .ToListAsync();
    if (duplicateCauGiayRooms.Count > 1)
    {
        db.Rooms.RemoveRange(duplicateCauGiayRooms.Skip(1));
    }

    var seededRoomLocations = new Dictionary<string, (string Address, string District, double Lat, double Lng)>
    {
        ["Phòng Studio Mới Xây, View Đẹp"] = ("Số 24, ngõ 18, Phố Hoàng Quốc Việt", "Cầu Giấy", 21.0455, 105.7925),
        ["Phòng Ở Ghép Sinh Viên, Giá Rẻ"] = ("78 Trần Duy Hưng, P. Trung Hoà", "Đống Đa", 21.0094, 105.8018),
        ["Căn Hộ Mini Nội Thất 2PN"] = ("128 Lê Văn Lương, P. Nhân Chính", "Nam Từ Liêm", 21.0038, 105.8068),
        ["Phòng Cao Cấp Gần Trường Đại Học"] = ("Đường Quang Trung, Phường Hà Cầu", "Hà Đông", 20.9712, 105.7728),
        ["Phòng Khép Kín, An Ninh Tốt"] = ("Đường Quang Trung, Phường Hà Cầu", "Hà Đông", 20.9750, 105.7800),
        ["Studio Đầy Đủ Nội Thất Gần Công Viên"] = ("Phố Đội Cấn, Phường Ngọc Hà", "Ba Đình", 21.0372, 105.8245),
        ["Phòng Trọ Có Gác Gần Chợ"] = ("Phố Thái Hà, Phường Trung Liệt", "Đống Đa", 21.0135, 105.8206),
        ["Căn Hộ Mini Yên Tĩnh Tại Cầu Giấy"] = ("Phố Cầu Giấy, Phường Quan Hoa", "Cầu Giấy", 21.0336, 105.7972),
        ["Phòng Ở Ghép Tiện Nghi Hoàng Mai"] = ("Đường Giải Phóng, Phường Hoàng Liệt", "Hoàng Mai", 20.9723, 105.8427),
        ["Phòng Trọ Thoáng Mát Gần Đại Học"] = ("Đường Xuân Đỉnh, Phường Xuân Đỉnh", "Bắc Từ Liêm", 21.0667, 105.7833)
    };
    var allRooms = await db.Rooms.ToListAsync();
    foreach (var room in allRooms)
    {
        if (seededRoomLocations.TryGetValue(room.Title, out var loc))
        {
            room.Address = loc.Address;
            room.District = loc.District;
            room.Latitude = loc.Lat;
            room.Longitude = loc.Lng;
        }
        else if (room.Latitude == null || room.Longitude == null)
        {
            // Tọa độ mặc định tại trung tâm quận tương ứng tại Hà Nội
            (room.Latitude, room.Longitude) = room.District switch
            {
                "Cầu Giấy" => (21.0336, 105.7972),
                "Đống Đa" => (21.0180, 105.8250),
                "Ba Đình" => (21.0340, 105.8250),
                "Hà Đông" => (20.9720, 105.7750),
                "Nam Từ Liêm" => (21.0120, 105.7650),
                "Bắc Từ Liêm" => (21.0600, 105.7600),
                "Hoàng Mai" => (20.9750, 105.8450),
                "Hai Bà Trưng" => (21.0080, 105.8550),
                "Thanh Xuân" => (20.9950, 105.8050),
                _ => (21.0285, 105.8542)
            };
        }
    }
    await db.SaveChangesAsync();

    if (app.Environment.IsDevelopment())
    {
        var adminEmail = builder.Configuration["AdminBootstrap:Email"]?.Trim().ToLowerInvariant();
        var adminPassword = builder.Configuration["AdminBootstrap:Password"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var existingAdmin = await db.Users.SingleOrDefaultAsync(user => user.Email == adminEmail);
            if (existingAdmin is null)
            {
                var admin = new AppUser
                {
                    FullName = "TroTot Admin",
                    Email = adminEmail,
                    Phone = "+10000000000",
                    Role = UserRole.Admin
                };
                admin.PasswordHash = scope.ServiceProvider
                    .GetRequiredService<IPasswordHasher<AppUser>>()
                    .HashPassword(admin, adminPassword);
                db.Users.Add(admin);
                await db.SaveChangesAsync();
            }
            else if (existingAdmin.Role != UserRole.Admin)
            {
                throw new InvalidOperationException(
                    $"The development admin email '{adminEmail}' already belongs to a non-admin account.");
            }
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
