using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Task2;

public class SameAuthorRequirement : IAuthorizationRequirement
{
}

public class SameAuthorHandler : AuthorizationHandler<SameAuthorRequirement, Quote>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameAuthorRequirement requirement, Quote resource)
    {
        // Try to get email claim to match with Author.
        // In the Day 2 JWT, Email is stored as JwtRegisteredClaimNames.Email (which maps to "email" or ClaimTypes.Email)
        var email = context.User.FindFirst(ClaimTypes.Email)?.Value 
            ?? context.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? context.User.FindFirst("email")?.Value;

        if (email != null && string.Equals(resource.Author, email, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
        // If not matching, it doesn't succeed (so it will result in 403)
        return Task.CompletedTask;
    }
}
