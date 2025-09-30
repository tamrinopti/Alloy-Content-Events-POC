using alloy_events_test.CMPIntegration;
using alloy_events_test.Extensions;
using alloy_events_test.GcsBlobProvider;
using alloy_events_test.Services;
using EPiServer.Cms.Shell;
using EPiServer.Cms.Shell.UI.Configurations;
using EPiServer.Cms.UI.AspNetIdentity;
using EPiServer.OptimizelyIdentity;
using EPiServer.Scheduler;
using EPiServer.ServiceLocation;
using EPiServer.Web.Routing;
using GcsBlobProvider.GcsBlobProvider;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.OpenApi.Models;
using Optimizely.Cms.Preview1;

using Optimizely.Cms.DependencyInjection;

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
            .AddCms();
        #region cmp-cms-integration
        services.AddCmsCmpPublishing();

        // Allow the quick editor to be embedded into CMP via an iframe
        services.AddAntiforgery(options => {
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        // If using ASP.NET Identity
        // Allow the quick editor to be embedded into CMP via an iframe
        services.ConfigureApplicationCookie(options => {
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        // If using OptiID
        // Allow the quick editor to be embedded into CMP via an iframe
        services.Configure<CookieAuthenticationOptions>(OptimizelyIdentityDefaults.CookieSchemeName, options => {
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
        if (_webHostingEnvironment.IsProduction())
        {
            //services.AddCmsCloudPlatformSupport(_configuration);
        }

        if (_webHostingEnvironment.IsDevelopment())
        {
            // This automatically configures dev signing credentials to call Integration API
            services.Configure<CmsServiceOptions>(o => {
                o.AddDevelopmentSigningCredentials();
            });
        }

        #endregion

        services.AddAlloy()
            .AddAdminUserRegistration()
            .AddEmbeddedLocalization<Startup>();

        services.Configure<UploadOptions>(x =>
        {
            x.FileSizeLimit = 52_428_800; // 50MB
        });

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

        // Add CMP integration services
        services.AddHttpClient<CmpTokenService>();
        services.AddHttpClient<CmpIntegrationService>();
        services.AddHttpClient<CmpTaskCompletionService>(); // Add HttpClient for the new service

        services.AddSingleton<ICmpTokenService, CmpTokenService>();
        services.AddScoped<ICmpIntegrationService, CmpIntegrationService>();
        services.AddScoped<ICmpTaskCompletionService, CmpTaskCompletionService>(); // Register the new service
        services.AddScoped<ICmpStructuredContentService, CmpStructuredContentService>();

        // Register the new service
        services.AddScoped<ICmpTaskAssetService, CmpTaskAssetService>();
        //services.AddScoped<CmpContentEventHandler>();
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

        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseCmsCmpPublishingPreviewLinks();

        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllerRoute(name: "Default", pattern: "{controller}/{action}/{id?}");
            endpoints.MapControllers();
            endpoints.MapContent();
        });
    }
}
