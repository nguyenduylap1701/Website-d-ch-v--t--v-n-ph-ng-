using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;

namespace TroTot.Web.Controllers;

[Route("api")]
public class ApiController : ControllerBase
{
    private readonly AppDbContext _db;

    public ApiController(AppDbContext db)
    {
        _db = db;
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
            .Take(Math.Clamp(limit ?? 30, 1, 100))
            .Select(r => new
            {
                id = r.Id,
                title = r.Title,
                address = r.Address,
                district = r.District,
                latitude = r.Latitude,
                longitude = r.Longitude,
                price = r.Price,
                area = r.Area,
                type = r.RoomType,
                rating = r.IsVerified ? 4.9 : 4.6,
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

    [HttpGet("landmarks")]
    public IActionResult GetLandmarks()
    {
        var landmarks = new[]
        {
            new { id = "hou", name = "Đại học Mở Hà Nội (FIT HOU)", address = "96 Định Công, Hoàng Mai", lat = 20.9856, lng = 105.8344 },
            new { id = "hust", name = "Đại học Bách Khoa Hà Nội", address = "Số 1 Đại Cồ Việt, Hai Bà Trưng", lat = 21.0049, lng = 105.8458 },
            new { id = "neu", name = "Đại học Kinh Tế Quốc Dân", address = "207 Giải Phóng, Hai Bà Trưng", lat = 21.0006, lng = 105.8427 },
            new { id = "vnu", name = "Đại học Quốc Gia Hà Nội", address = "144 Xuân Thủy, Cầu Giấy", lat = 21.0378, lng = 105.7818 },
            new { id = "hnue", name = "Đại học Sư Phạm Hà Nội", address = "136 Xuân Thủy, Cầu Giấy", lat = 21.0366, lng = 105.7828 },
            new { id = "tmu", name = "Đại học Thương Mại", address = "79 Hồ Tùng Mậu, Cầu Giấy", lat = 21.0365, lng = 105.7744 },
            new { id = "utc", name = "Đại học Giao Thông Vận Tải", address = "3 Cầu Giấy, Láng Thượng, Đống Đa", lat = 21.0286, lng = 105.8028 },
            new { id = "ptit", name = "Học viện Bưu chính Viễn thông", address = "Km10 Đường Nguyễn Trãi, Hà Đông", lat = 20.9806, lng = 105.7877 },
            new { id = "hanu", name = "Đại học Hà Nội", address = "Km9 Nguyễn Trãi, Nam Từ Liêm", lat = 20.9912, lng = 105.7958 },
            new { id = "hau", name = "Đại học Kiến Trúc Hà Nội", address = "Km10 Nguyễn Trãi, Thanh Xuân", lat = 20.9831, lng = 105.7892 },
            new { id = "hmu", name = "Đại học Y Hà Nội", address = "1 Tôn Thất Tùng, Đống Đa", lat = 21.0028, lng = 105.8306 },
            new { id = "tlu", name = "Đại học Thủy Lợi", address = "175 Tây Sơn, Đống Đa", lat = 21.0075, lng = 105.8239 }
        };
        return new JsonResult(landmarks);
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
