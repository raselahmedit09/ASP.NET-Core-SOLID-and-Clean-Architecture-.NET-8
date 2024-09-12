using APIRestClient.HR.LeaveManagementModule;
using APIRestClient.HR.LeaveManagementModule.Contacts;
using Microsoft.AspNetCore.Mvc;

namespace HR.Employee.Api.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class LeaveController : ControllerBase
    {
        private LeaveManagementModuleAPI _leaveManagementModuleAPI;

        public LeaveController(LeaveManagementModuleAPI leaveManagementModuleAPI)
        {
            _leaveManagementModuleAPI = leaveManagementModuleAPI;
        }


        [HttpGet]
        public async Task<List<LeaveTypeDto>> apiGetWay_LeaveTypes_GetAsync()
        {
            return await _leaveManagementModuleAPI.LeaveTypes_GetAsync();
        }
    }
}
