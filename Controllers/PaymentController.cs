using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;
using TroTot.Web.Services;

namespace TroTot.Web.Controllers;

[Route("Payment")]
[Authorize]
public sealed class PaymentController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentController> _logger;
    private readonly IWebHostEnvironment _environment;

    public PaymentController(AppDbContext db, IConfiguration config, ILogger<PaymentController> logger, IWebHostEnvironment environment)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _environment = environment;
    }

    [HttpPost("CreateDeposit")]
    public async Task<IActionResult> CreateDeposit([FromQuery] int roomId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var tenantId)) return Unauthorized();
        if (roomId <= 0) return BadRequest(new { message = "Invalid room." });
        if (!TryGetVnPayConfig(out var tmnCode, out var hashSecret, out var baseUrl, out var configError))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = configError });

        var amount = _config.GetValue<long?>("VnPay:DepositAmount") ?? 500_000;
        var holdMinutes = Math.Clamp(_config.GetValue<int?>("VnPay:HoldMinutes") ?? 15, 1, 30);
        if (amount < 10_000 || amount > 1_000_000_000) return StatusCode(500, new { message = "Invalid deposit amount configuration." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var room = await _db.Rooms.SingleOrDefaultAsync(r => r.Id == roomId, cancellationToken);
            if (room is null || room.Status != RoomStatus.Approved)
                return BadRequest(new { message = "Room is unavailable." });
            if (room.AvailabilityStatus != RoomAvailability.Available)
                return Conflict(new { message = "Room is already reserved or rented." });

            var now = DateTime.UtcNow;
            var deposit = new RoomDeposit
            {
                RoomId = room.Id,
                TenantId = tenantId,
                Amount = amount,
                TransactionCode = $"R06{now:yyMMddHHmmss}{Guid.NewGuid():N}"[..35],
                OrderInfo = $"Room deposit {room.Id}",
                Status = "Pending",
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(holdMinutes)
            };
            room.AvailabilityStatus = RoomAvailability.PendingDeposit;
            _db.RoomDeposits.Add(deposit);
            await _db.SaveChangesAsync(cancellationToken);

            var vietnamNow = now.AddHours(7);
            var payment = new VnPayLibrary();
            payment.AddRequestData("vnp_Version", "2.1.0");
            payment.AddRequestData("vnp_Command", "pay");
            payment.AddRequestData("vnp_TmnCode", tmnCode);
            payment.AddRequestData("vnp_Amount", checked(deposit.Amount * 100).ToString(CultureInfo.InvariantCulture));
            payment.AddRequestData("vnp_CreateDate", vietnamNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            payment.AddRequestData("vnp_ExpireDate", vietnamNow.AddMinutes(holdMinutes).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            payment.AddRequestData("vnp_CurrCode", "VND");
            payment.AddRequestData("vnp_IpAddr", GetClientIp());
            payment.AddRequestData("vnp_Locale", "vn");
            payment.AddRequestData("vnp_OrderInfo", deposit.OrderInfo);
            payment.AddRequestData("vnp_OrderType", "other");
            payment.AddRequestData("vnp_ReturnUrl", _config["VnPay:ReturnUrl"]!);
            payment.AddRequestData("vnp_TxnRef", deposit.TransactionCode);
            var paymentUrl = payment.CreateRequestUrl(baseUrl, hashSecret);

            await transaction.CommitAsync(cancellationToken);
            return Ok(new { url = paymentUrl, depositId = deposit.Id, expiresAt = deposit.ExpiresAt });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Could not create VNPay deposit for room {RoomId}", roomId);
            return StatusCode(500, new { message = "Could not create the deposit. Please try again." });
        }
    }

    [HttpPost("MockConfirm/{id:int}")]
    public async Task<IActionResult> MockConfirm(int id, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() || !_config.GetValue<bool>("VnPay:EnableMockConfirmation"))
            return NotFound();
        if (!TryGetUserId(out var tenantId)) return Unauthorized();

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var deposit = await _db.RoomDeposits.Include(d => d.Room)
            .SingleOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);
        if (deposit is null) return NotFound();
        if (deposit.Status != "Pending")
        {
            await transaction.CommitAsync(cancellationToken);
            return RedirectToAction(deposit.Status == "Success" ? nameof(Receipt) : nameof(Result), new { id });
        }

        var now = DateTime.UtcNow;
        if (deposit.ExpiresAt <= now)
        {
            deposit.Status = "Expired";
            if (deposit.Room?.AvailabilityStatus == RoomAvailability.PendingDeposit)
                deposit.Room.AvailabilityStatus = RoomAvailability.Available;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RedirectToAction(nameof(Result), new { id });
        }
        if (deposit.Room?.AvailabilityStatus != RoomAvailability.PendingDeposit)
            return Conflict("Room is no longer held for this deposit.");

        deposit.Status = "Success";
        deposit.PaidAt = now;
        deposit.ReceiptCode = $"DEMO-{deposit.TransactionCode}";
        deposit.Room.AvailabilityStatus = RoomAvailability.Rented;
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogWarning("Development mock payment confirmed for deposit {DepositId}; no VNPay payment was verified.", id);
        return RedirectToAction(nameof(Receipt), new { id });
    }
    [HttpGet("ReturnUrl")]
    [AllowAnonymous]
    public async Task<IActionResult> ReturnUrl(CancellationToken cancellationToken)
    {
        var result = await ProcessGatewayResponseAsync(Request.Query, cancellationToken);
        if (!result.Valid) return BadRequest(result.Message);
        if (result.DepositId is null) return BadRequest("Invalid transaction reference.");

        var status = await _db.RoomDeposits.AsNoTracking()
            .Where(d => d.Id == result.DepositId.Value)
            .Select(d => d.Status)
            .SingleOrDefaultAsync(cancellationToken);
        return string.Equals(status, "Success", StringComparison.Ordinal)
            ? RedirectToAction(nameof(Receipt), new { id = result.DepositId.Value })
            : RedirectToAction(nameof(Result), new { id = result.DepositId.Value });
    }

    [HttpGet("Result/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Result(int id, CancellationToken cancellationToken)
    {
        var deposit = await _db.RoomDeposits.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new { d.Status, d.RoomId })
            .SingleOrDefaultAsync(cancellationToken);
        if (deposit is null) return NotFound();
        if (deposit.Status == "Success") return RedirectToAction(nameof(Receipt), new { id });

        ViewBag.RoomId = deposit.RoomId;
        ViewBag.Title = deposit.Status == "Expired" ? "Giao dịch đã hết hạn" : "Thanh toán chưa hoàn tất";
        ViewBag.Message = deposit.Status == "Failed"
            ? "Giao dịch chưa thành công hoặc đã bị hủy. Bạn có thể quay lại phòng để thử lại."
            : "Giao dịch chưa được xác nhận. Bạn có thể quay lại trang chủ hoặc thử lại sau.";
        return View("Result");
    }

    [HttpGet("Ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> Ipn(CancellationToken cancellationToken)
    {
        var result = await ProcessGatewayResponseAsync(Request.Query, cancellationToken);
        if (!result.Valid)
            return Json(new { RspCode = result.ResponseCode ?? "97", Message = result.Message });
        return Json(new { RspCode = "00", Message = "Confirm Success" });
    }

    [HttpGet("DepositStatus/{id:int}")]
    public async Task<IActionResult> DepositStatus(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var tenantId)) return Unauthorized();
        var deposit = await _db.RoomDeposits.AsNoTracking()
            .Where(d => d.Id == id && d.TenantId == tenantId)
            .Select(d => new { d.Status, d.ExpiresAt })
            .SingleOrDefaultAsync(cancellationToken);
        return deposit is null ? NotFound() : Ok(deposit);
    }

    [HttpPost("Expire/{id:int}")]
    public async Task<IActionResult> ExpireDeposit(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var tenantId)) return Unauthorized();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var deposit = await _db.RoomDeposits.Include(d => d.Room)
            .SingleOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);
        if (deposit is null) return NotFound();
        if (deposit.Status == "Pending" && deposit.ExpiresAt <= DateTime.UtcNow)
        {
            deposit.Status = "Expired";
            if (deposit.Room?.AvailabilityStatus == RoomAvailability.PendingDeposit)
                deposit.Room.AvailabilityStatus = RoomAvailability.Available;
            await _db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Ok(new { deposit.Status });
    }

    [HttpGet("Receipt/{id:int}")]
    public async Task<IActionResult> Receipt(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var tenantId)) return Unauthorized();
        var deposit = await _db.RoomDeposits.AsNoTracking()
            .Include(d => d.Room).Include(d => d.Tenant)
            .SingleOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && d.Status == "Success", cancellationToken);
        return deposit is null ? NotFound() : View("Receipt", deposit);
    }

    private async Task<GatewayResult> ProcessGatewayResponseAsync(IQueryCollection query, CancellationToken cancellationToken)
    {
        if (!TryGetVnPayConfig(out var tmnCode, out var hashSecret, out _, out var configError))
            return GatewayResult.Fail("99", configError);
        var secureHash = query["vnp_SecureHash"].ToString();
        if (string.IsNullOrWhiteSpace(secureHash)) return GatewayResult.Fail("97", "Missing signature.");

        var response = new VnPayLibrary();
        foreach (var (key, value) in query)
        {
            if (key.StartsWith("vnp_", StringComparison.Ordinal) && key is not "vnp_SecureHash" and not "vnp_SecureHashType")
                response.AddResponseData(key, value.ToString());
        }
        if (!response.ValidateSignature(secureHash, hashSecret)) return GatewayResult.Fail("97", "Invalid signature.");
        if (!string.Equals(response.GetResponseData("vnp_TmnCode"), tmnCode, StringComparison.Ordinal))
            return GatewayResult.Fail("97", "Invalid merchant.");
        if (!long.TryParse(response.GetResponseData("vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out var gatewayAmount))
            return GatewayResult.Fail("04", "Invalid amount.");

        var transactionCode = response.GetResponseData("vnp_TxnRef");
        if (string.IsNullOrWhiteSpace(transactionCode)) return GatewayResult.Fail("04", "Invalid transaction reference.");
        var gatewaySuccess = response.GetResponseData("vnp_ResponseCode") == "00" && response.GetResponseData("vnp_TransactionStatus") == "00";

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var deposit = await _db.RoomDeposits.Include(d => d.Room)
            .SingleOrDefaultAsync(d => d.TransactionCode == transactionCode, cancellationToken);
        if (deposit is null) return GatewayResult.Fail("01", "Deposit not found.");
        if (gatewayAmount != checked(deposit.Amount * 100)) return GatewayResult.Fail("04", "Amount mismatch.");

        if (deposit.Status == "Pending")
        {
            if (gatewaySuccess)
            {
                deposit.Status = "Success";
                deposit.PaidAt = DateTime.UtcNow;
                deposit.VnPayTransactionId = response.GetResponseData("vnp_TransactionNo");
                deposit.ReceiptCode = $"BM03-{deposit.TransactionCode}";
                if (deposit.ExpiresAt > DateTime.UtcNow && deposit.Room?.AvailabilityStatus == RoomAvailability.PendingDeposit)
                    deposit.Room.AvailabilityStatus = RoomAvailability.Rented;
            }
            else
            {
                deposit.Status = "Failed";
                if (deposit.Room?.AvailabilityStatus == RoomAvailability.PendingDeposit)
                    deposit.Room.AvailabilityStatus = RoomAvailability.Available;
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return GatewayResult.Ok(deposit.Id);
    }

    private bool TryGetVnPayConfig(out string tmnCode, out string hashSecret, out string baseUrl, out string error)
    {
        tmnCode = _config["VnPay:TmnCode"] ?? "";
        hashSecret = _config["VnPay:HashSecret"] ?? "";
        baseUrl = _config["VnPay:BaseUrl"] ?? "";
        var returnUrl = _config["VnPay:ReturnUrl"];
        error = "Thiếu VNPay:TmnCode hoặc VNPay:HashSecret. Hãy đặt hai giá trị được cấp trong cấu hình User Secrets.";
        if (string.IsNullOrWhiteSpace(tmnCode) || string.IsNullOrWhiteSpace(hashSecret))
            return false;

        if (tmnCode.Length != 8 || !tmnCode.All(char.IsLetterOrDigit))
        {
            error = "VNPay:TmnCode không hợp lệ. Hãy dùng đúng mã merchant 8 ký tự chữ-số do VNPay cấp, không dùng tên website hoặc chuỗi giữ chỗ.";
            return false;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var gateway) || gateway.Scheme != Uri.UriSchemeHttps ||
            !Uri.TryCreate(returnUrl, UriKind.Absolute, out var callback) || callback.Scheme != Uri.UriSchemeHttps)
        {
            error = "VNPay:BaseUrl hoặc VNPay:ReturnUrl chưa hợp lệ. Cả hai địa chỉ phải dùng HTTPS.";
            return false;
        }

        error = "";
        return true;
    }

    private string GetClientIp()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        var ip = remoteIp is null || System.Net.IPAddress.IsLoopback(remoteIp)
            ? "127.0.0.1"
            : (remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp).ToString();
        return ip.Length <= 45 ? ip : "127.0.0.1";
    }

    private bool TryGetUserId(out int userId) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private sealed record GatewayResult(bool Valid, string? Message, string? ResponseCode, int? DepositId)
    {
        public static GatewayResult Ok(int id) => new(true, null, null, id);
        public static GatewayResult Fail(string code, string message) => new(false, message, code, null);
    }
}
