# Publishing to the Asset Store

Draft: https://publisher.unity.com/packages/1527894 ("hawkes-gen").

1. **Open this project** in Unity Hub with **Unity 6000.3.7f1**: Add > Add project from disk > `C:\Projects\hawkesgen-unity`.
   The Asset Store Publishing Tools are already installed (`Packages/manifest.json`).
2. **Try it once:** Window > Hawkes Gen > Sign in with your browser, then make a sprite. Optional, but worth it.
3. **Validate:** Tools > Asset Store > Validator. Choose the folder **Assets/HawkesGen**, category Tools, and Validate.
   Fix anything red (yellow is advice).
4. **Upload:** Tools > Asset Store > Uploader. Sign in with your **Unity publisher account** (only you can do this).
   Pick the draft **hawkes-gen**, choose **From Assets Folder**, select **Assets/HawkesGen**, leave dependencies off,
   and click **Export and Upload**.
5. **Fill in the draft** in the Publisher Portal: paste [LISTING.md](LISTING.md) (keep the account-and-credits disclosure at
   the very top), fill the AI description field, add the images, set the price to Free, then **Submit for review**.
   Unity's review takes from a few days to a couple of weeks.

Updating later: change the code, run the smoke test (README), validate, and upload a new version from the draft.
