using alloy_events_test.Extensions;
using alloy_events_test.Services;
using EPiServer.Cms.Shell;
using EPiServer.Cms.UI.AspNetIdentity;
using EPiServer.Scheduler;
using EPiServer.ServiceLocation;
using EPiServer.Web.Routing;
using Microsoft.OpenApi.Models;
using GcsBlobProvider.GcsBlobProvider;
using alloy_events_test.GcsBlobProvider;

namespace alloy_events_test;

public class Startup
{
    private readonly IWebHostEnvironment _webHostingEnvironment;
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration, IWebHostEnvironment webHostingEnvironment)
    {
        Configuration = configuration;
        _webHostingEnvironment = webHostingEnvironment;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        if (_webHostingEnvironment.IsDevelopment())
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(_webHostingEnvironment.ContentRootPath, "App_Data"));

            services.Configure<SchedulerOptions>(options => options.Enabled = false);
        }

        services
            .AddCmsAspNetIdentity<ApplicationUser>()
            .AddCms()
            .AddAlloy()
            .AddAdminUserRegistration()
            .AddEmbeddedLocalization<Startup>();

        services.Configure<GcsSettings>(Configuration.GetSection("GcsSettings"));
        services.AddBlobProvider<GcpBlobProvider>("GcsBlobProvider", defaultProvider: true);

        // Required by Wangkanai.Detection
        services.AddDetection();

        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromSeconds(10);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "StartPage API",
                Version = "v1",
                Description = "API for managing StartPage content in Episerver CMS 12",
            });
        });

        services.AddSingleton<EventPublisher>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // Required by Wangkanai.Detection
        app.UseDetection();
        app.UseSession();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllerRoute(name: "Default", pattern: "{controller}/{action}/{id?}");
            endpoints.MapControllers();
            endpoints.MapContent();
        });
    }
}
