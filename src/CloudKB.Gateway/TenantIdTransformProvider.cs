using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace CloudKB.Gateway;


public class TenantIdTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            var userId = transformContext.HttpContext.User.FindFirstValue("user_id");
            if (!string.IsNullOrEmpty(userId))
            {
                transformContext.ProxyRequest.Headers.Remove("X-User-Id");
                transformContext.ProxyRequest.Headers.Add("X-User-Id", userId);
            }
            
            // Remove Authorization header from downstream request
            transformContext.ProxyRequest.Headers.Remove("Authorization");
            
            return System.Threading.Tasks.ValueTask.CompletedTask;
        });
    }
}
