using ApiGateway.Abstractions.Errors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace ApiGateway.Errors.Handling;

/// <summary>
/// Route authorization failures as problem details. The configured authentication handlers still challenge or
/// forbid first (so <c>WWW-Authenticate</c> is set by them); the body is then written in the gateway shape:
/// 401 <c>gateway.authentication_required</c> without credentials, <c>gateway.invalid_token</c> with
/// credentials that failed, and 403 <c>gateway.forbidden</c>.
/// </summary>
public sealed class ProblemAuthorizationResultHandler(
    IProblemResponseWriter writer,
    IAuthenticationSchemeProvider schemes) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        if (authorizeResult.Challenged)
        {
            await ChallengeOrSetStatusAsync(context, policy, StatusCodes.Status401Unauthorized, static (ctx, scheme) => ctx.ChallengeAsync(scheme));
            var error = context.Request.Headers.ContainsKey(HeaderNames.Authorization)
                ? GatewayErrors.InvalidToken
                : GatewayErrors.AuthenticationRequired;
            await writer.WriteAsync(context, error);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await ChallengeOrSetStatusAsync(context, policy, StatusCodes.Status403Forbidden, static (ctx, scheme) => ctx.ForbidAsync(scheme));
            await writer.WriteAsync(context, GatewayErrors.Forbidden);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private async Task ChallengeOrSetStatusAsync(
        HttpContext context,
        AuthorizationPolicy policy,
        int status,
        Func<HttpContext, string?, Task> action)
    {
        if (policy.AuthenticationSchemes.Count > 0)
        {
            foreach (var scheme in policy.AuthenticationSchemes)
            {
                await action(context, scheme);
            }

            return;
        }

        // Without any authentication scheme (e.g. before authentication is configured), challenging would throw.
        var hasDefault = status == StatusCodes.Status401Unauthorized
            ? await schemes.GetDefaultChallengeSchemeAsync() is not null
            : await schemes.GetDefaultForbidSchemeAsync() is not null;
        if (hasDefault)
        {
            await action(context, null);
        }
        else
        {
            context.Response.StatusCode = status;
        }
    }
}
