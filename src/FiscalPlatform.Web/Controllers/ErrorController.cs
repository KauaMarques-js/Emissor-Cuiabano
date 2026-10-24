using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalPlatform.Web.Controllers;

[AllowAnonymous]
public class ErrorController : Controller
{
    [Route("/Error")]
    public IActionResult Index() => Content("Ocorreu um erro inesperado. Tente novamente; se persistir, verifique os logs da aplicação.");
}
