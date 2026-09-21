using AutoMapper;
using HR.LeaveManagement.Application.Contracts.Persistence;
using HR.LeaveManagement.Application.Features.LeaveType.Commands.CreateLeaveType;
using HR.LeaveManagement.Application.MappingProfiles;
using HR.LeaveManagement.Application.UnitTests.Mocks;
using Moq;
using Shouldly;

namespace HR.LeaveManagement.Application.UnitTests.Features.LeaveTypes.Commands
{

    public class CreateLeaveTypeCommandHandlerTests
    {
        private readonly IMapper _mapper;
        private readonly Mock<ILeaveTypeRepository> _mockCategoryRepository;
        private Mock<ILeaveTypeRepository> _mockRepo;

        public CreateLeaveTypeCommandHandlerTests()
        {
            _mockRepo = MockLeaveTypeRepository.GetMockLeaveTypeRepository();

            var mapperConfig = new MapperConfiguration(c =>
            {
                c.AddProfile<LeaveTypeProfile>();
            });

            _mapper = mapperConfig.CreateMapper();
        }

        [Fact]
        public async Task Handle_ValidCategory_AddedToCategoriesRepo()
        {
            var handler = new CreateLeaveTypeCommandHandler(_mapper, _mockRepo.Object);

            await handler.Handle(new CreateLeaveTypeCommand() { Name = "Test Leave", DefaultDays = 10, }, CancellationToken.None);

            var leaveTypes = await _mockCategoryRepository.Object.GetAsync();
            leaveTypes.Count.ShouldBe(4);
        }
    }
}
