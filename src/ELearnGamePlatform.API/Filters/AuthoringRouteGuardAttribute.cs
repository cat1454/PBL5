using ELearnGamePlatform.API.Controllers;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace ELearnGamePlatform.API.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthoringRouteGuardAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        var role = user.GetCurrentUserRole()?.Trim();

        var isAdmin = string.Equals(role, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase);
        var isInstructor = string.Equals(role, UserRole.Instructor.ToString(), StringComparison.OrdinalIgnoreCase);

        if (!isAdmin && !isInstructor)
        {
            context.Result = new ObjectResult(ApiErrorResponse.Create("learner_authoring_forbidden", "Learners cannot upload or generate learning materials."))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        base.OnActionExecuting(context);
    }
}
