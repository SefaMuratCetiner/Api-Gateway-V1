namespace ApiGateway.Abstractions.Features;

/// <summary>Body byte counts of the current request, for the access log and usage analytics.</summary>
public interface IBodySizeFeature
{
    long? RequestBytes { get; }

    long? ResponseBytes { get; }
}
