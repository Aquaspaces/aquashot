# Verifying the bundled ffmpeg

After placing `src/Aquashot/Resources/ffmpeg.exe` (see that folder's README for which
build to use), confirm it has the required encoders and capture devices.

Confirm encoders compiled in:

    ffmpeg -hide_banner -encoders | findstr /R "nvenc qsv amf libx264"

Confirm capture devices:

    ffmpeg -hide_banner -devices | findstr /R "gdigrab ddagrab"

The build embeds the binary automatically when it is present (the csproj EmbeddedResource
is conditional on the file existing). With the binary absent, the build still succeeds and
the app runs, but recording is disabled with a tray notification.
