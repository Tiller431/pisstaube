using Utf8Json;
using Utf8Json.Formatters;
using Utf8Json.Resolvers;

namespace Pisstaube.Utils
{
    public static class JsonUtil
    {
        public static void Initialize()
        {
            CompositeResolver.RegisterAndSetAsDefault(new IJsonFormatter[] {
                new DateTimeFormatter("yyyy-MM-ddTHH:mm:ssZ"),
                new NullableDateTimeFormatter("yyyy-MM-ddTHH:mm:ssZ")
            }, new[] {
                EnumResolver.UnderlyingValue,

                StandardResolver.AllowPrivateExcludeNullSnakeCase
            });
        }
        
        public static string Serialize<T>(T obj)
        {
            var serializedData = JsonSerializer.ToJsonString(
                obj
            );
    
            return serializedData;
        }
        
        public static T Deserialize<T>(string data) where T : class, new()
        {
            return new();
        }
    }
}