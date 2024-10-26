using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WireMock.Owin.Mappers;

namespace WireMock.Owin
{
    public class WebSocketMiddleware
    {
        private readonly IWireMockMiddlewareOptions _options;

        public WebSocketMiddleware(IWireMockMiddlewareOptions options)
        {
            _options = options;
        }

        public async Task Invoke(HttpContext context, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            while (!result.CloseStatus.HasValue)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var responseMessage = await ProcessMessageAsync(message);

                var responseBuffer = Encoding.UTF8.GetBytes(responseMessage);
                await webSocket.SendAsync(new ArraySegment<byte>(responseBuffer, 0, responseBuffer.Length), result.MessageType, result.EndOfMessage, CancellationToken.None);

                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }

            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }

        private async Task<string> ProcessMessageAsync(string message)
        {
            // Use the custom message parser to process the message
            var requestMessage = await _options.CustomMessageParser.ParseRequestAsync(message);
            var responseMessage = await _options.CustomMessageParser.ParseResponseAsync(requestMessage);

            return responseMessage;
        }
    }
}
