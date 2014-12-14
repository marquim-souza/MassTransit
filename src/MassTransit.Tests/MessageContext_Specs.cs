// Copyright 2007-2014 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.Tests
{
    using System;
    using System.Threading.Tasks;
    using EndpointConfigurators;
    using Magnum.Extensions;
    using NUnit.Framework;
    using NUnit.Framework;
    using Shouldly;
    using TestFramework;
    using TestFramework.Messages;


    [TestFixture]
    public class Sending_a_message_to_a_queue :
        InMemoryTestFixture
    {
        [Test]
        public async void Should_have_an_empty_fault_address()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            ping.FaultAddress.ShouldBe(null);
        }

        [Test]
        public async void Should_have_an_empty_response_address()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            ping.ResponseAddress.ShouldBe(null);
        }

        [Test]
        public async void Should_include_the_correlation_id()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            ping.CorrelationId.ShouldBe(_correlationId);
        }

        [Test]
        public async void Should_include_the_destination_address()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            ping.DestinationAddress.ShouldBe(InputQueueAddress);
        }

        [Test]
        public async void Should_include_the_header()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            object header;
            ping.ContextHeaders.TryGetHeader("One", out header);
            header.ShouldBe("1");
        }

        [Test]
        public async void Should_include_the_source_address()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            ping.SourceAddress.ShouldBe(BusAddress);
        }

        Task<ConsumeContext<PingMessage>> _ping;
        Guid _correlationId;

        [TestFixtureSetUp]
        public void Setup()
        {
            _correlationId = Guid.NewGuid();

            InputQueueSendEndpoint.Send(new PingMessage(), Pipe.New<SendContext<PingMessage>>(x => x.Execute(context =>
            {
                context.CorrelationId = _correlationId;
                context.ContextHeaders.Set("One", "1");
            })))
                .Wait(TestCancellationToken);
        }

        protected override void ConfigureInputQueueEndpoint(IReceiveEndpointConfigurator configurator)
        {
            _ping = Handler<PingMessage>(configurator);
        }
    }


    [TestFixture]
    public class Sending_a_request_to_a_queue :
        InMemoryTestFixture
    {
        [Test]
        public async void Should_have_received_the_response_on_the_handler()
        {
            PongMessage message = await _response;

            message.CorrelationId.ShouldBe(_ping.Result.Message.CorrelationId);
        }

        [Test]
        public async void Should_have_the_matching_correlation_id()
        {
            ConsumeContext<PongMessage> context = await _responseHandler;

            context.Message.CorrelationId.ShouldBe(_ping.Result.Message.CorrelationId);
        }

        [Test]
        public async void Should_include_the_destination_address()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            ping.DestinationAddress.ShouldBe(InputQueueAddress);
        }

        [Test]
        public async void Should_include_the_response_address()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            ping.ResponseAddress.ShouldBe(BusAddress);
        }

        [Test]
        public async void Should_include_the_source_address()
        {
            ConsumeContext<PingMessage> ping = await _ping;

            ping.SourceAddress.ShouldBe(BusAddress);
        }

        [Test]
        public async void Should_receive_the_response()
        {
            ConsumeContext<PongMessage> context = await _responseHandler;
        }

        Task<ConsumeContext<PingMessage>> _ping;
        Task<ConsumeContext<PongMessage>> _responseHandler;
        Task<Request<PingMessage>> _request;
        Task<PongMessage> _response;

        [TestFixtureSetUp]
        public void Setup()
        {
            _responseHandler = SubscribeHandler<PongMessage>();

            _request = Bus.Request(InputQueueAddress, new PingMessage(), x =>
            {
                _response = x.Handle<PongMessage>(async _ =>
                {
                });
            });
        }

        protected override void ConfigureInputQueueEndpoint(IReceiveEndpointConfigurator configurator)
        {
            _ping = Handler<PingMessage>(configurator, async x => await x.RespondAsync(new PongMessage(x.Message.CorrelationId)));
        }
    }


    [TestFixture]
    public class Sending_a_request_with_two_handlers :
        InMemoryTestFixture
    {
        [Test]
        public async void Should_have_received_the_actual_response()
        {
            PingNotSupported message = await _notSupported;

            message.CorrelationId.ShouldBe(_ping.Result.Message.CorrelationId);
        }

        [Test]
        public async void Should_not_complete_the_handler()
        {
            await _notSupported;

            await BusSendEndpoint.Send(new PongMessage((await _ping).Message.CorrelationId));

            Assert.Throws<TaskCanceledException>(async () =>
            {
                await _response;
            });
        }

        Task<ConsumeContext<PingMessage>> _ping;
        Task<ConsumeContext<PongMessage>> _responseHandler;
        Task<Request<PingMessage>> _request;
        Task<PongMessage> _response;
        Task<PingNotSupported> _notSupported;

        [TestFixtureSetUp]
        public void Setup()
        {
            _responseHandler = SubscribeHandler<PongMessage>();

            _request = Bus.Request(InputQueueAddress, new PingMessage(), x =>
            {
                _response = x.Handle<PongMessage>(async _ =>
                {
                });

                _notSupported = x.Handle<PingNotSupported>(async _ =>
                {
                });
            });
        }

        protected override void ConfigureInputQueueEndpoint(IReceiveEndpointConfigurator configurator)
        {
            _ping = Handler<PingMessage>(configurator, async x => await x.RespondAsync(new PingNotSupported(x.Message.CorrelationId)));
        }
    }


    [TestFixture]
    public class Sending_a_request_with_no_handler :
        InMemoryTestFixture
    {
        [Test]
        public async void Should_receive_a_request_timeout_exception_on_the_handler()
        {
            Assert.Throws<RequestTimeoutException>(async () => await _response);
        }

        [Test]
        public async void Should_receive_a_request_timeout_exception_on_the_request()
        {
            Assert.Throws<RequestTimeoutException>(async () =>
            {
                Request<PingMessage> request = await _request;

                await request.Task;
            });
        }

        Task<Request<PingMessage>> _request;
        Task<PongMessage> _response;

        [TestFixtureSetUp]
        public void Setup()
        {
            _request = Bus.Request(InputQueueAddress, new PingMessage(), x =>
            {
                x.Timeout = 1.Seconds();

                _response = x.Handle<PongMessage>(async _ =>
                {
                });
            });
        }
    }
}