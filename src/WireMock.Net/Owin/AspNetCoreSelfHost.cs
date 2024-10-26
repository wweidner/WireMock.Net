using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using WireMock.Logging;
using WireMock.Owin.Mappers;
using WireMock.Services;
using WireMock.Util;

namespace WireMock.Owin;

internal partial class AspNetCoreSelfHost : IOwinSelfHost
{
    private const string CorsPolicyName = "WireMock.Net - Policy";

    private readonly CancellationTokenSource _cts = new();
    private readonly IWireMockMiddlewareOptions _wireMockMiddlewareOptions;
    private readonly IWireMockLogger _logger;
    private readonly HostUrlOptions _urlOptions;

    private Exception _runningException;
    private IWebHost _host;

    public bool IsStarted { get; private set; }

    public List<string> Urls { get; } = new();

    public List<int> Ports { get; } = new();

    public Exception RunningException => _runningException;

    public AspNetCoreSelfHost(IWireMockMiddlewareOptions wireMockMiddlewareOptions, HostUrlOptions urlOptions)
    {
        _logger = wireMockMiddlewareOptions.Logger ?? new WireMockConsoleLogger();

        _wireMockMiddlewareOptions = wireMockMiddlewareOptions;
        _urlOptions = urlOptions;
    }

    public Task StartAsync()
    {
        var builder = new WebHostBuilder();

        if (string.IsNullOrEmpty(AppContext.BaseDirectory))
        {
            builder.UseContentRoot(Directory.GetCurrentDirectory());
        }

        _host = builder
            .UseSetting("suppressStatusMessages", "True")
            .ConfigureAppConfigurationUsingEnvironmentVariables()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_wireMockMiddlewareOptions);
                services.AddSingleton<IMappingMatcher, MappingMatcher>();
                services.AddSingleton<IRandomizerDoubleBetween0And1, RandomizerDoubleBetween0And1>();
                services.AddSingleton<IOwinRequestMapper, OwinRequestMapper>();
                services.AddSingleton<IOwinResponseMapper, OwinResponseMapper>();
                services.AddSingleton<IGuidUtils, GuidUtils>();

                AddCors(services);
                _wireMockMiddlewareOptions.AdditionalServiceRegistration?.Invoke(services);
            })
            .Configure(appBuilder =>
            {
                appBuilder.UseMiddleware<GlobalExceptionMiddleware>();

                UseCors(appBuilder);
                _wireMockMiddlewareOptions.PreWireMockMiddlewareInit?.Invoke(appBuilder);

                appBuilder.UseMiddleware<WireMockMiddleware>();

                _wireMockMiddlewareOptions.PostWireMockMiddlewareInit?.Invoke(appBuilder);

                UseWebSocket(appBuilder);
            })
            .UseKestrel(options =>
            {
                SetKestrelOptionsLimits(options);

                SetHttpsAndUrls(options, _wireMockMiddlewareOptions, _urlOptions.GetDetails());
            })
            .ConfigureKestrelServerOptions()
            .Build();

        return RunHost(_cts.Token);
    }

    private Task RunHost(CancellationToken token)
    {
        try
        {
            var appLifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();
            appLifetime.ApplicationStarted.Register(() =>
            {
                var addresses = _host.ServerFeatures
                    .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
                    .Addresses;

                foreach (var address in addresses)
                {
                    Urls.Add(address.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost"));

                    PortUtils.TryExtract(address, out _, out _, out _, out _, out var port);
                    Ports.Add(port);
                }

                IsStarted = true;
            });

            _logger.Info("Server using .NET 5.0");

            return _host.RunAsync(token);
        }
        catch (Exception e)
        {
            _runningException = e;
            _logger.Error(e.ToString());

            IsStarted = false;

            return Task.CompletedTask;
        }
    }

    public Task StopAsync()
    {
        _cts.Cancel();

        IsStarted = false;
        return _host.StopAsync();
    }

    private void UseWebSocket(IApplicationBuilder appBuilder)
    {
        appBuilder.Use(async (context, next) =>
        {
            if (context.Request.Path == "/ws")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    var webSocketMiddleware = new WebSocketMiddleware(_wireMockMiddlewareOptions);
                    await webSocketMiddleware.Invoke(context, webSocket);
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            }
            else
            {
                await next();
            }
        });
    }
}
