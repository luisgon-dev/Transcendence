using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Transcendence.WebAPI.Tests;

/// <summary>
/// Reads the <see cref="ProducesResponseTypeAttribute"/> set an action publishes to the OpenAPI document
/// (controller-level declarations union the action's own). The generated TypeScript client only gains a
/// typed branch for a status code that appears here, so an action that returns a status it never declares
/// ships as an untyped surprise to callers.
/// </summary>
internal static class ResponseTypeContractAssertions
{
    internal static IReadOnlyDictionary<int, Type> DeclaredResponses<TController>(string actionName)
        where TController : ControllerBase
    {
        var action = typeof(TController).GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"{typeof(TController).Name} has no public action named '{actionName}'.");

        var declarations = typeof(TController)
            .GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true));

        var responses = new Dictionary<int, Type>();
        foreach (var declaration in declarations)
            responses[declaration.StatusCode] = declaration.Type;

        return responses;
    }
}
