namespace Transcendence.WebAPI.Models.Common;

/// <summary>
/// A small typed body for side-effecting operations that acknowledge success with a human-readable
/// message (and, optionally, the affected resource id). Replaces the ad-hoc anonymous
/// <c>{ message, id }</c> payloads so the shape is documented in the OpenAPI contract and typed in
/// the generated client (P1 — API Design &amp; Contracts). Pure-side-effect endpoints that carry no
/// useful body should prefer <c>204 NoContent</c> instead.
/// </summary>
/// <param name="Message">Human-readable outcome, safe to surface in a toast/log.</param>
/// <param name="Id">The affected resource id, when the operation targets a specific entity.</param>
public record OperationResult(string Message, string? Id = null);
