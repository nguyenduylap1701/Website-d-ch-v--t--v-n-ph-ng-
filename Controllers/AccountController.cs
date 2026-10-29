using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;
using TroTot.Web.ViewModels;

namespace TroTot.Web.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;

    public AccountController(AppDbContext db, IPasswordHasher<AppUser> hasher)
    {
        _db = db;
        _hasher = hasher;
    }
    [HttpGet("dang-nhap")]
    public IActionResult DangNhap(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost("dang-nhap")]
    public async Task<IActionResult> DangNhap(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var id = vm.LoginId.Trim();
        var phone = NormalizePhone(id);
        var email = id.ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email || u.Phone == phone);
        var ok = user != null &&
                 _hasher.VerifyHashedPassword(user, user.PasswordHash, vm.Password) != PasswordVerificationResult.Failed;

        if (!ok)
        {
            ModelState.AddModelError("", "Email/số điện thoại hoặc mật khẩu không đúng.");
            return View(vm);
        }
        var authenticatedUser = user!;
        if (!authenticatedUser.IsActive)
        {
            ModelState.AddModelError("", "Tài khoản của bạn hiện đã bị khóa. Vui lòng liên hệ quản trị viên.");
            return View(vm);
        }

        await SignInAsync(authenticatedUser, vm.RememberMe);
        TempData["Success"] = $"Chào mừng {authenticatedUser.FullName} quay trở lại!";
        if (Url.IsLocalUrl(vm.ReturnUrl))
        {
            return LocalRedirect(vm.ReturnUrl!);
        }

        return authenticatedUser.Role is UserRole.Admin or UserRole.Owner
            ? RedirectToAction("QuanLy", "Home")
            : Redirect("/");
    }
    [HttpGet("dang-ky")]
    public IActionResult DangKy() => View(new RegisterViewModel());

    [HttpPost("dang-ky")]
    public async Task<IActionResult> DangKy(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var email = vm.Email.Trim().ToLowerInvariant();
        var phone = NormalizePhone(vm.Phone);

        if (await _db.Users.AnyAsync(u => u.Email == email))
            ModelState.AddModelError(nameof(vm.Email), "Email này đã được đăng ký.");
        if (await _db.Users.AnyAsync(u => u.Phone == phone))
            ModelState.AddModelError(nameof(vm.Phone), "Số điện thoại này đã được đăng ký.");
        if (!ModelState.IsValid) return View(vm);

        var user = new AppUser
        {
            FullName = vm.FullName.Trim(),
            Email = email,
            Phone = phone,
            Role = vm.Role == "owner" ? UserRole.Owner : UserRole.Tenant
        };
        user.PasswordHash = _hasher.HashPassword(user, vm.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await SignInAsync(user, isPersistent: true);
        TempData["Success"] = "Đăng ký thành công! Chào mừng bạn đến với TroTot.vn.";
        return Redirect("/");
    }
    [HttpPost("dang-xuat")]
    public async Task<IActionResult> DangXuat()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "Bạn đã đăng xuất.";
        return Redirect("/");
    }
    private async Task SignInAsync(AppUser user, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = isPersistent });
    }

    private static string NormalizePhone(string s)
        => new string(s.Where(c => char.IsDigit(c) || c == '+').ToArray());
}
