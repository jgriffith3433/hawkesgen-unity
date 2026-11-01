# Dev tools (not part of the package)

Unity ignores this folder (it's outside `Assets`). To use these, copy the folder into `Assets/_Dev/Editor/`, let Unity
compile, use them, then delete `Assets/_Dev` again before validating or uploading.

- `HawkesGenSmokeTest.cs`: the live end-to-end test (see the repo README).
- `HawkesGenExport.cs`: exports `Assets/HawkesGen` as a `.unitypackage` (a direct download).
- `HawkesGenDemoScene.cs`: rebuilds `Samples/Hawkes Gen Demo.unity` (Tools > Hawkes Gen (dev) > Build Demo Scene).
- `make_unitypackage.py`: builds the same `.unitypackage` without Unity (it can stay open): `python DevTools/make_unitypackage.py ../hawkesgenwebsite/public/downloads/HawkesGen.unitypackage`
