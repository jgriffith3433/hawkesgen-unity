# Dev tools (not part of the package)

Unity ignores this folder (it's outside `Assets`). To use these, copy the folder into `Assets/_Dev/Editor/`, let Unity
compile, use them, then delete `Assets/_Dev` again before validating or uploading.

- `HawkesGenSmokeTest.cs`: the live end-to-end test (see the repo README).
- `HawkesGenExport.cs`: exports `Assets/HawkesGen` as a `.unitypackage` (the download on hawkesgen.com).
- `HawkesGenDemoScene.cs`: rebuilds `Samples/Hawkes Gen Demo.unity` (Tools > Hawkes Gen (dev) > Build Demo Scene).
