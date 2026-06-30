// <copyright file="ApiKeyAttribute.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.API
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Authorization filter which requires a valid shared secret in the <c>X-API-Key</c> header.
    /// The expected key is read from configuration (<c>OpenMU:WebApiKey</c>) or the
    /// <c>WEB_API_KEY</c> environment variable. Fails closed when no key is configured.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private const string HeaderName = "X-API-Key";

        /// <inheritdoc />
        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var configuration = context.HttpContext.RequestServices.GetService<IConfiguration>();
            var expectedKey = configuration?["OpenMU:WebApiKey"] ?? Environment.GetEnvironmentVariable("WEB_API_KEY");
            var providedKey = context.HttpContext.Request.Headers[HeaderName].ToString();

            if (string.IsNullOrEmpty(expectedKey) || !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
            {
                context.Result = new UnauthorizedObjectResult(new { error = "unauthorized", message = "Invalid or missing API key." });
            }

            return Task.CompletedTask;
        }
    }
}
