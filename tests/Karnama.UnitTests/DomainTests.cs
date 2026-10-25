using FluentAssertions;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;

namespace Karnama.UnitTests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("09121234567", "09121234567")]
    [InlineData("+989121234567", "09121234567")]
    [InlineData("00989121234567", "09121234567")]
    [InlineData("9121234567", "09121234567")]
    [InlineData("۰۹۱۲ ۱۲۳ ۴۵۶۷", "09121234567")]
    [InlineData("٠٩١٢١٢٣٤٥٦٧", "09121234567")]
    public void Normalizes_valid_numbers(string input, string expected)
    {
        PhoneNumber.TryNormalize(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0212345678")]
    [InlineData("0912123456")]
    [InlineData("09121234567a")]
    [InlineData("08121234567")]
    public void Rejects_invalid_numbers(string input) => PhoneNumber.TryNormalize(input, out _).Should().BeFalse();

    [Fact]
    public void Mask_hides_middle_digits() => PhoneNumber.Mask("09121234567").Should().Be("0912***4567");
}

public class RoleTests
{
    private static LocalizedText Name(string fa) => LocalizedText.Of("fa", fa);

    [Fact]
    public void Create_accepts_allowed_scopes()
    {
        var r = Role.Create(Name("تدوینگر"), null, [new(Permissions.WorkView, PermissionScope.Assigned), new(Permissions.WorkRun, PermissionScope.Assigned)]);
        r.IsSuccess.Should().BeTrue();
        r.Value.Permissions.Should().HaveCount(2);
    }

    [Fact]
    public void Create_rejects_scope_not_allowed_for_permission()
    {
        var r = Role.Create(Name("x"), null, [new(Permissions.SettingsManage, PermissionScope.Own)]);
        r.IsFailure.Should().BeTrue();
        r.Error!.Code.Should().Be("roles.invalidScope");
    }

    [Fact]
    public void Create_rejects_unknown_permission_and_empty_name()
    {
        Role.Create(Name("x"), null, [new("hack.everything", PermissionScope.All)]).Error!.Code.Should().Be("roles.unknownPermission");
        Role.Create(new LocalizedText(), null, []).Error!.Code.Should().Be("roles.nameRequired");
    }

    [Fact]
    public void System_role_cannot_be_deleted_and_grants_are_fixed()
    {
        var owner = Role.CreateSystem(SystemRoles.Owner, Name("مالک"), [new(Permissions.RolesManage, PermissionScope.All)]);
        owner.Delete(null, DateTimeOffset.UtcNow).Error!.Code.Should().Be("roles.systemCannotDelete");

        owner.Update(Name("مالک اصلی"), null, []).IsSuccess.Should().BeTrue();
        owner.Permissions.Should().ContainSingle(p => p.Code == Permissions.RolesManage);
        owner.Name.Get("fa").Should().Be("مالک اصلی");
    }

    [Fact]
    public void Update_diffs_grants_in_place()
    {
        var role = Role.Create(Name("x"), null, [new(Permissions.WorkView, PermissionScope.Own), new(Permissions.WorkRun, PermissionScope.Assigned)]).Value;
        var view = role.Permissions.Single(p => p.Code == Permissions.WorkView);

        role.Update(Name("x"), null, [new(Permissions.WorkView, PermissionScope.All)]).IsSuccess.Should().BeTrue();

        role.Permissions.Should().ContainSingle().Which.Should().BeSameAs(view);
        view.Scope.Should().Be(PermissionScope.All);
    }

    [Fact]
    public void Soft_delete_and_restore()
    {
        var role = Role.Create(Name("x"), null, []).Value;
        role.Delete(Guid.NewGuid(), DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
        role.IsDeleted.Should().BeTrue();
        role.Restore();
        role.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Catalog_codes_are_unique_and_scopes_non_empty()
    {
        Permissions.Catalog.Select(p => p.Code).Should().OnlyHaveUniqueItems();
        Permissions.Catalog.Should().OnlyContain(p => p.AllowedScopes.Count > 0);
    }
}

public class OtpChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fresh_challenge_is_usable_for_two_minutes()
    {
        var c = OtpChallenge.Create(Guid.NewGuid(), "09121234567", "h", Now);
        c.CheckUsable(Now.AddSeconds(119)).IsSuccess.Should().BeTrue();
        c.CheckUsable(Now.AddMinutes(2)).Error!.Code.Should().Be("auth.otpExpired");
    }

    [Fact]
    public void Wrong_code_error_is_generic() => OtpChallenge.WrongCode.Code.Should().Be("auth.otpWrong");
}

public class UserTests
{
    [Fact]
    public void Locks_out_after_ten_failures_for_fifteen_minutes()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("09121234567", "امیر", UserKind.Staff).Value;
        for (var i = 0; i < User.MaxFailedLogins - 1; i++) user.RegisterFailedLogin(now);
        user.IsLockedOut(now).Should().BeFalse();

        user.RegisterFailedLogin(now);
        user.IsLockedOut(now).Should().BeTrue();
        user.IsLockedOut(now.Add(User.LockoutDuration)).Should().BeFalse();
    }

    [Fact]
    public void Totp_step_cannot_be_reused()
    {
        var user = User.Create("09121234567", "امیر", UserKind.Staff).Value;
        user.AcceptTwoFactorStep(100).IsSuccess.Should().BeTrue();
        user.AcceptTwoFactorStep(100).IsFailure.Should().BeTrue();
        user.AcceptTwoFactorStep(99).IsFailure.Should().BeTrue();
        user.AcceptTwoFactorStep(101).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Rejects_bad_phone_and_name()
    {
        User.Create("123", "x", UserKind.Staff).IsFailure.Should().BeTrue();
        User.Create("09121234567", " ", UserKind.Staff).IsFailure.Should().BeTrue();
    }
}

public class LocalizedTextTests
{
    [Fact]
    public void Falls_back_to_default_language_then_any()
    {
        var t = new LocalizedText(new Dictionary<string, string> { ["fa"] = "سلام", ["en"] = "Hello" });
        t.Get("en").Should().Be("Hello");
        t.Get("ar").Should().Be("سلام");
        LocalizedText.Of("en", "Hi").Get("fa").Should().Be("Hi");
    }

    [Fact]
    public void Drops_empty_values_and_normalizes_codes()
    {
        var t = new LocalizedText(new Dictionary<string, string> { ["FA"] = " نام ", ["en"] = "  " });
        t.Values.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("fa", "نام"));
    }
}
