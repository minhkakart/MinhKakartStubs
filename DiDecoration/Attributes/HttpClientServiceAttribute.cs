namespace DiDecoration.Attributes;

/// <summary>
/// Specifies that a class is an HTTP client service, which means it will be registered as a typed HTTP client in the dependency injection container.
/// The class must have a constructor that accepts an <see cref="HttpClient"/> parameter to be registered as an HTTP client service.
/// The <see cref="HttpClientServiceAttribute"/> allows you to specify a base URL for the HTTP client, a timeout for requests, and any interceptors
/// that should be applied to the HTTP client. Interceptors can be used to add custom logic before or after sending HTTP requests, such as logging,
/// retry policies, or authentication. Invalid base URLs and interceptor types are rejected when registration runs so configuration errors fail fast.
/// </summary>
/// <param name="baseUrl">
/// An optional base URL for the HTTP client. If specified, this URL must be an absolute URI and will be used as the base address for all requests made
/// by the HTTP client. If not specified, the base address will need to be set manually when configuring the HTTP client or when making requests.
/// </param>
/// <param name="timeoutSeconds">
/// An optional timeout in seconds for HTTP requests made by the client. If specified, this value will be used to set the timeout for the HTTP client. If not specified, a default timeout of 30 seconds will be used.
/// </param>
/// <param name="interceptors">
/// An optional array of interceptor types that should be applied to the HTTP client. Interceptors are classes that can implement custom logic to be executed before or after sending HTTP requests. This can include tasks such as logging request and response data, implementing retry policies, adding authentication headers, or modifying request parameters. Each interceptor type must implement the appropriate interface (e.g., <see cref="DelegatingHandler"/>) to be registered as an interceptor for the HTTP client.
/// </param>
/// <example>
/// <code>
/// [HttpClientService("https://api.example.com", 30, typeof(RetryHandler))]
/// public sealed class CatalogClient
/// {
///     public CatalogClient(HttpClient httpClient) { }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HttpClientServiceAttribute(string? baseUrl = null, int timeoutSeconds = 30, params Type[] interceptors) : Attribute
{
    public string? BaseUrl { get; } = baseUrl;
    public int TimeoutSeconds { get; } = timeoutSeconds;
    public Type[] Interceptors { get; } = interceptors;
}


