namespace alloy_events_test;

public class Program
{
    public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureCmsDefaults()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
                webBuilder.ConfigureServices((context, services) =>
                {
                    services.AddHttpClient("CmpApiClient", (serviceProvider, client) =>
                    {
                        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                        var baseUrl = configuration.GetValue<string>("CmpSettings:BaseUrl");

                        if (!string.IsNullOrEmpty(baseUrl))
                        {
                            client.BaseAddress = new Uri(baseUrl);
                        }
                    });

                });
                //webBuilder.ConfigureKestrel(options =>
                //{
                //    options.ListenAnyIP(5000); // HTTP only
                //});
            });
}
