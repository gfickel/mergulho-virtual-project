/// <summary>
/// One playable video: a directly streamable HTTP(S) URL (e.g. a public GCS
/// object) plus a display title. Engine- and screen-agnostic — AnimalDef, and
/// any future beach/about data, expose a VideoRef[]. The UI Toolkit Espécie screen
/// turns them into tap-to-play cards (ISpeciesCatalog -> SpeciesInfo.Videos ->
/// IVideoPlayback); the surviving uGUI consumer is the About screen's Instagram
/// card, which drives <see cref="VideoPlayerController"/> directly.
/// </summary>
[System.Serializable]
public class VideoRef
{
    public string title;
    public string url;
}
