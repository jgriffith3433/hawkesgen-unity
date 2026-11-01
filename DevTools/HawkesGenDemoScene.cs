// Not part of the package: builds Assets/HawkesGen/Samples/Hawkes Gen Demo.unity (the sample sprite and the setup steps).
//   Tools > Hawkes Gen (dev) > Build Demo Scene, or: Unity -batchmode -quit -projectPath . -executeMethod HawkesGen.DevTests.HawkesGenDemoScene.Build
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HawkesGen.DevTests
{
    public static class HawkesGenDemoScene
    {
        const string Knight = "Assets/HawkesGen/Samples/Knight.png";
        const string ScenePath = "Assets/HawkesGen/Samples/Hawkes Gen Demo.unity";

        [MenuItem("Tools/Hawkes Gen (dev)/Build Demo Scene")]
        public static void Build()
        {
            // The sample image is a Sprite, ready for a sprite sheet.
            var ti = (TextureImporter)AssetImporter.GetAtPath(Knight);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.spritePixelsPerUnit = 200;
            ti.SaveAndReimport();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.orthographicSize = 3.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.957f, 0.949f, 0.929f);
            cam.transform.position = new Vector3(1.6f, 0f, -10f);

            var knight = new GameObject("Sample Sprite (Knight)", typeof(SpriteRenderer));
            knight.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Knight);
            knight.transform.position = new Vector3(-2.2f, 0f, 0f);

            Text("Title", "Hawkes Gen for Unity", new Vector3(0.6f, 2.2f, 0f), 0.11f, FontStyle.Bold);
            Text("Setup steps",
                "1. Open Window > Hawkes Gen.\n" +
                "2. Click \"Sign in with your browser\" and approve\n    Unity Editor on hawkesgen.ai.\n" +
                "3. Select Samples/Knight.png in the Project window.\n" +
                "4. Choose \"Animated sprite sheet\", type a motion\n    (\"walking in place, a looping walk cycle\"),\n    and click Generate.\n" +
                "5. The sheet arrives in Assets/Hawkes Gen Output,\n    sliced, with an AnimationClip: drag the clip\n    onto this sprite and press Play.\n\n" +
                "Uses credits from your hawkesgen.ai account.\n" +
                "See Documentation/Hawkes Gen Documentation.pdf.",
                new Vector3(0.6f, 1.6f, 0f), 0.065f, FontStyle.Normal);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[demo] " + ScenePath);
        }

        static void Text(string name, string text, Vector3 position, float size, FontStyle style)
        {
            var go = new GameObject(name, typeof(TextMesh));
            var tm = go.GetComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.fontStyle = style;
            tm.anchor = TextAnchor.UpperLeft;
            tm.color = new Color(0.102f, 0.098f, 0.082f);
            go.transform.position = position;
        }
    }
}
