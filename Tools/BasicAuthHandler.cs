using System;
using System.Net;
using System.Text;

namespace backgroundControl.Tools;

/// <summary>
/// HTTP Basic Auth 校验工具。
/// 规则：GET 请求（浏览、下载）放行；POST/DELETE 请求必须带正确凭据。
/// Basic Auth 是明文传输，仅限局域网使用。
/// </summary>
public static class BasicAuthHandler
{
    /// <summary>
    /// 校验请求的 Basic Auth，返回 true 表示通过，false 表示需要 401 挑战。
    /// </summary>
    public static bool TryAuthenticate(HttpListenerContext ctx, HttpConfig cfg)
    {
        // GET 请求（浏览、下载）放行，方便浏览器直接打开
        if (string.Equals(ctx.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            return true;

        var auth = ctx.Request.Headers["Authorization"];
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            Challenge(ctx);
            return false;
        }

        try
        {
            var encoded = auth.Substring(6).Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var idx     = decoded.IndexOf(':');
            if (idx < 0)
            {
                Challenge(ctx);
                return false;
            }
            var user = decoded.Substring(0, idx);
            var pass = decoded.Substring(idx + 1);

            if (user == cfg.Username && pass == cfg.Password) return true;

            Challenge(ctx);
            return false;
        }
        catch
        {
            Challenge(ctx);
            return false;
        }
    }

    private static void Challenge(HttpListenerContext ctx)
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"BackgroundControl\"");
        ctx.Response.ContentType  = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes("401 Unauthorized");
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }
}
