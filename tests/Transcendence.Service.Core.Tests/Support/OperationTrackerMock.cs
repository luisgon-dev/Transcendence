using Moq;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.Service.Core.Tests.Support;

internal static class OperationTrackerMock
{
    public static Mock<IBackgroundOperationTracker> Create()
    {
        var tracker = new Mock<IBackgroundOperationTracker>();
        tracker.Setup(x => x.StartAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        tracker.Setup(x => x.CreateChildAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        return tracker;
    }
}
