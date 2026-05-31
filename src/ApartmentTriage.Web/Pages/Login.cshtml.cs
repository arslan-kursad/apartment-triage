using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ApartmentTriage.Application.Services;
using ApartmentTriage.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApartmentTriage.Web.Pages;

public sealed class LoginModel(IOtpService otpService, LoginAttemptLimiter attemptLimiter) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Kod gereklidir.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Kod 6 haneli olmalıdır.")]
    public string? Code { get; set; }

    public LoginMessage? Message { get; private set; }

    public IActionResult OnGet(string? reason = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Index");

        Message = reason switch
        {
            "denied" => LoginMessage.AccessDenied,
            "expired" => LoginMessage.Expired,
            _ => null
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Message = LoginMessage.InvalidFormat;
            return Page();
        }

        var attemptKey = GetAttemptKey();
        var now = DateTimeOffset.UtcNow;
        if (!attemptLimiter.IsAllowed(attemptKey, now))
        {
            Message = LoginMessage.TooManyAttempts;
            return Page();
        }

        var result = await otpService.VerifyAsync(Code!, ct);
        if (result.Status != OtpVerifyStatus.Success || result.Resident is null)
        {
            attemptLimiter.RegisterFailure(attemptKey, now);
            Message = MapFailure(result.Status);
            return Page();
        }

        attemptLimiter.Reset(attemptKey);

        var resident = result.Resident;
        var displayName = resident.DisplayName ?? resident.ApartmentNumber ?? "Hanwas";
        var claims = new List<Claim>
        {
            new("residentId", resident.Id.ToString()),
            new("displayName", displayName),
            new(ClaimTypes.NameIdentifier, resident.Id.ToString()),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Role, resident.Role.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return RedirectToPage("/Index");
    }

    private static LoginMessage MapFailure(OtpVerifyStatus status)
        => status switch
        {
            OtpVerifyStatus.Expired => LoginMessage.Expired,
            OtpVerifyStatus.TooManyMatches => LoginMessage.TryAgain,
            _ => LoginMessage.InvalidCode
        };

    private string GetAttemptKey()
    {
        var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var ip = forwardedFor?.Split(',')[0].Trim();
        if (string.IsNullOrWhiteSpace(ip))
            ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        return string.IsNullOrWhiteSpace(ip) ? "unknown" : ip;
    }

    public sealed record LoginMessage(string Tr, string En, string CssClass)
    {
        public static readonly LoginMessage AccessDenied = new(
            "Bu hesap için panel erişimi atanmadı.",
            "Dashboard access has not been assigned to this account.",
            "border-amber-200 bg-amber-50 text-amber-900");

        public static readonly LoginMessage Expired = new(
            "Kodun süresi doldu. Telegram'da /login yazarak yeni kod alın.",
            "This code has expired. Send /login on Telegram to get a new code.",
            "border-amber-200 bg-amber-50 text-amber-900");

        public static readonly LoginMessage InvalidFormat = new(
            "Lütfen 6 haneli kodu girin.",
            "Please enter the 6-digit code.",
            "border-rose-200 bg-rose-50 text-rose-900");

        public static readonly LoginMessage InvalidCode = new(
            "Kod yanlış veya artık geçerli değil.",
            "The code is incorrect or no longer valid.",
            "border-rose-200 bg-rose-50 text-rose-900");

        public static readonly LoginMessage TryAgain = new(
            "Bu kod güvenli biçimde doğrulanamadı. Telegram'da /login yazarak yeni kod alın.",
            "This code could not be verified safely. Send /login on Telegram to get a new code.",
            "border-rose-200 bg-rose-50 text-rose-900");

        public static readonly LoginMessage TooManyAttempts = new(
            "Çok fazla deneme yapıldı. Lütfen bir süre sonra tekrar deneyin.",
            "Too many attempts. Please wait a while and try again.",
            "border-rose-200 bg-rose-50 text-rose-900");
    }
}
