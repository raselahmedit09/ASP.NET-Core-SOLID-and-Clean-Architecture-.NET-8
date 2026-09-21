using MediatR;

namespace HR.LeaveManagement.Application.Features.LeaveType.Queries.GetLeaveTypeDetails
{
    //public class GetLeaveTypeDetailsQuery : IRequest<List<LeaveTypeDto>>
    //{
    //}

    public record GetLeaveTypeDetailsQuery(int Id) : IRequest<LeaveTypeDetailsDto>;
}
