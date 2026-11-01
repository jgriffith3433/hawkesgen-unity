// Not part of the package (outside Assets/HawkesGen): drives the package against the live service from batch mode.
//   Unity -batchmode -projectPath . -executeMethod HawkesGen.DevTests.HawkesGenSmokeTest.Run
//   env HG_COOKIE: a test account's hg_session cookie (the "browser" that approves). Optional: HG_SERVER (default the
//   window's server, hawkesgen.ai), HG_SPRITE=1 to also make a sprite sheet (a GPU clip: costs more than the rest).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HawkesGen.Editor;
using UnityEditor;
using UnityEngine;

namespace HawkesGen.DevTests
{
    public static class HawkesGenSmokeTest
    {
        public static void Run() => _ = RunAsync();

        static void Say(string s) => Debug.Log("[smoke] " + s);

        static async Task RunAsync()
        {
            int failures = 0;
            async Task Check(string name, Func<Task> body)
            {
                try { await body(); Say("PASS " + name); }
                catch (Exception e) { failures++; Say("FAIL " + name + ": " + e.Message); }
            }
            string cookie = Environment.GetEnvironmentVariable("HG_COOKIE");
            string serverOverride = Environment.GetEnvironmentVariable("HG_SERVER");
            bool sprite = Environment.GetEnvironmentVariable("HG_SPRITE") == "1";
            if (!string.IsNullOrEmpty(serverOverride)) HawkesGenSettings.SetServer(serverOverride.TrimEnd('/'));
            HawkesGenSettings.SignOut();
            HawkesGenSettings.SetClientId("");
            HawkesGenSettings.SetOutputFolder("Assets/_Dev/Output");

            await Check("sign in (browser approval simulated)", async () =>
            {
                string server = HawkesGenSettings.GetServer(); // EditorPrefs only work on the main thread
                await HawkesGenClient.SignInAsync(url =>
                {
                    // What the approve button does in the browser, then the browser following the redirect.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                        using (var http = new HttpClient(new HttpClientHandler { UseCookies = false }))
                        {
                            var body = "{\"query\":" + Json.Write(new Uri(url).Query.TrimStart('?')) + ",\"daily_cap\":500}";
                            var req = new HttpRequestMessage(HttpMethod.Post, server + "/v1/oauth/approve") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                            req.Headers.Add("Cookie", cookie);
                            var r = await http.SendAsync(req);
                            var redirect = Json.Str(Json.Parse(await r.Content.ReadAsStringAsync()), "redirect");
                            Debug.Log($"[smoke]   approve {(int)r.StatusCode} -> {redirect}");
                            var back = await http.GetAsync(redirect);
                            Debug.Log($"[smoke]   listener answered {(int)back.StatusCode}");
                        }
                        }
                        catch (Exception e) { Debug.Log("[smoke]   browser step failed: " + e); }
                    });
                });
                if (!HawkesGenSettings.IsSignedIn()) throw new Exception("no tokens");
            });

            await Check("account", async () =>
            {
                var a = await HawkesGenClient.CallToolAsync("account", null);
                Say($"  {Json.Str(a, "email")} has {Json.Num(a, "credits")} credits, via {Json.Str(a, "connection")}");
            });

            string knight = null;
            await Check("upload the sample", async () => { knight = await HawkesGenClient.UploadAsync(Path.GetFullPath("Assets/HawkesGen/Samples/Knight.png")); Say("  " + knight); });

            await Check("remove background -> sprite", async () =>
            {
                var r = await HawkesGenClient.RunAsync("remove_background", new Dictionary<string, object> { { "image", knight } }, (p, m) => Say($"  {p:P0} {m}"), default);
                var f = Json.Arr(r, "files")[0];
                var path = "Assets/_Dev/Output/" + Json.Str(f, "filename");
                await HawkesGenClient.DownloadAsync(Json.Str(f, "url"), Path.GetFullPath(path));
                AssetDatabase.Refresh();
                HawkesGenImporter.AsSprite(path);
                if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null) throw new Exception("not a sprite");
            });

            await Check("pixel art", async () =>
            {
                var steps = new List<object> { new Dictionary<string, object> { { "op", "trim" } }, new Dictionary<string, object> { { "op", "pixelate" }, { "size", 48 }, { "colors", 16 } } };
                var r = await HawkesGenClient.RunAsync("transform_image", new Dictionary<string, object> { { "image", knight }, { "steps", steps } }, null, default);
                if (Json.Arr(r, "files").Count != 1) throw new Exception("no file");
            });

            if (sprite) await Check("sprite sheet -> sliced frames + AnimationClip", async () =>
            {
                var r = await HawkesGenClient.RunAsync("make_sprite_sheet", new Dictionary<string, object> { { "image", knight }, { "motion", "walks in place, a looping walk cycle, side view" }, { "frames", 8 } }, (p, m) => Say($"  {p:P0} {m}"), default);
                string png = null, json = null;
                foreach (var f in Json.Arr(r, "files"))
                {
                    string mime = Json.Str(f, "mime"), title = Json.Str(f, "title");
                    if (title != "sprite sheet" && mime != "application/json") continue;
                    var path = "Assets/_Dev/Output/" + Json.Str(f, "filename");
                    await HawkesGenClient.DownloadAsync(Json.Str(f, "url"), Path.GetFullPath(path));
                    if (mime == "application/json") json = path; else png = path;
                }
                AssetDatabase.Refresh();
                var note = HawkesGenImporter.AsSpriteSheet(png, json);
                if (note != null) throw new Exception(note);
                int sprites = AssetDatabase.LoadAllAssetsAtPath(png).OfType<Sprite>().Count();
                var anim = AssetDatabase.LoadAssetAtPath<AnimationClip>(Path.ChangeExtension(png, null) + ".anim");
                Say($"  {sprites} sprites, clip {(anim ? anim.length.ToString("0.00") + " s, loops " + AnimationUtility.GetAnimationClipSettings(anim).loopTime : "missing")}");
                if (sprites != 8 || !anim) throw new Exception("slicing or clip failed");
            });

            Say(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
