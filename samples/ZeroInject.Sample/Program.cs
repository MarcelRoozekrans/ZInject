using Microsoft.Extensions.DependencyInjection;
using ZeroInject.Sample;

var services = new ServiceCollection();
services.AddZeroInjectSampleServices();

var provider = services.BuildServiceProvider();

var greeting = provider.GetRequiredService<IGreetingService>();
Console.WriteLine(greeting.Greet("World"));

var worker = provider.GetRequiredService<ScopedWorker>();
Console.WriteLine(worker.DoWork());
