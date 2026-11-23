using System.Security.Claims;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace StudentManagement.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("session")]
    public async Task<IActionResult> CreateSession([FromBody] FirebaseSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return BadRequest(new { message = "A Firebase ID token is required." });
        }

        try
        {
            var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(request.IdToken);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, decodedToken.Uid),
                new(ClaimTypes.Name, decodedToken.Claims.TryGetValue("name", out var name) ? name?.ToString() ?? decodedToken.Uid : decodedToken.Uid),
                new(ClaimTypes.Email, decodedToken.Claims.TryGetValue("email", out var email) ? email?.ToString() ?? string.Empty : string.Empty)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = false,
                    ExpiresUtc = null
                });

            return Ok(new { authenticated = true });
        }
        catch (FirebaseAuthException)
        {
            return Unauthorized(new { message = "Firebase authentication could not be verified." });
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { authenticated = false });
    }
}

public sealed class FirebaseSessionRequest
{
    public string IdToken { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}
