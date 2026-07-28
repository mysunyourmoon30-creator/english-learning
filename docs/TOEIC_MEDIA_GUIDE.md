# TOEIC media admission guide

Only original or explicitly licensed content may enter the production catalog.
Do not reproduce ETS/TOEIC questions, recordings, photographs, or answer material.

For each listening item:

1. obtain a human recording and license evidence;
2. record the accent as `US`, `UK`, `Australian`, or `Canadian`;
3. normalize the approved master to -18 through -14 LUFS with true peak at or
   below -1 dBTP;
4. export MP3 and calculate its SHA-256;
5. upload it to the configured private object-storage container;
6. for Part 1, upload a licensed photograph to the HTTPS CDN and record its
   image license ID;
7. have a qualified English reviewer approve clarity and item alignment;
8. add the metadata to `content/toeic-media/manifest.json`.

The authenticated questions API returns a stable `contentKey` for Parts 1–4.
Use that value as the manifest `contentKey`. `audioObjectKey` is the 64-character
hexadecimal object key stored by the audio store. The runtime ignores entries that
do not contain the human-source license and approval fields.

Validate while drafting:

```powershell
.\scripts\Test-ToeicMediaManifest.ps1 -Mode Draft
```

Validate the complete release catalog:

```powershell
.\scripts\Test-ToeicMediaManifest.ps1 -Mode Production
```
