using System;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Exceptions;
using NSubstitute.ReceivedExtensions;
using Pilgaard.CronJobs.Configuration;
using Xunit;

namespace Pilgaard.CronJobs.Tests;

public class CronBackgroundServiceTests : IAsyncDisposable
{
    private readonly IServiceScopeFactory _serviceScopeFactoryMock;
    private readonly ILogger<CronBackgroundService> _loggerMock;
    private readonly ICronJob _cronJobMock;
    private readonly IServiceScope _serviceScopeMock;
    private readonly IServiceProvider _serviceProviderMock;
    private readonly IOptions<CronJobOptions> _optionsMock;
    private CronBackgroundService _sut = null!;

    private static readonly CancellationTokenSource Cts = new(TimeSpan.FromSeconds(5));
    private static readonly CancellationToken Token = Cts.Token;

    public CronBackgroundServiceTests()
    {
        _serviceScopeFactoryMock = Substitute.For<IServiceScopeFactory>();
        _serviceScopeMock = Substitute.For<IServiceScope>();
        _serviceProviderMock = Substitute.For<IServiceProvider>();
        _loggerMock = Substitute.For<ILogger<CronBackgroundService>>();
        _cronJobMock = Substitute.For<ICronJob>();
        _optionsMock = Options.Create(new CronJobOptions());
    }

    [Fact]
    public void When_CronBackgroundService_IsConstructed_AScopeIsRetrieved()
    {
        // Arrange
        MockCronJobAndServiceScope(_cronJobMock);

        // Act
        _sut = new CronBackgroundService(
            _cronJobMock,
            _serviceScopeFactoryMock,
            _loggerMock,
            _optionsMock);

        // Assert - CreateScope is called during construction
        _serviceScopeFactoryMock.Received(1).CreateScope();
    }

    [Fact]
    public async Task When_CronBackgroundService_IsRunning_ItsCronJob_IsExecuted()
    {
        // Arrange
        MockCronJobAndServiceScope(_cronJobMock);
        RunCronJobEverySecond();

        // Act
        await new CronBackgroundService(_cronJobMock, _serviceScopeFactoryMock, _loggerMock, _optionsMock)
            .StartAsync(Token);

        // Assert - ExecuteAsync has been received at least once, after 3 seconds.
        await AssertWithTimeout(async () =>
            await _cronJobMock.Received(Quantity.AtLeastOne()).ExecuteAsync(Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// This test is flaky, there's no way of knowing for how long it'll run on GitHub Actions.
    /// </summary>
    [Fact]
    public async Task When_CronBackgroundService_IsRunning_ItsCronJob_IsExecuted_TheRightNumberOfTimes()
    {
        // Arrange
        MockCronJobAndServiceScope(_cronJobMock);
        RunCronJobEverySecond();

        // Act
        await new CronBackgroundService(_cronJobMock, _serviceScopeFactoryMock, _loggerMock, _optionsMock)
            .StartAsync(Token);

        // Assert - ExecuteAsync has been received at least 5 times, after 5 seconds.
        // In CI, this might span over far more than 5 seconds, so the quantity just has to be 5-30
        await AssertWithTimeout(async () =>
                await _cronJobMock.Received(Quantity.Within(5, 30)).ExecuteAsync(Arg.Any<CancellationToken>()),
            TimeSpan.FromSeconds(5));
    }



    [Fact]
    public async Task When_CronBackgroundService_IsRunning_ItsCronJob_IsNotExecuted_MoreThanItShould()
    {
        // Arrange
        MockCronJobAndServiceScope(_cronJobMock);
        RunCronJobEveryMinute();

        // Act
        await new CronBackgroundService(
            _cronJobMock,
            _serviceScopeFactoryMock,
            _loggerMock,
            _optionsMock).StartAsync(Token);

        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert - ExecuteAsync has not been received at least 5 times, after 2 seconds.
        await Assert.ThrowsAsync<ReceivedCallsException>(async () =>
            await _cronJobMock.Received(5).ExecuteAsync(Arg.Any<CancellationToken>()));
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.StopAsync(Token);
        _sut.Dispose();
        GC.SuppressFinalize(this);
    }

    private void MockCronJobAndServiceScope(ICronJob cronJob)
    {
        _serviceScopeFactoryMock.CreateScope().Returns(_serviceScopeMock);
        _serviceScopeMock.ServiceProvider.Returns(_serviceProviderMock);
        _serviceProviderMock
            .GetService(cronJob.GetType())
            .ReturnsForAnyArgs(cronJob);
    }

    private static async Task AssertWithTimeout(Func<Task> assertion, TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        var token = cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await assertion.Invoke();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                await Task.Delay(100, token);
            }
        }
    }

    private void RunCronJobEverySecond() => _cronJobMock.CronSchedule.Returns(CronExpression.Parse("* * * * * *", CronFormat.IncludeSeconds));
    private void RunCronJobEveryMinute() => _cronJobMock.CronSchedule.Returns(CronExpression.Parse("* * * * *"));

}