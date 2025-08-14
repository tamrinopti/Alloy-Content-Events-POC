using sqlBlobProvider = SqlBlobProvider.src.EPiCode.SqlBlobProvider.SqlBlobProvider;

namespace alloy_events_test.SqlBlobProvider.src
{
    public static class SqlExtensions
    {
        public static void ConfigureServices(this IServiceCollection services)
        {
            // This provider will be added as the default
            //services.AddFileBlobProvider("myFileBlobProvider",  @"c:\path\to\file\blobs");
            services.AddBlobProvider<sqlBlobProvider>("SqlBlobProvider", defaultProvider: true);
            //services.Configure<sqlBlobProvider>(o => {
            //    o.AddProvider<sqlBlobProvider>("anotherCustomBlobProvider");
            //    o.DefaultProvider = "anotherCustomBlobProvider";
            //});
        }
    }
}
