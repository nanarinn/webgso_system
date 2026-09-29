namespace TestingSystem.Web.Services;

public sealed record PlantApiEndpointInput(string DataType, string ApiLink);

public static class LssPlantApiConfigurationPolicy
{
    private static readonly HashSet<string> DdqDataTypes =
        ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];

    public static string? Validate(IEnumerable<PlantApiEndpointInput> ddqEndpoints, string? mmfEndpoint)
    {
        var ddq = ddqEndpoints.ToList();
        if (ddq.Count == 0)
            return "At least one DDQ API link is required.";
        if (ddq.Any(endpoint => !DdqDataTypes.Contains(endpoint.DataType)))
            return "Choose a valid data type for every DDQ API link.";
        if (ddq.GroupBy(endpoint => endpoint.DataType, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return "Every DDQ API link must use a unique data type.";
        if (ddq.Any(endpoint => !IsHttpEndpoint(endpoint.ApiLink)))
            return "Enter a valid HTTP or HTTPS link for every DDQ endpoint.";
        if (!IsHttpEndpoint(mmfEndpoint))
            return "A valid MMF API link is required.";
        return null;
    }

    private static bool IsHttpEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
}
