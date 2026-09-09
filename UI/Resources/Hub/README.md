# Season logo loading image

`season-1-logo.png` is the first decoded RGBA frame of the recovered
`HubMedia/Season_1_logo_video_1380x460.webm`. It preserves the video's 1380 × 460
canvas, lettering placement and alpha channel. The older `sharedassets44-643`
still has different internal padding and visibly changes size at the handoff.
Both the menu banner and season hub now display this frame until playback is
ready, and retain it when video is missing or fails.

Source video SHA-256: `0aa6acc0a185ed0dcb630aa8848aaffc760c93fca74ed39212131f394d313427`

PNG SHA-256: `f7c7f7c5b623513fec9e7ddf4ba9a03173926892ae74bc6be5d74425efee1837`

Reproduce with FFmpeg 7.1 from the recovered SDK assets:

```text
ffmpeg -c:v libvpx -i Season_1_logo_video_1380x460.webm -frames:v 1 -pix_fmt rgba -update 1 season-1-logo.png
```

The explicit libvpx decoder preserves WebM alpha. The PNG is embedded in the
UI assembly, so this change does not require rebuilding the asset bundle.
The source remains Battlestate Games artwork; see THIRD_PARTY_NOTICES.md.
