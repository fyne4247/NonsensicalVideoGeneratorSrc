Adds standalone native macOS and Linux builds alongside the existing Windows build. Packages include the media tools needed for rendering, and writable library/settings data is kept outside the installed application.

The port includes native file dialogs and clipboard support, audio output following, fullscreen handling, atomic media downloads/imports, cached library previews, and a reproducible patch for the video decoder's failed-open double-free. Different files with the same name are retained with numbered filenames instead of replacing existing media.

Validation:

- macOS: 142 tests passed and the native Release build passed.
- Windows: application build passed; 121 tests passed and 21 platform-specific tests were skipped.
- Patched decoder: repeated direct loads of empty, truncated, and valid media passed on macOS and Windows.
- Apple Silicon, Intel macOS, and Linux packages were rebuilt and reviewed with the patched decoder and bundled FFmpeg/ffprobe, including MP3 encoding.

This should remain a draft until the remaining checks are complete:

- Real macOS import dialog responsiveness and cancellation.
- Generation and playback from the latest installed packages.
- Windows real-window playback, Linux GUI, and Intel macOS window checks.
- An earlier imported-library file-loss incident remains unexplained. It did not recur in a later test with roughly 250 clips and ten renders; source originals were intact. Preservation hardening and tracing are included, but the original cause is not claimed fixed.

Existing temporary-render listing and music-fade behavior are deferred. This PR does not publish installers, modify the Steam listing, or replace the Windows release.
