// Window > Hawkes Gen: sign in with the browser, pick what to make, watch it progress, and get Unity assets back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace HawkesGen.Editor
{
    public class HawkesGenWindow : EditorWindow
    {
        enum Recipe { Sprite, SpriteSheet, EditImage, RemoveBackground, PixelArt, Material, Skybox, SoundEffect, VoiceLine, Music }
        readonly string[] RecipeNames = { "Sprite or image", "Animated sprite sheet", "Edit an image", "Remove background", "Pixel art", "Tileable material", "Skybox", "Sound effect", "Voice line", "Music" };
        readonly string[] RecipeCosts = { "about 5 credits (+2 transparent)", "about 50 credits", "about 5 credits", "2 credits", "2 credits", "about 10 credits", "about 5 credits", "about 8 credits", "about 10 credits", "about 20 credits" };
        readonly string[] Aspects = { "1:1", "16:9", "9:16", "4:3", "3:4", "3:2", "2:3" };

        class Job
        {
            public string Title;
            public float Progress;
            public string Message = "Starting";
            public string Error;
            public bool Done;
            public readonly List<string> Results = new List<string>();
            public string Note;
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
        }

        [SerializeField] Recipe recipe;
        [SerializeField] string prompt = "";
        [SerializeField] int aspect;
        [SerializeField] bool transparent = true;
        [SerializeField] Texture2D source;
        [SerializeField] int frames = 8;
        [SerializeField] int pixelSize = 64;
        [SerializeField] int pixelColors = 16;
        [SerializeField] bool outline = true;
        [SerializeField] float seconds = 2f;
        [SerializeField] bool loop;
        [SerializeField] int voiceIndex;

        readonly List<Job> _jobs = new List<Job>();
        string _email;
        double _credits = -1;
        string _status;
        bool _busySigningIn;
        List<(string id, string name)> _voices;
        Vector2 _scroll;

        [MenuItem("Window/Hawkes Gen")]
        public static void Open() => GetWindow<HawkesGenWindow>("Hawkes Gen").minSize = new Vector2(340, 420);

        void OnEnable()
        {
            if (HawkesGenSettings.IsSignedIn()) _ = RefreshAccountAsync();
        }

        void Update()
        {
            if (_jobs.Any((j) => !j.Done)) Repaint();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawAccount();
            if (HawkesGenSettings.IsSignedIn())
            {
                EditorGUILayout.Space(8);
                DrawRecipe();
                EditorGUILayout.Space(8);
                DrawJobs();
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ account

        void DrawAccount()
        {
            EditorGUILayout.LabelField("Hawkes Gen", EditorStyles.boldLabel);
            if (!HawkesGenSettings.IsSignedIn())
            {
                EditorGUILayout.HelpBox("Make sprites, sprite sheets, materials, skyboxes, sounds, voices and music from a prompt. Generation runs on Hawkes Gen's servers and uses credits from your hawkesgen.com account (new accounts get 50 free).", MessageType.Info);
                using (new EditorGUI.DisabledScope(_busySigningIn))
                {
                    if (GUILayout.Button(_busySigningIn ? "Waiting for your browser..." : "Sign in with your browser", GUILayout.Height(30))) _ = SignInAsync();
                }
                if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Warning);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(_email ?? "Signed in", _credits >= 0 ? $"{_credits:N0} credits" : "");
                if (GUILayout.Button("Buy credits", GUILayout.Width(90))) Application.OpenURL(HawkesGenSettings.GetServer() + "/app#credits");
                if (GUILayout.Button("↻", GUILayout.Width(24))) _ = RefreshAccountAsync();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                HawkesGenSettings.SetOutputFolder(EditorGUILayout.TextField("Save to", HawkesGenSettings.GetOutputFolder()));
                if (GUILayout.Button("Sign out", GUILayout.Width(70))) { HawkesGenSettings.SignOut(); _email = null; _credits = -1; }
            }
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Warning);
        }

        async Task SignInAsync()
        {
            _busySigningIn = true;
            _status = null;
            try
            {
                await HawkesGenClient.SignInAsync();
                await RefreshAccountAsync();
            }
            catch (Exception e)
            {
                _status = e.Message;
            }
            finally
            {
                _busySigningIn = false;
                Repaint();
            }
        }

        async Task RefreshAccountAsync()
        {
            try
            {
                var a = await HawkesGenClient.CallToolAsync("account", null);
                _email = Json.Str(a, "email");
                _credits = Json.Num(a, "credits", -1);
                _status = null;
            }
            catch (Exception e)
            {
                _status = e.Message;
            }
            Repaint();
        }

        // ------------------------------------------------------------------ what to make

        void DrawRecipe()
        {
            recipe = (Recipe)EditorGUILayout.Popup("Make", (int)recipe, RecipeNames);
            bool needsSource = recipe == Recipe.SpriteSheet || recipe == Recipe.EditImage || recipe == Recipe.RemoveBackground || recipe == Recipe.PixelArt;
            if (needsSource)
            {
                if (source == null && Selection.activeObject is Texture2D picked) source = picked;
                source = (Texture2D)EditorGUILayout.ObjectField("Image", source, typeof(Texture2D), false);
            }
            switch (recipe)
            {
                case Recipe.Sprite:
                    prompt = PromptField("What to draw, with the style");
                    aspect = EditorGUILayout.Popup("Aspect", aspect, Aspects);
                    transparent = EditorGUILayout.Toggle("Transparent background", transparent);
                    break;
                case Recipe.SpriteSheet:
                    prompt = PromptField("The animation (e.g. run cycle facing right)");
                    frames = EditorGUILayout.IntSlider("Frames", frames, 2, 32);
                    break;
                case Recipe.EditImage: prompt = PromptField("What to change"); break;
                case Recipe.PixelArt:
                    pixelSize = EditorGUILayout.IntSlider("Size (px)", pixelSize, 16, 256);
                    pixelColors = EditorGUILayout.IntSlider("Colors", pixelColors, 2, 64);
                    outline = EditorGUILayout.Toggle("1 px outline", outline);
                    break;
                case Recipe.Material: prompt = PromptField("The surface (e.g. mossy cobblestone)"); break;
                case Recipe.Skybox: prompt = PromptField("The environment"); break;
                case Recipe.SoundEffect:
                    prompt = PromptField("The sound");
                    seconds = EditorGUILayout.Slider("Seconds (0 = automatic)", seconds, 0f, 30f);
                    loop = EditorGUILayout.Toggle("Seamless loop", loop);
                    break;
                case Recipe.VoiceLine:
                    prompt = PromptField("The line ([whispers], [shouting] work)");
                    if (_voices == null) { if (GUILayout.Button("Load voices")) _ = LoadVoicesAsync(); }
                    else voiceIndex = EditorGUILayout.Popup("Voice", Mathf.Clamp(voiceIndex, 0, _voices.Count - 1), _voices.Select((v) => v.name).ToArray());
                    break;
                case Recipe.Music:
                    prompt = PromptField("Style, mood, instruments");
                    seconds = EditorGUILayout.Slider("Seconds", Mathf.Max(seconds, 10f), 10f, 240f);
                    break;
            }
            EditorGUILayout.LabelField(RecipeCosts[(int)recipe], EditorStyles.miniLabel);
            bool ready = (!needsSource || source != null) && (recipe == Recipe.RemoveBackground || recipe == Recipe.PixelArt || !string.IsNullOrWhiteSpace(prompt)) && (recipe != Recipe.VoiceLine || _voices != null && _voices.Count > 0);
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("Generate", GUILayout.Height(30))) _ = RunAsync(recipe);
            }
        }

        string PromptField(string label)
        {
            EditorGUILayout.LabelField(label);
            return EditorGUILayout.TextArea(prompt, EditorStyles.textArea, GUILayout.MinHeight(48));
        }

        async Task LoadVoicesAsync()
        {
            try
            {
                var r = await HawkesGenClient.CallToolAsync("list_voices", null);
                _voices = (Json.Arr(r, "voices") ?? new List<object>()).Select((v) => (Json.Str(v, "id"), Json.Str(v, "name"))).ToList();
            }
            catch (Exception e) { _status = e.Message; }
            Repaint();
        }

        // ------------------------------------------------------------------ running it

        async Task RunAsync(Recipe r)
        {
            var job = new Job { Title = $"{RecipeNames[(int)r]}{(string.IsNullOrWhiteSpace(prompt) ? "" : ": " + Trim(prompt, 40))}" };
            _jobs.Insert(0, job);
            string what = prompt;
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
            try
            {
                string input = null;
                if (sourcePath != null && (r == Recipe.SpriteSheet || r == Recipe.EditImage || r == Recipe.RemoveBackground || r == Recipe.PixelArt))
                {
                    job.Message = "Uploading the image";
                    input = await HawkesGenClient.UploadAsync(Path.GetFullPath(sourcePath));
                }
                var (tool, args) = Request(r, what, input);
                var result = await HawkesGenClient.RunAsync(tool, args, (p, m) => { job.Progress = p; job.Message = m; }, job.Cancel.Token);
                job.Message = "Saving into the project";
                await SaveAsync(r, what, result, job);
                job.Message = $"Done · {Json.Num(result, "credits"):N0} credits";
                _ = RefreshAccountAsync();
            }
            catch (OperationCanceledException) { job.Error = "Stopped (the server finishes the step it's on; anything unfinished is refunded)."; }
            catch (Exception e) { job.Error = e.Message; }
            finally
            {
                job.Done = true;
                job.Progress = 1;
                Repaint();
            }
        }

        (string tool, Dictionary<string, object> args) Request(Recipe r, string what, string input)
        {
            switch (r)
            {
                case Recipe.Sprite: return ("generate_image", new Dictionary<string, object> { { "prompt", what }, { "aspect", Aspects[aspect] }, { "transparent", transparent } });
                case Recipe.SpriteSheet: return ("make_sprite_sheet", new Dictionary<string, object> { { "image", input }, { "motion", what }, { "frames", frames }, { "loop", true } });
                case Recipe.EditImage: return ("edit_image", new Dictionary<string, object> { { "image", input }, { "instruction", what } });
                case Recipe.RemoveBackground: return ("remove_background", new Dictionary<string, object> { { "image", input } });
                case Recipe.PixelArt:
                    var steps = new List<object> { Step("trim"), new Dictionary<string, object> { { "op", "pixelate" }, { "size", pixelSize }, { "colors", pixelColors } } };
                    if (outline) steps.Add(new Dictionary<string, object> { { "op", "outline" }, { "width", 1 } });
                    return ("transform_image", new Dictionary<string, object> { { "image", input }, { "steps", steps } });
                case Recipe.Material: return ("make_texture", new Dictionary<string, object> { { "surface", what } });
                case Recipe.Skybox: return ("make_skybox", new Dictionary<string, object> { { "prompt", what } });
                case Recipe.SoundEffect:
                    var sfx = new Dictionary<string, object> { { "description", what }, { "loop", loop } };
                    if (seconds > 0.4f) sfx["seconds"] = Math.Round(seconds, 1);
                    return ("sound_effect", sfx);
                case Recipe.VoiceLine: return ("voice_line", new Dictionary<string, object> { { "text", what }, { "voice", _voices[voiceIndex].id } });
                default: return ("make_music", new Dictionary<string, object> { { "style", what }, { "seconds", Math.Round(seconds) } });
            }
        }

        static Dictionary<string, object> Step(string op) => new Dictionary<string, object> { { "op", op } };

        async Task SaveAsync(Recipe r, string what, Dictionary<string, object> result, Job job)
        {
            string folder = HawkesGenSettings.GetOutputFolder().TrimEnd('/');
            if (!folder.StartsWith("Assets")) folder = "Assets/Hawkes Gen Output";
            Directory.CreateDirectory(folder);
            var saved = new List<(string path, string title, string mime)>();
            foreach (var f in Json.Arr(result, "files") ?? new List<object>())
            {
                string mime = Json.Str(f, "mime", "");
                string title = Json.Str(f, "title", "");
                if (r == Recipe.SpriteSheet && (mime == "video/mp4" || mime == "image/gif")) continue; // the clip and preview aren't needed in Unity
                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Json.Str(f, "filename", "hawkesgen")}");
                await HawkesGenClient.DownloadAsync(Json.Str(f, "url"), Path.GetFullPath(path));
                saved.Add((path, title, mime));
            }
            AssetDatabase.Refresh();
            string name = Slug(what, RecipeNames[(int)r]);
            switch (r)
            {
                case Recipe.Sprite:
                case Recipe.RemoveBackground:
                case Recipe.PixelArt:
                case Recipe.EditImage:
                    foreach (var s in saved.Where((s) => s.mime.StartsWith("image/"))) HawkesGenImporter.AsSprite(s.path);
                    break;
                case Recipe.SpriteSheet:
                    var png = saved.FirstOrDefault((s) => s.title == "sprite sheet").path;
                    var json = saved.FirstOrDefault((s) => s.mime == "application/json").path;
                    if (png != null && json != null) job.Note = HawkesGenImporter.AsSpriteSheet(png, json);
                    break;
                case Recipe.Material:
                    string Find(string map) => saved.FirstOrDefault((s) => s.title == map).path;
                    var mat = HawkesGenImporter.AsMaterial(folder, name, Find("albedo"), Find("normal"), Find("roughness"));
                    job.Results.Add(AssetDatabase.GetAssetPath(mat));
                    break;
                case Recipe.Skybox:
                    var pano = saved.FirstOrDefault((s) => s.mime.StartsWith("image/")).path;
                    if (pano != null) job.Results.Add(AssetDatabase.GetAssetPath(HawkesGenImporter.AsSkybox(folder, name, pano)));
                    break;
            }
            job.Results.AddRange(saved.Select((s) => s.path));
            AssetDatabase.SaveAssets();
            var first = job.Results.Count > 0 ? AssetDatabase.LoadMainAssetAtPath(job.Results[0]) : null;
            if (first) EditorGUIUtility.PingObject(first);
        }

        void DrawJobs()
        {
            if (_jobs.Count == 0) return;
            EditorGUILayout.LabelField("Recent", EditorStyles.boldLabel);
            foreach (var job in _jobs.Take(12).ToList())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(job.Title, EditorStyles.wordWrappedLabel);
                    if (job.Error != null) EditorGUILayout.HelpBox(job.Error, MessageType.Error);
                    else
                    {
                        var rect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
                        EditorGUI.ProgressBar(rect, job.Progress, job.Message);
                    }
                    if (!string.IsNullOrEmpty(job.Note)) EditorGUILayout.HelpBox(job.Note, MessageType.Info);
                    foreach (var path in job.Results)
                    {
                        if (GUILayout.Button(Path.GetFileName(path), EditorStyles.linkLabel)) EditorGUIUtility.PingObject(Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(path));
                    }
                    if (!job.Done && GUILayout.Button("Stop", GUILayout.Width(60))) job.Cancel.Cancel();
                }
            }
        }

        static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        static string Slug(string s, string fallback)
        {
            var words = new string((s ?? "").ToLowerInvariant().Select((c) => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(4);
            string slug = string.Join("-", words);
            return string.IsNullOrEmpty(slug) ? fallback.ToLowerInvariant().Replace(' ', '-') : slug;
        }
    }
}
