using AutoMapper;
using HR.LeaveManagement.Application.Contracts.Persistence;
using HR.LeaveManagement.Application.Features.LeaveType.Queries.GetLeaveTypeDetails;
using MediatR;

namespace HR.LeaveManagement.Application.Features.LeaveType.Queries.GetAllLeaveTypes
{
    public class GetLeaveTypesQueryHandler : IRequestHandler<GetLeaveTypesQuery, List<LeaveTypeDto>>
    {
        private readonly IMapper _mapper;
        private readonly ILeaveTypeRepository _leaveTypeRepository;

        public GetLeaveTypesQueryHandler(IMapper mapper, ILeaveTypeRepository leaveTypeRepository)
        {
            this._mapper = mapper;
            this._leaveTypeRepository = leaveTypeRepository;
        }

        public async Task<List<LeaveTypeDto>> Handle(GetLeaveTypesQuery request, CancellationToken cancellationToken)
        {
            // query the database  
            var leaveTypes = await _leaveTypeRepository.GetAsync();

            //convert data object to DTO Object
            var data = _mapper.Map<List<LeaveTypeDto>>(leaveTypes);

            //return list fo DTO Object
            //_logger.LogInformation("Leave types were retrieved successfully");
            return data;
        }

        public async Task Handle(GetLeaveTypeDetailsQuery getLeaveTypeDetailsQuery, CancellationToken none)
        {
            throw new NotImplementedException();
        }
    }
}
