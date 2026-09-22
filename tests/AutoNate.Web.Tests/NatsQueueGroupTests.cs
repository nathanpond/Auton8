using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The guarantee #660's components rest on, measured against the real NATS in
/// the test infra: two subscribers sharing ONE durable consumer receive a
/// message once between them, rather than once each.
/// </summary>
/// <remarks>
/// This is what an ephemeral consumer per process could never give. Before
/// #660 every replica of the app created its own consumer, so a bus message
/// was delivered to each of them and a message-start workflow started once per
/// replica -- by construction, not by accident. The complement below is that
/// pre-#660 shape, so a test that stopped distinguishing them would fail.
/// </remarks>
public sealed class NatsQueueGroupTests : IAsyncLifetime
{
    private const string NatsUrl = "nats://127.0.0.1:4222";
    private readonly string _stream = $"n8qg{Guid.NewGuid():N}"[..20];
    private NatsConnection? _nats;

    public async Task InitializeAsync()
    {
        _nats = new NatsConnection(new NatsOpts { Url = NatsUrl });
        await _nats.ConnectAsync();
        await new NatsJSContext(_nats).CreateStreamAsync(new StreamConfig(_stream, [$"{_stream}.>"])
        {
            Storage = StreamConfigStorage.Memory
        });
    }

    public async Task DisposeAsync()
    {
        if (_nats is null) return;
        try { await new NatsJSContext(_nats).DeleteStreamAsync(_stream); } catch (NatsJSApiException) { }
        await _nats.DisposeAsync();
    }

    [Fact]
    public async Task Two_consumers_bound_to_one_durable_share_the_message_rather_than_both_taking_it()
    {
        var js = new NatsJSContext(_nats!);
        var subject = $"{_stream}.topic";
        var durable = await js.CreateOrUpdateConsumerAsync(_stream, new ConsumerConfig
        {
            Name = "shared",
            DurableName = "shared",
            FilterSubject = subject,
            AckWait = TimeSpan.FromSeconds(30)
        });

        await js.PublishAsync(subject, "one");

        // Both clients ask the SAME consumer for work, which is what a queue
        // group over a durable consumer is: the server hands the message to one
        // of them and the other is told there is nothing.
        var first = await FetchCountAsync(durable);
        var second = await FetchCountAsync(durable);

        Assert.Equal(1, first + second);
    }

    /// <summary>
    /// The pre-#660 shape: a consumer each. Both receive the same message --
    /// which is exactly the double start #636 observed, one per subscriber.
    /// </summary>
    [Fact]
    public async Task Two_consumers_of_their_own_each_receive_the_message()
    {
        var js = new NatsJSContext(_nats!);
        var subject = $"{_stream}.topic";
        var mine = await js.CreateOrUpdateConsumerAsync(_stream, new ConsumerConfig
        {
            Name = "mine", DurableName = "mine", FilterSubject = subject, AckWait = TimeSpan.FromSeconds(30)
        });
        var yours = await js.CreateOrUpdateConsumerAsync(_stream, new ConsumerConfig
        {
            Name = "yours", DurableName = "yours", FilterSubject = subject, AckWait = TimeSpan.FromSeconds(30)
        });

        await js.PublishAsync(subject, "one");

        Assert.Equal(1, await FetchCountAsync(mine));
        Assert.Equal(1, await FetchCountAsync(yours));
    }

    private static async Task<int> FetchCountAsync(INatsJSConsumer consumer)
    {
        var count = 0;
        await foreach (var message in consumer.FetchAsync<string>(new NatsJSFetchOpts
        {
            MaxMsgs = 5,
            Expires = TimeSpan.FromSeconds(2)
        }))
        {
            count++;
            await message.AckAsync();
        }
        return count;
    }
}
