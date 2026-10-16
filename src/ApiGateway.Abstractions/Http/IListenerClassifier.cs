using Microsoft.AspNetCore.Http;

namespace ApiGateway.Abstractions.Http;

/// <summary>Tells which listener a request arrived on, for libraries that treat operational traffic differently.</summary>
public interface IListenerClassifier
{
    /// <summary>True when the request arrived on the internal (operational) listener.</summary>
    bool IsInternal(HttpContext context);
}
