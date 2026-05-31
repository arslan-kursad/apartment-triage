using System.Security.Claims;
using ApartmentTriage.Application.Services;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Web.Pages;
using ApartmentTriage.Web.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Pages;

public class LoginModelTests
{
    [Fact]
    public async Task OnPostAsync_Success_SignsInWithDashboardRoleClaims()
    {
        var resident = Resident.Create(displayName: "Kürşad", telegramId: 100000001);
        resident.SetRole(ResidentRole.Manager);
        var auth = new RecordingAuthService();
        var model = BuildModel(new LoginOtpService(OtpVerifyResult.Success(resident)), auth);
        model.Code = "123456";

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.PageName.Should().Be("/Index");
        auth.SignedInPrincipal.Should().NotBeNull();
        auth.SignedInPrincipal!.FindFirst(ClaimTypes.Role)?.Value.Should().Be("Manager");
        auth.SignedInPrincipal.FindFirst("residentId")?.Value.Should().Be(resident.Id.ToString());
        auth.SignedInPrincipal.FindFirst("displayName")?.Value.Should().Be("KÜRŞAD");
    }

    [Fact]
    public async Task OnPostAsync_InvalidCode_StaysOnPage()
    {
        var model = BuildModel(new LoginOtpService(OtpVerifyResult.Invalid));
        model.Code = "000000";

        var result = await model.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        model.Message.Should().NotBeNull();
        model.Message!.Tr.Should().Contain("Kod yanlış");
    }

    [Fact]
    public async Task OnPostAsync_ExpiredCode_ShowsExpiredMessage()
    {
        var model = BuildModel(new LoginOtpService(OtpVerifyResult.Expired));
        model.Code = "123456";

        var result = await model.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        model.Message.Should().NotBeNull();
        model.Message!.Tr.Should().Contain("süresi doldu");
    }

    [Fact]
    public async Task OnPostAsync_AfterFiveFailures_RateLimitsFurtherAttempts()
    {
        var otp = new LoginOtpService(OtpVerifyResult.Invalid);
        var model = BuildModel(otp);
        model.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");

        for (var i = 0; i < 5; i++)
        {
            model.Code = "000000";
            (await model.OnPostAsync(CancellationToken.None)).Should().BeOfType<PageResult>();
        }

        model.Code = "000000";
        var result = await model.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        model.Message.Should().NotBeNull();
        model.Message!.Tr.Should().Contain("Çok fazla deneme");
    }

    private static LoginModel BuildModel(IOtpService otpService, RecordingAuthService? auth = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth ?? new RecordingAuthService());

        var model = new LoginModel(otpService, new LoginAttemptLimiter())
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services.BuildServiceProvider()
                }
            }
        };

        return model;
    }
}

public class LogoutModelTests
{
    [Fact]
    public async Task OnGetAsync_SignsOutAndRedirectsToLogin()
    {
        var auth = new RecordingAuthService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth);
        var model = new LogoutModel
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services.BuildServiceProvider()
                }
            }
        };

        var result = await model.OnGetAsync();

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.PageName.Should().Be("/Login");
        auth.SignOutScheme.Should().Be("Cookies");
    }
}

internal sealed class LoginOtpService(OtpVerifyResult verifyResult) : IOtpService
{
    public Task<OtpSendStatus> GenerateAndSendAsync(
        string identifier,
        ChannelType channel,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OtpSendStatus.Sent);

    public Task<OtpVerifyResult> VerifyAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(verifyResult);
}

internal sealed class RecordingAuthService : IAuthenticationService
{
    public ClaimsPrincipal? SignedInPrincipal { get; private set; }
    public string? SignOutScheme { get; private set; }

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        => Task.FromResult(AuthenticateResult.NoResult());

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        => Task.CompletedTask;

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        => Task.CompletedTask;

    public Task SignInAsync(
        HttpContext context,
        string? scheme,
        ClaimsPrincipal principal,
        AuthenticationProperties? properties)
    {
        SignedInPrincipal = principal;
        return Task.CompletedTask;
    }

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
    {
        SignOutScheme = scheme;
        return Task.CompletedTask;
    }
}
