using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable "drop a list of videos here" component. Given a list of
/// <see cref="VideoRef"/>, it clones an inactive video-card template once per
/// entry and binds each clone's <see cref="VideoPlayerController"/> to a URL.
/// Toggles its own GameObject off when the list is empty so it contributes no
/// height to the surrounding layout.
///
/// Screen-agnostic: any screen (Animals, Beaches, About, …) can hold a
/// VideoSection and call Show()/Clear(). The card template + wiring is built by
/// VideoSectionBuilder (editor), so every screen gets an identical card.
/// </summary>
public class VideoSection : MonoBehaviour
{
    // Inactive prototype card living under this section; cloned per video.
    // Clones are appended to the template's own parent (this section).
    [SerializeField] private GameObject cardTemplate;

    private readonly List<GameObject> spawned = new List<GameObject>();

    /// <summary>Rebuild the cards for the given videos (clears any previous).</summary>
    public void Show(IReadOnlyList<VideoRef> videos)
    {
        Clear();

        bool hasVideos = videos != null && videos.Count > 0;
        gameObject.SetActive(hasVideos); // empty → no header, no layout footprint
        if (!hasVideos || cardTemplate == null) return;

        Transform parent = cardTemplate.transform.parent;
        foreach (VideoRef video in videos)
        {
            if (video == null || string.IsNullOrEmpty(video.url)) continue;
            GameObject card = Instantiate(cardTemplate, parent);
            card.SetActive(true);
            var player = card.GetComponent<VideoPlayerController>();
            if (player != null) player.Bind(video.url, video.title);
            spawned.Add(card);
        }
    }

    /// <summary>Destroy all spawned cards (each stops its own playback on destroy).</summary>
    public void Clear()
    {
        foreach (GameObject card in spawned)
            if (card != null) Destroy(card);
        spawned.Clear();
    }

    // Safety net: leaving the screen deactivates this GO and kills any streams,
    // even if the owning controller forgets to Clear().
    void OnDisable() => Clear();
}
