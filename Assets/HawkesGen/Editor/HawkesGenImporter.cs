// Results into Unity assets: sprites, sprite sheets sliced into frames with an AnimationClip, PBR maps as a Material for
// the project's render pipeline, panoramas as a skybox Material.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if HAWKESGEN_SPRITE_EDITOR
using UnityEditor.U2D.Sprites;
#endif

namespace HawkesGen.Editor
{
    public static class HawkesGenImporter
    {
        /// <summary>A PNG as a sprite with transparency.</summary>
        public static void AsSprite(string path)
        {
            AssetDatabase.ImportAsset(path);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }

        /// <summary>A sprite sheet and its frame JSON: sliced into frames (with the 2D Sprite package) and an AnimationClip.
        /// Returns a note for the user, or null when everything was made.</summary>
        public static string AsSpriteSheet(string pngPath, string jsonPath)
        {
            AssetDatabase.ImportAsset(pngPath);
            var ti = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (ti == null) return "The sheet didn't import as a texture.";
            var sheet = Json.Parse(File.ReadAllText(jsonPath));
            var meta = Json.Obj(sheet, "meta");
            int height = (int)Json.Num(Json.Obj(meta, "size"), "h");
            float fps = (float)Json.Num(meta, "fps", 12);
            var frames = (Json.Obj(sheet, "frames") ?? new Dictionary<string, object>()).OrderBy((kv) => kv.Key).ToList();
            string baseName = Path.GetFileNameWithoutExtension(pngPath);

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
#if HAWKESGEN_SPRITE_EDITOR
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(ti);
            provider.InitSpriteEditorDataProvider();
            var rects = new List<SpriteRect>();
            foreach (var kv in frames)
            {
                var f = Json.Obj(kv.Value, "frame");
                float w = (float)Json.Num(f, "w"), h = (float)Json.Num(f, "h");
                // The JSON measures from the top; Unity measures from the bottom.
                rects.Add(new SpriteRect { name = $"{baseName}_{kv.Key}", rect = new Rect((float)Json.Num(f, "x"), height - (float)Json.Num(f, "y") - h, w, h), alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f), spriteID = GUID.Generate() });
            }
            provider.SetSpriteRects(rects.ToArray());
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select((r) => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply();
            ti.SaveAndReimport();

            var sprites = AssetDatabase.LoadAllAssetsAtPath(pngPath).OfType<Sprite>().OrderBy((s) => s.name).ToArray();
            if (sprites.Length > 0) MakeClip(sprites, fps, Path.ChangeExtension(pngPath, null) + ".anim");
            return null;
#else
            return "Install the 2D Sprite package (Window > Package Manager > Unity Registry > 2D Sprite) to have sheets sliced into frames and an AnimationClip made. The sheet and its frame JSON are saved.";
#endif
        }

        static void MakeClip(Sprite[] sprites, float fps, string path)
        {
            var clip = new AnimationClip { frameRate = fps };
            var binding = new EditorCurveBinding { type = typeof(SpriteRenderer), path = "", propertyName = "m_Sprite" };
            var keys = sprites.Select((s, i) => new ObjectReferenceKeyframe { time = i / fps, value = s }).ToArray();
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(path));
        }

        /// <summary>Albedo, normal and roughness maps as a Material for the project's render pipeline.</summary>
        public static Material AsMaterial(string folder, string name, string albedo, string normal, string roughness)
        {
            if (normal != null)
            {
                AssetDatabase.ImportAsset(normal);
                if (AssetImporter.GetAtPath(normal) is TextureImporter ni) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
            }
            if (roughness != null)
            {
                AssetDatabase.ImportAsset(roughness);
                if (AssetImporter.GetAtPath(roughness) is TextureImporter ri) { ri.sRGBTexture = false; ri.SaveAndReimport(); }
            }
            var pipeline = GraphicsSettings.currentRenderPipeline;
            string pipelineName = pipeline != null ? pipeline.GetType().Name : "";
            Shader shader = pipelineName.Contains("HDRenderPipeline") ? Shader.Find("HDRP/Lit") : pipeline != null ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            var mat = new Material(shader ? shader : Shader.Find("Standard"));
            var albedoTex = albedo != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(albedo) : null;
            var normalTex = normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null;
            foreach (var p in new[] { "_BaseMap", "_BaseColorMap", "_MainTex" }) if (albedoTex && mat.HasProperty(p)) mat.SetTexture(p, albedoTex);
            foreach (var p in new[] { "_BumpMap", "_NormalMap" }) if (normalTex && mat.HasProperty(p)) mat.SetTexture(p, normalTex);
            if (normalTex) mat.EnableKeyword("_NORMALMAP");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.mat");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>An equirectangular panorama as a skybox Material.</summary>
        public static Material AsSkybox(string folder, string name, string panorama)
        {
            AssetDatabase.ImportAsset(panorama);
            if (AssetImporter.GetAtPath(panorama) is TextureImporter ti) { ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = false; ti.maxTextureSize = 4096; ti.SaveAndReimport(); }
            var mat = new Material(Shader.Find("Skybox/Panoramic"));
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(panorama));
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.mat");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
