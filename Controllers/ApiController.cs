using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;
using TroTot.Web.ViewModels;

namespace TroTot.Web.Controllers;

[Route("api")]
public class ApiController : ControllerBase
{
    private static readonly string[] AllowedReviewImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxReviewImageBytes = 5 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public ApiController(AppDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [HttpGet("rooms/{id:int}/reviews")]
    public async Task<IActionResult> GetRoomReviews(int id)
    {
        var roomExists = await _db.Rooms.AnyAsync(room =>
            room.Id == id && room.Status == RoomStatus.Approved);
        if (!roomExists)
        {
            return NotFound();
        }

        var publishedReviews = _db.RoomReviews
            .AsNoTracking()
            .Where(review => review.RoomId == id && review.IsPublished);
        var totalCount = await publishedReviews.CountAsync();
        var averageRating = totalCount == 0
            ? 0
            : await publishedReviews.AverageAsync(review => (double)review.Rating);
        var reviews = await publishedReviews
            .OrderByDescending(review => review.CreatedAt)
            .Select(review => new
            {
                id = review.Id,
                author = review.User == null ? "Người thuê" : review.User.FullName,
                rating = review.Rating,
                content = review.Content,
                image = review.ImagePath,
                createdAt = review.CreatedAt
            })
            .ToListAsync();

        return new JsonResult(new { roomId = id, averageRating, totalCount, reviews });
    }

    [Authorize]
    [HttpPost("rooms/{id:int}/reviews")]
    [RequestSizeLimit(MaxReviewImageBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxReviewImageBytes + 64 * 1024)]
    public async Task<IActionResult> CreateRoomReview(int id, [FromForm] RoomReviewSubmission submission)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var content = submission.Content.Trim();
        if (content.Length == 0)
        {
            return BadRequest(new { message = "Vui lòng nhập nội dung đánh giá." });
        }

        var roomExists = await _db.Rooms.AnyAsync(room =>
            room.Id == id && room.Status == RoomStatus.Approved);
        if (!roomExists)
        {
            return NotFound();
        }

                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdString, out var userId))
        {
            return Unauthorized();
        }
        var hasContract = await _db.RentalContracts.AnyAsync(c =>
            c.RoomId == id && c.TenantId == userId);

        if (!hasContract)
        {
            return BadRequest(new { message = "Bạn chỉ có thể đánh giá phòng mà bạn đã hoặc đang thuê (phải có hợp đồng trên hệ thống)." });
        }

        string? imagePath = null;
        if (submission.Image is { Length: > 0 } image)
        {
            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            if (!AllowedReviewImageExtensions.Contains(extension) || image.Length > MaxReviewImageBytes)
            {
                return BadRequest(new
                {
                    message = "Ảnh không hợp lệ. Chỉ chấp nhận JPG, PNG hoặc WEBP, tối đa 5MB."
                });
            }

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var directory = Path.Combine(_environment.WebRootPath, "uploads", "reviews");
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, fileName);
            await using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await image.CopyToAsync(stream, HttpContext.RequestAborted);
            }

            imagePath = $"/uploads/reviews/{fileName}";
        }

                var review = new RoomReview
        {
            UserId = userId,
            RoomId = id,
            Rating = submission.Rating,
            Content = content,
            ImagePath = imagePath,
            IsPublished = true // Tự động duyệt vì đã xác minh có hợp đồng
        };
        _db.RoomReviews.Add(review);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRoomReviews), new { id }, new
        {
            id = review.Id,
            roomId = id,
            rating = review.Rating,
            content = review.Content,
            image = review.ImagePath,
            createdAt = review.CreatedAt,
            isPublished = review.IsPublished
        });
    }

    [HttpGet("rooms/stats")]
    public async Task<IActionResult> GetRoomStats()
    {
        var approvedRooms = _db.Rooms.Where(r => r.Status == RoomStatus.Approved);
        var stats = new
        {
            availableRooms = await approvedRooms.CountAsync(),
            verifiedRooms = await approvedRooms.CountAsync(r => r.IsVerified),
            owners = await _db.Users.CountAsync(u => u.Role == UserRole.Owner),
            districts = await approvedRooms.Select(r => r.District).Distinct().CountAsync()
        };

        return new JsonResult(stats);
    }

    [HttpGet("districts")]
    public async Task<IActionResult> GetDistricts()
    {
        var districts = await _db.Rooms
            .Where(r => r.Status == RoomStatus.Approved)
            .GroupBy(r => r.District)
            .Select(group => new
            {
                name = group.Key,
                averagePrice = group.Average(r => (double)r.Price),
                rooms = group.Count()
            })
            .OrderByDescending(district => district.rooms)
            .ToListAsync();

        return new JsonResult(districts);
    }

    [HttpGet("rooms/recommendations")]
    public async Task<IActionResult> GetRoomRecommendations(
        [FromQuery] long minRent = 1_000_000,
        [FromQuery] long maxRent = 10_000_000,
        [FromQuery] string[]? amenity = null)
    {
        if (minRent < 0 || maxRent < minRent || maxRent > 100_000_000)
        {
            return BadRequest(new { message = "Khoảng ngân sách không hợp lệ." });
        }

        var requestedAmenities = (amenity ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        var rooms = await _db.Rooms
            .AsNoTracking()
            .Where(room => room.Status == RoomStatus.Approved &&
                room.Price >= minRent && room.Price <= maxRent)
            .OrderBy(room => room.Price)
            .Take(50)
            .Select(room => new
            {
                room.Id,
                room.Title,
                room.Address,
                room.District,
                room.Price,
                room.RoomType,
                room.Area,
                room.Description,
                room.Rules,
                room.Amenities,
                room.ElectricPrice,
                room.WaterPrice,
                room.IsVerified,
                Image = room.Images
                    .OrderBy(image => image.IsPrimary ? 0 : 1)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Path)
                    .FirstOrDefault(),
                AverageRating = _db.RoomReviews
                    .Where(review => review.RoomId == room.Id && review.IsPublished)
                    .Select(review => (double?)review.Rating)
                    .Average() ?? 0
            })
            .ToListAsync();

        var recommendations = rooms
            .Select(room =>
            {
                var availableAmenities = room.Amenities
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var matchedAmenities = requestedAmenities
                    .Where(required => availableAmenities.Contains(required, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                var missingAmenities = requestedAmenities
                    .Except(matchedAmenities, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var budgetScore = maxRent == minRent
                    ? 1d
                    : 1d - Math.Abs(room.Price - ((minRent + maxRent) / 2d)) / (maxRent - minRent);
                var amenityScore = requestedAmenities.Length == 0
                    ? 1d
                    : matchedAmenities.Length / (double)requestedAmenities.Length;
                var fitScore = Math.Clamp(
                    (int)Math.Round(Math.Clamp(budgetScore, 0, 1) * 55 +
                        amenityScore * 30 +
                        (room.IsVerified ? 15 : 0)),
                    0,
                    100);

                return new
                {
                    id = room.Id,
                    title = room.Title,
                    address = room.Address,
                    district = room.District,
                    price = room.Price,
                    roomType = room.RoomType,
                    area = room.Area,
                    description = room.Description,
                    rules = room.Rules,
                    amenities = availableAmenities,
                    electricPrice = room.ElectricPrice,
                    waterPrice = room.WaterPrice,
                    verified = room.IsVerified,
                    image = room.Image ?? "https://images.unsplash.com/photo-1505693416388-ac5ce068fe85?auto=format&fit=crop&w=1200&q=80",
                    averageRating = room.AverageRating,
                    fitScore,
                    matchedAmenities,
                    missingAmenities
                };
            })
            .Where(room => room.missingAmenities.Length == 0)
            .OrderByDescending(room => room.fitScore)
            .ThenBy(room => room.price)
            .Take(12)
            .ToList();

        return new JsonResult(new
        {
            recommendations,
            totalCount = recommendations.Count,
            scoringNote = "Điểm gợi ý dựa trên ngân sách, tiện ích đã khai báo và trạng thái xác minh; không phải đánh giá an ninh hay dự đoán AI."
        });
    }

    [HttpGet("rooms")]
    public async Task<IActionResult> GetRooms(
        [FromQuery] int? limit,
        [FromQuery] string? keyword,
        [FromQuery] string? district,
        [FromQuery] string? type,
        [FromQuery] string[]? amenity,
        [FromQuery] long? minPrice,
        [FromQuery] long? maxPrice,
        [FromQuery] bool? verified,
        [FromQuery] string? sort)
    {
        var query = _db.Rooms
            .Where(r => r.Status == RoomStatus.Approved)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim();
            query = query.Where(r =>
                r.Title.Contains(search) ||
                r.Address.Contains(search) ||
                r.District.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(district))
        {
            var searchDistrict = district.Trim();
            query = query.Where(r => r.District.Contains(searchDistrict) || r.Address.Contains(searchDistrict));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(r => r.RoomType == type);
        }

        foreach (var item in amenity ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                var requiredAmenity = item.Trim();
                query = query.Where(r => r.Amenities.Contains(requiredAmenity));
            }
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
            _ => query.OrderByDescending(r => r.CreatedAt)
        };

        var rooms = await query
            .Take(Math.Clamp(limit ?? 6, 1, 50))
            .Select(r => new
            {
                id = r.Id,
                title = r.Title,
                district = r.District,
                price = r.Price,
                area = r.Area,
                type = r.RoomType,
                rating = _db.RoomReviews
                    .Where(review => review.RoomId == r.Id && review.IsPublished)
                    .Select(review => (double?)review.Rating)
                    .Average() ?? 0,
                verified = r.IsVerified,
                image = r.Images
                    .OrderBy(i => i.IsPrimary ? 0 : 1)
                    .ThenBy(i => i.Id)
                    .Select(i => i.Path)
                    .FirstOrDefault() ?? "https://images.unsplash.com/photo-1505693416388-ac5ce068fe85?auto=format&fit=crop&w=1200&q=80",
                amenities = r.Amenities
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Take(4)
                    .ToArray()
            })
            .ToListAsync();

        return new JsonResult(rooms);
    }

    [HttpGet("rooms/{id:int}")]
    public async Task<IActionResult> GetRoomDetail(int id)
    {
        var room = await _db.Rooms
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == id && r.Status == RoomStatus.Approved);

        if (room == null)
        {
            return NotFound();
        }

        var payload = new
        {
            id = room.Id,
            title = room.Title,
            district = room.District,
            price = room.Price,
            area = room.Area,
            roomType = room.RoomType,
            address = room.Address,
            description = room.Description,
            rules = room.Rules,
            amenities = room.Amenities
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray(),
            images = room.Images.OrderBy(i => i.IsPrimary ? 0 : 1).ThenBy(i => i.Id).Select(i => i.Path).ToList(),
            electricPrice = room.ElectricPrice,
            waterPrice = room.WaterPrice,
            internetFee = room.InternetFee,
            depositMonths = room.DepositMonths,
            verified = room.IsVerified
        };

        return new JsonResult(payload);
    }
}
