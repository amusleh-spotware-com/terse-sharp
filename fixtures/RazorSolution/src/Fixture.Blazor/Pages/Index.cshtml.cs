using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fixture.Blazor.Pages;

public sealed class IndexModel : PageModel
{
    public string Title { get; set; } = "orders";

    public void OnGet() => Title = "open orders";
}
