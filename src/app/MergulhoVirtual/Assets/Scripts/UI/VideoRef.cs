/// <summary>
/// One playable video: a directly streamable HTTP(S) URL (e.g. a public GCS
/// object) plus a display title. Engine- and screen-agnostic — AnimalDef, and
/// any future beach/about data, expose a VideoRef[] that a <see cref="VideoSection"/>
/// turns into tap-to-play cards via <see cref="VideoPlayerController"/>.
/// </summary>
[System.Serializable]
public class VideoRef
{
    public string title;
    public string url;
}
