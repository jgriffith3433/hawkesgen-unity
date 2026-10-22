// The Hawkes Gen service from the Unity editor: browser sign-in (OAuth with PKCE and a loopback redirect, the same
// approval as Cursor or VS Code), then the Hawkes Gen MCP server's tools over HTTPS (JSON-RPC), file uploads and downloads.
// Sign-in tokens live in EditorPrefs on this computer only: they are never written into the project or a build.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace HawkesGen.Editor
{
    /// <summary>Where Hawkes Gen is, and this computer's sign-in.</summary>
    public static class HawkesGenSettings
    {
        // Methods over EditorPrefs, no static fields or properties: nothing is kept between Play mode sessions.
        static string Key(string name) => "HawkesGen." + name;

        public static string GetServer() => EditorPrefs.GetString(Key("Server"), "https://hawkesgen.com").TrimEnd('/');
        public static void SetServer(string value) => EditorPrefs.SetString(Key("Server"), value);

        public static string GetAccessToken() => EditorPrefs.GetString(Key("Access"), "");
        public static void SetAccessToken(string value) => EditorPrefs.SetString(Key("Access"), value);
        public static string GetRefreshToken() => EditorPrefs.GetString(Key("Refresh"), "");
        public static void SetRefreshToken(string value) => EditorPrefs.SetString(Key("Refresh"), value);
        public static string GetClientId() => EditorPrefs.GetString(Key("ClientId." + GetServer()), "");
        public static void SetClientId(string value) => EditorPrefs.SetString(Key("ClientId." + GetServer()), value);

        /// <summary>The folder (per project) results are saved into.</summary>
        public static string GetOutputFolder() => EditorPrefs.GetString(Key("Output." + Application.dataPath), "Assets/Hawkes Gen Output");
        public static void SetOutputFolder(string value) => EditorPrefs.SetString(Key("Output." + Application.dataPath), value);

        public static bool IsSignedIn() => !string.IsNullOrEmpty(GetRefreshToken()) || !string.IsNullOrEmpty(GetAccessToken());

        public static void SignOut()
        {
            SetAccessToken("");
            SetRefreshToken("");
        }
    }

    public class HawkesGenException : Exception
    {
        public HawkesGenException(string message) : base(message) { }
    }

    /// <summary>Calls to Hawkes Gen. Every method is async and safe to await from editor code.</summary>
    public static class HawkesGenClient
    {
        // No static state (safe with Enter Play Mode Options / domain reload off): a client per call.
        static HttpClient NewHttp() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // ------------------------------------------------------------------ sign-in

        /// <summary>Opens the browser to sign in and approve this editor, and waits for the answer (up to 5 minutes).
        /// openUrl replaces the browser (automated tests).</summary>
        public static async Task SignInAsync(Action<string> openUrl = null, CancellationToken cancel = default)
        {
            string server = HawkesGenSettings.GetServer();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string redirect = $"http://127.0.0.1:{port}/callback";
                string clientId = await EnsureClientAsync(server);
                string verifier = Base64Url(RandomBytes(32));
                string challenge = Base64Url(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                string state = Base64Url(RandomBytes(16));
                string url = $"{server}/oauth/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirect)}"
                    + $"&code_challenge={challenge}&code_challenge_method=S256&state={state}&scope=generate&resource={Uri.EscapeDataString(server + "/mcp")}";
                (openUrl ?? Application.OpenURL)(url);

                var accept = listener.AcceptTcpClientAsync();
                var done = await Task.WhenAny(accept, Task.Delay(TimeSpan.FromMinutes(5), cancel));
                if (done != accept) throw new HawkesGenException("Sign-in timed out. Try again.");
                string code;
                using (var conn = accept.Result)
                using (var stream = conn.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    string requestLine = await reader.ReadLineAsync() ?? "";
                    var query = ParseQuery(requestLine.Split(' ').Length > 1 ? requestLine.Split(' ')[1] : "");
                    bool ok = query.TryGetValue("code", out code) && query.TryGetValue("state", out var gotState) && gotState == state;
                    string page = ok
                        ? "<h2 style='font-family:Georgia,serif'>Signed in to Hawkes Gen</h2><p style='font-family:sans-serif'>Go back to Unity. You can close this tab.</p>"
                        : "<h2 style='font-family:Georgia,serif'>Sign-in didn't finish</h2><p style='font-family:sans-serif'>Go back to Unity and try again.</p>";
                    byte[] body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>Hawkes Gen</title><body style='background:#f4f2ed;color:#1a1915;padding:48px'>{page}</body>");
                    byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, 0, head.Length);
                    await stream.WriteAsync(body, 0, body.Length);
                    if (!ok) throw new HawkesGenException(query.TryGetValue("error", out var err) ? $"Sign-in was cancelled ({err})." : "Sign-in failed. Try again.");
                }

                var tokens = await PostFormAsync(server + "/oauth/token", new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" }, { "code", code }, { "code_verifier", verifier }, { "client_id", clientId }, { "redirect_uri", redirect },
                });
                SaveTokens(tokens);
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>This editor registered with the server once (dynamic client registration).</summary>
        static async Task<string> EnsureClientAsync(string server)
        {
            if (!string.IsNullOrEmpty(HawkesGenSettings.GetClientId())) return HawkesGenSettings.GetClientId();
            var reg = await SendJsonAsync(HttpMethod.Post, server + "/oauth/register", new Dictionary<string, object>
            {
                { "client_name", "Unity Editor" }, { "redirect_uris", new List<object> { "http://127.0.0.1/callback" } },
                { "grant_types", new List<object> { "authorization_code", "refresh_token" } }, { "token_endpoint_auth_method", "none" },
            }, auth: false);
            string id = Json.Str(reg, "client_id");
            if (string.IsNullOrEmpty(id)) throw new HawkesGenException("The server didn't register this editor.");
            HawkesGenSettings.SetClientId(id);
            return id;
        }

        static void SaveTokens(object tokens)
        {
            string access = Json.Str(tokens, "access_token");
            if (string.IsNullOrEmpty(access)) throw new HawkesGenException(Json.Str(tokens, "error_description", "Sign-in failed."));
            HawkesGenSettings.SetAccessToken(access);
            HawkesGenSettings.SetRefreshToken(Json.Str(tokens, "refresh_token", HawkesGenSettings.GetRefreshToken()));
        }

        static async Task<bool> RefreshAsync()
        {
            if (string.IsNullOrEmpty(HawkesGenSettings.GetRefreshToken())) return false;
            try
            {
                var tokens = await PostFormAsync(HawkesGenSettings.GetServer() + "/oauth/token", new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", HawkesGenSettings.GetRefreshToken() } });
                SaveTokens(tokens);
                return true;
            }
            catch (HawkesGenException)
            {
                HawkesGenSettings.SignOut();
                return false;
            }
        }

        // ------------------------------------------------------------------ the MCP server's tools

        /// <summary>Calls one of the server's tools; returns its structured result. Tool errors come back as exceptions.</summary>
        public static async Task<Dictionary<string, object>> CallToolAsync(string tool, Dictionary<string, object> args)
        {
            var request = new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" }, { "id", Guid.NewGuid().ToString("N") }, { "method", "tools/call" },
                { "params", new Dictionary<string, object> { { "name", tool }, { "arguments", args ?? new Dictionary<string, object>() } } },
            };
            var answer = await SendJsonAsync(HttpMethod.Post, HawkesGenSettings.GetServer() + "/mcp", request, auth: true);
            var error = Json.Obj(answer, "error");
            if (error != null) throw new HawkesGenException(Json.Str(error, "message", "The server refused the call."));
            var result = Json.Obj(answer, "result");
            if (Json.Bool(result, "isError"))
            {
                var content = Json.Arr(result, "content");
                throw new HawkesGenException(content != null && content.Count > 0 ? Json.Str(content[0], "text", "The tool failed.") : "The tool failed.");
            }
            return Json.Obj(result, "structuredContent") ?? new Dictionary<string, object>();
        }

        /// <summary>Runs a tool to the end, reporting progress (0-1 and a sentence) as the server reports it.</summary>
        public static async Task<Dictionary<string, object>> RunAsync(string tool, Dictionary<string, object> args, Action<float, string> progress, CancellationToken cancel)
        {
            var r = await CallToolAsync(tool, args);
            while (Json.Str(r, "state") == "running")
            {
                cancel.ThrowIfCancellationRequested();
                var p = Json.Obj(r, "progress");
                double total = Math.Max(1, Json.Num(p, "total", Json.Num(r, "steps", 1)));
                progress?.Invoke((float)(Json.Num(p, "progress", Json.Num(r, "step", 1) - 1) / total), Json.Str(p, "message", Json.Str(r, "detail", "Working")));
                r = await CallToolAsync("get_task", new Dictionary<string, object> { { "task_id", Json.Str(r, "task_id") }, { "wait_seconds", 5 } });
            }
            progress?.Invoke(1f, "Done");
            return r;
        }

        /// <summary>Uploads a file from the project; returns its asset ref ("asset:a_...") for other tools.</summary>
        public static async Task<string> UploadAsync(string path)
        {
            var link = Json.Obj(await CallToolAsync("upload_file", new Dictionary<string, object> { { "filename", Path.GetFileName(path) } }), "upload");
            string url = Json.Str(link, "url");
            if (string.IsNullOrEmpty(url)) throw new HawkesGenException("The server didn't give an upload link.");
            using (var http = NewHttp())
            using (var content = new ByteArrayContent(File.ReadAllBytes(path)))
            using (var r = await http.PutAsync(url, content))
            {
                string text = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) throw new HawkesGenException($"Upload failed: {Json.Str(SafeParse(text), "error", r.ReasonPhrase)}");
                return Json.Str(SafeParse(text), "ref");
            }
        }

        /// <summary>Saves a result's download link to a file.</summary>
        public static async Task DownloadAsync(string url, string path)
        {
            using (var http = NewHttp())
            using (var r = await http.GetAsync(url))
            {
                if (!r.IsSuccessStatusCode) throw new HawkesGenException($"Download failed ({(int)r.StatusCode}).");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, await r.Content.ReadAsByteArrayAsync());
            }
        }

        // ------------------------------------------------------------------ HTTP

        static async Task<object> SendJsonAsync(HttpMethod method, string url, object body, bool auth)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using (var req = new HttpRequestMessage(method, url))
                {
                    req.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
                    req.Headers.Accept.ParseAdd("application/json");
                    req.Headers.TryAddWithoutValidation("X-Hawkes-Client", "unity/1.0.0");
                    if (auth)
                    {
                        if (string.IsNullOrEmpty(HawkesGenSettings.GetAccessToken()) && !await RefreshAsync()) throw new HawkesGenException("Sign in to Hawkes Gen first.");
                        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", HawkesGenSettings.GetAccessToken());
                    }
                    using (var http = NewHttp())
                    using (var r = await http.SendAsync(req))
                    {
                        string text = await r.Content.ReadAsStringAsync();
                        if (auth && r.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                        {
                            HawkesGenSettings.SetAccessToken("");
                            if (await RefreshAsync()) continue;
                            throw new HawkesGenException("Your sign-in expired. Sign in again.");
                        }
                        var parsed = SafeParse(text);
                        if (!r.IsSuccessStatusCode) throw new HawkesGenException(Json.Str(parsed, "error_description", Json.Str(parsed, "error", $"The server answered {(int)r.StatusCode}.")));
                        return parsed;
                    }
                }
            }
            throw new HawkesGenException("Sign in to Hawkes Gen first.");
        }

        static async Task<object> PostFormAsync(string url, Dictionary<string, string> form)
        {
            using (var http = NewHttp())
            using (var content = new FormUrlEncodedContent(form))
            using (var r = await http.PostAsync(url, content))
            {
                var parsed = SafeParse(await r.Content.ReadAsStringAsync());
                if (!r.IsSuccessStatusCode) throw new HawkesGenException(Json.Str(parsed, "error_description", "The server refused the sign-in."));
                return parsed;
            }
        }

        static object SafeParse(string text)
        {
            try { return Json.Parse(text); }
            catch (FormatException) { return new Dictionary<string, object> { { "error", text.Length > 200 ? text.Substring(0, 200) : text } }; }
        }

        static Dictionary<string, string> ParseQuery(string pathAndQuery)
        {
            var result = new Dictionary<string, string>();
            int q = pathAndQuery.IndexOf('?');
            if (q < 0) return result;
            foreach (var part in pathAndQuery.Substring(q + 1).Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) result[Uri.UnescapeDataString(part.Substring(0, eq))] = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
            }
            return result;
        }

        static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
