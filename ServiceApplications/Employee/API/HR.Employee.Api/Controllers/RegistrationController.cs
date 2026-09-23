using Microsoft.AspNetCore.Mvc;

namespace HR.Employee.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RegistrationController : ControllerBase
    {

        [HttpGet]
        public async Task<string> Registration()
        {
            return " Yes! Registration is succeeded";
        }

        [HttpGet("Login_1")]
        public async Task<string> Login()
        {
            return " Yes! Login is succeeded";
        }
    }
}
