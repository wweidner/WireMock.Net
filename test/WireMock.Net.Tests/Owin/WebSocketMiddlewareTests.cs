using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using WireMock.Owin;
using Xunit;

namespace WireMock.Net.Tests.Owin
{
    public class WebSocketMiddlewareTests
    {
        private readonly Mock<IWireMockMiddlewareOptions> _mockOptions;
        private readonly IHost _host;

        public WebSocketMiddlewareTests()
        {
            _mockOptions = new Mock<IWireMockMiddlewareOptions>();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton(_mockOptions.Object);
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.UseMiddleware<WebSocketMiddleware>();
                    });
                })
                .Start();
        }

        [Fact]
        public async Task WebSocketMiddleware_ShouldHandleWebSocketConnection()
        {
            var client = _host.GetTestClient();
            var webSocket = await client.WebSockets.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

            var message = "Hello, WebSocket!";
            var buffer = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

            var receiveBuffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);

            var receivedMessage = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
            Assert.Equal(message, receivedMessage);

            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }

        [Fact]
        public async Task WebSocketMiddleware_ShouldProcessMessage()
        {
            var client = _host.GetTestClient();
            var webSocket = await client.WebSockets.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

            var message = "Test Message";
            var buffer = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

            var receiveBuffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);

            var receivedMessage = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
            Assert.Equal("Processed: Test Message", receivedMessage);

            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
    }
}
