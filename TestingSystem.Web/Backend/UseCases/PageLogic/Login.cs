using Microsoft.AspNetCore.Components;


namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class Login : ComponentBase
{
    [SupplyParameterFromQuery(Name = "error")]
    public string? Error { get; set; }

    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery(Name = "reason")]
    public string? Reason { get; set; }

    [SupplyParameterFromQuery(Name = "registered")]
    public string? Registered { get; set; }

    private string SafeReturnUrl => !string.IsNullOrWhiteSpace(ReturnUrl)
        && ReturnUrl.StartsWith('/')
        && !ReturnUrl.StartsWith("//")
            ? ReturnUrl
            : "/";
}
