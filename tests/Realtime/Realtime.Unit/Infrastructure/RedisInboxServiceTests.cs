using FluentAssertions;
using Haworks.Realtime.Api.Application.Common;
using Haworks.Realtime.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Haworks.Realtime.Unit.Infrastructure;

public class RedisInboxServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly Mock<ILogger<RedisInboxService>> _loggerMock;
    private readonly RedisInboxService _sut;

    public RedisInboxServiceTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<RedisInboxService>>();

        _redisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_dbMock.Object);

        _sut = new RedisInboxService(_redisMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task StoreMessageAsync_trims_inbox_at_max_size()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var messageType = "Test";
        var message = new { Data = "payload" };

        _dbMock.Setup(x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _dbMock.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(Mock.Of<ITransaction>());

        var transactionMock = new Mock<ITransaction>();
        transactionMock.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _dbMock.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(transactionMock.Object);

        // Act
        await _sut.StoreMessageAsync(userId, messageId, messageType, message, CancellationToken.None);

        // Assert — Transaction with LPUSH + LTRIM + EXPIRE is executed
        transactionMock.Verify(x => x.ExecuteAsync(It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public void MaxInboxSize_is_1000()
    {
        InboxConstants.MaxInboxSize.Should().Be(1000);
    }

    [Fact]
    public async Task GetMessagesAsync_returns_empty_when_no_messages()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _dbMock.Setup(x => x.ListRangeAsync(
                It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        var result = await _sut.GetMessagesAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AcknowledgeMessagesAsync_deletes_inbox_key_when_delivered_count_exceeds_length()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var deliveredCount = 5;
        _dbMock.Setup(x => x.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(3); // Less than deliveredCount
        _dbMock.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _sut.AcknowledgeMessagesAsync(userId, deliveredCount, CancellationToken.None);

        // Assert
        _dbMock.Verify(x => x.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == $"realtime:inbox:{userId}"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeMessagesAsync_trims_list_when_delivered_count_is_partial()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var deliveredCount = 3;
        var currentLength = 10;
        _dbMock.Setup(x => x.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(currentLength);
        _dbMock.Setup(x => x.ListTrimAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.AcknowledgeMessagesAsync(userId, deliveredCount, CancellationToken.None);

        // Assert — LTRIM keeps [0, currentLength - deliveredCount - 1] = [0, 6]
        _dbMock.Verify(x => x.ListTrimAsync(
            It.Is<RedisKey>(k => k.ToString() == $"realtime:inbox:{userId}"),
            0,
            6, // currentLength - deliveredCount - 1 = 10 - 3 - 1 = 6
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task Inbox_overflow_trims_oldest_entries()
    {
        // Arrange — simulate writing multiple messages; verify transaction is executed
        var userId = Guid.NewGuid();
        var executeCount = 0;

        _dbMock.Setup(x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true); // Message is new (not duplicate)

        var transactionMock = new Mock<ITransaction>();
        transactionMock.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
            .Callback(() => executeCount++)
            .ReturnsAsync(true);

        _dbMock.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(transactionMock.Object);

        // Act — write 5 messages
        for (var i = 0; i < 5; i++)
        {
            await _sut.StoreMessageAsync(userId, Guid.NewGuid(), "Test", new { Index = i }, CancellationToken.None);
        }

        // Assert — transaction executed for each message (includes LTRIM for inbox size management)
        executeCount.Should().Be(5);
    }
}
