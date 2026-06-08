using System.Collections.Generic;
using System.Security.Claims;
using ELearnGamePlatform.API.Filters;
using ELearnGamePlatform.API.Controllers;
using ELearnGamePlatform.Core.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class AuthoringRouteGuardTests
{
    private static ActionExecutingContext CreateContextWithRole(string? role)
    {
        var claims = new List<Claim>();
        if (role != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor()
        );

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object()
        );
    }

    [Fact]
    public void OnActionExecuting_LearnerRole_Returns403Forbidden()
    {
        var guard = new AuthoringRouteGuardAttribute();
        var context = CreateContextWithRole(UserRole.Learner.ToString());

        guard.OnActionExecuting(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        var apiError = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal("learner_authoring_forbidden", apiError.Code);
        Assert.Equal("Learners cannot upload or generate learning materials.", apiError.Message);
    }

    [Fact]
    public void OnActionExecuting_InstructorRole_DoesNotBlockRequest()
    {
        var guard = new AuthoringRouteGuardAttribute();
        var context = CreateContextWithRole(UserRole.Instructor.ToString());

        guard.OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void OnActionExecuting_AdminRole_DoesNotBlockRequest()
    {
        var guard = new AuthoringRouteGuardAttribute();
        var context = CreateContextWithRole(UserRole.Admin.ToString());

        guard.OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void OnActionExecuting_NoRole_Returns403Forbidden()
    {
        var guard = new AuthoringRouteGuardAttribute();
        var context = CreateContextWithRole(null);

        guard.OnActionExecuting(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }
}
