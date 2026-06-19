using System;
using System.IO;

namespace Aquashot.History;

// Coarse media classification used for filtering, hover-play, and the video play badge.
public enum MediaKind { Image, Gif, Video, Other }

// The top-bar file-type filter.
public enum MediaFilter { All, Images, Gif, Video }

public static class MediaKinds
{
    public static MediaKind Of(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" => MediaKind.Image,
            ".gif" => MediaKind.Gif,
            ".mp4" => MediaKind.Video,
            _ => MediaKind.Other,
        };
    }

    // True when a path passes the given file-type filter.
    public static bool Matches(MediaFilter filter, string path) => filter switch
    {
        MediaFilter.All => true,
        MediaFilter.Images => Of(path) is MediaKind.Image,
        MediaFilter.Gif => Of(path) is MediaKind.Gif,
        MediaFilter.Video => Of(path) is MediaKind.Video,
        _ => true,
    };
}
