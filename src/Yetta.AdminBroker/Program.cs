using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yetta.AdminBroker;
using Yetta.Core;

try
{
    if (args is ["--install", var userSid])
    {
        Environment.ExitCode = await BrokerInstaller.Install(userSid);
    }
    else if (args is ["--remove"])
    {
        Environment.ExitCode = await BrokerInstaller.Remove();
    }
    else if (args is ["--service"])
    {
        using IHost host = Host.CreateDefaultBuilder(args)
            .UseWindowsService(config => config.ServiceName = AdminBrokerProtocol.ServiceName)
            .ConfigureServices(services => services.AddHostedService<BrokerWorker>())
            .Build();
        await host.RunAsync();
    }
    else
    {
        Console.Error.WriteLine("Yetta Administrator Broker is only a Windows service installer/runtime.");
        Environment.ExitCode = 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
