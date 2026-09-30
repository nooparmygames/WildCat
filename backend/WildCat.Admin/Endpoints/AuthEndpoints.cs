using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Admin.Endpoints;

/// <summary>
/// Developer accounts: register, activate, login, password reset and change. Everything here except
/// change-password is anonymous and rate limited per client IP address.
/// </summary>
internal static class AuthEndpoints
{
    private const string InvalidLogin = "The email or password is not correct.";
    private const string InvalidActivationCode = "The activation code is not valid or has expired.";
    private const string InvalidResetCode = "The reset code is not valid or has expired.";

    // Checked against a password when the email is unknown, so a login for a missing account takes
    // about as long as one with a wrong password.
    private static readonly Lazy<string> TimingHash = new(() => PasswordHasher.Hash(Guid.NewGuid().ToString("N")));

    public static void Map(RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");
        var anonymous = auth.MapGroup("").AllowAnonymous().RequireRateLimiting(WildCatAdminExtensions.AuthRateLimitPolicy);

        anonymous.MapPost("/register", Register)
            .WithSummary("Registers a developer account and emails an activation code.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        anonymous.MapPost("/activate", Activate)
            .WithSummary("Activates an account with the emailed code.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        anonymous.MapPost("/resend-activation", ResendActivation)
            .WithSummary("Emails a new activation code if the account exists and is not active yet. Always 202.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        anonymous.MapPost("/login", Login)
            .WithSummary("Exchanges email and password for a bearer token valid for eight hours.")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        anonymous.MapPost("/request-password-reset", RequestPasswordReset)
            .WithSummary("Emails a password reset code if the account exists. Always 202.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        anonymous.MapPost("/reset-password", ResetPassword)
            .WithSummary("Sets a new password with the emailed reset code.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/change-password", ChangePassword)
            .RequireAuthorization(WildCatAdminExtensions.DeveloperPolicy)
            .WithSummary("Changes the caller's password; needs the old one. A wrong old password is 400, not 401.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static IDeveloperManager Manager(IGrainFactory grains) =>
        grains.GetGrain<IDeveloperManager>(WildCatAdminExtensions.DeveloperManagerKey);

    private static async Task<IResult> Register(RegisterRequest request, IGrainFactory grains, AccountEmails emails,
        ILoggerFactory loggers)
    {
        var errors = new List<string>();
        string? email = Validation.Email(request.Email, errors);
        Validation.NewPassword(request.Password, "password", errors);
        Validation.MaxLength(request.FirstName, "first name", Validation.MaxNameLength, errors);
        Validation.MaxLength(request.LastName, "last name", Validation.MaxNameLength, errors);
        if (errors.Count > 0 || email == null)
            return Problems.Validation(errors);

        string? firstName = Validation.Clean(request.FirstName);
        var registration = await Manager(grains).RegisterDeveloper(email, request.Password!, firstName, Validation.Clean(request.LastName));
        if (registration == null)
            return Problems.Conflict("An account with this email already exists.");

        try
        {
            await emails.SendActivationAsync(email, firstName, registration.ActivationCode);
        }
        catch (Exception ex)
        {
            // The account exists now, so answer as usual; the developer can ask for a new code
            // through resend-activation once mail works.
            loggers.CreateLogger(typeof(AuthEndpoints)).LogError(ex, "The activation email to {Email} could not be sent", email);
        }
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> Activate(ActivateRequest request, IGrainFactory grains)
    {
        var errors = new List<string>();
        string? email = Validation.Email(request.Email, errors);
        Validation.Required(request.Code, "code", errors);
        if (errors.Count > 0 || email == null)
            return Problems.Validation(errors);

        var developer = await Manager(grains).GetDeveloper(email);
        if (developer == null || !await developer.Activate(request.Code!))
            return Problems.BadRequest(InvalidActivationCode);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ResendActivation(EmailRequest request, IGrainFactory grains, AccountEmails emails)
    {
        var errors = new List<string>();
        string? email = Validation.Email(request.Email, errors);
        if (errors.Count > 0 || email == null)
            return Problems.Validation(errors);

        emails.SendInBackground(async sender =>
        {
            var developer = await Manager(grains).GetDeveloper(email);
            if (developer == null)
                return;
            string? code = await developer.CreateActivationCode();
            if (code == null)
                return;
            var data = await developer.GetDeveloperData();
            await sender.SendActivationAsync(email, data.FirstName, code);
        });
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> Login(LoginRequest request, IGrainFactory grains, AdminTokenService tokens, AdminAccess access)
    {
        var errors = new List<string>();
        string? email = Validation.Email(request.Email, errors);
        Validation.Required(request.Password, "password", errors);
        if (errors.Count > 0 || email == null)
            return Problems.Validation(errors);

        var developer = await Manager(grains).GetDeveloper(email);
        if (developer == null)
        {
            PasswordHasher.Verify(request.Password!, TimingHash.Value);
            return Problems.Unauthorized(InvalidLogin);
        }
        if (!await developer.IsPasswordCorrect(request.Password!))
            return Problems.Unauthorized(InvalidLogin);

        var data = await developer.GetDeveloperData();
        if (data.Status == DeveloperState.DeveloperStatus.NotActivated)
            return Problems.Forbidden("This account is not activated yet. Enter the code from the activation email first.");
        if (data.Status == DeveloperState.DeveloperStatus.Banned)
            return Problems.Forbidden("This account is disabled.");

        Guid key = developer.GetPrimaryKey();
        bool isAdmin = access.IsAdminEmail(data.Email);
        var (token, expiresAt) = tokens.CreateToken(key, data.Email, isAdmin);
        return TypedResults.Ok(new LoginResponse(token, expiresAt,
            new DeveloperSummary(key, data.Email, data.FirstName, data.LastName, isAdmin)));
    }

    private static async Task<IResult> RequestPasswordReset(EmailRequest request, IGrainFactory grains, AccountEmails emails)
    {
        var errors = new List<string>();
        string? email = Validation.Email(request.Email, errors);
        if (errors.Count > 0 || email == null)
            return Problems.Validation(errors);

        emails.SendInBackground(async sender =>
        {
            var developer = await Manager(grains).GetDeveloper(email);
            if (developer == null)
                return;
            string code = await developer.CreatePasswordResetCode();
            var data = await developer.GetDeveloperData();
            await sender.SendPasswordResetAsync(email, data.FirstName, code);
        });
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request, IGrainFactory grains)
    {
        var errors = new List<string>();
        string? email = Validation.Email(request.Email, errors);
        Validation.Required(request.Code, "code", errors);
        Validation.NewPassword(request.NewPassword, "new password", errors);
        if (errors.Count > 0 || email == null)
            return Problems.Validation(errors);

        var developer = await Manager(grains).GetDeveloper(email);
        if (developer == null || !await developer.ConsumePasswordResetCode(request.Code!))
            return Problems.BadRequest(InvalidResetCode);
        await developer.ChangePassword(request.NewPassword!);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, ClaimsPrincipal user, IGrainFactory grains)
    {
        var errors = new List<string>();
        Validation.Required(request.OldPassword, "old password", errors);
        Validation.NewPassword(request.NewPassword, "new password", errors);
        if (errors.Count > 0)
            return Problems.Validation(errors);

        var developer = grains.GetGrain<IDeveloper>(user.GetDeveloperKey());
        // 400 rather than 401: the caller is logged in, only the typed password is wrong.
        if (!await developer.IsPasswordCorrect(request.OldPassword!))
            return Problems.BadRequest("The old password is not correct.");
        await developer.ChangePassword(request.NewPassword!);
        return TypedResults.NoContent();
    }
}
