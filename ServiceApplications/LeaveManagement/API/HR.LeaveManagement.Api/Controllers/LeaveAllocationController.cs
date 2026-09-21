using Microsoft.AspNetCore.Mvc;

namespace HR.LeaveManagement.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LeaveAllocationController : ControllerBase
    {

        // GET: api/<LeaveRequestsController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<LeaveRequestsController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<LeaveRequestsController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<LeaveRequestsController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<LeaveRequestsController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }

    }
}
