using UnityEngine;

public class ScreenManager : MonoBehaviour
{
    [SerializeField] private GameObject splashScreen;
    [SerializeField] private GameObject mainScreen;
    [SerializeField] private GameObject beachesScreen;
    [SerializeField] private GameObject animalsScreen;
    [SerializeField] private GameObject aboutScreen;
    [SerializeField] private GameObject bottomNav;

    [SerializeField] private CameraFeedToInference cameraInference;

    void Start()
    {
        if (splashScreen != null) ShowSplash();
        else ShowMain();
    }

    public void ShowSplash()  => Show(splashScreen);
    public void ShowMain()    => Show(mainScreen);
    public void ShowBeaches() => Show(beachesScreen);
    public void ShowAnimals() => Show(animalsScreen);
    public void ShowAbout()   => Show(aboutScreen);

    void Show(GameObject target)
    {
        if (splashScreen  != null) splashScreen.SetActive(target == splashScreen);
        if (mainScreen    != null) mainScreen.SetActive(target == mainScreen);
        if (beachesScreen != null) beachesScreen.SetActive(target == beachesScreen);
        if (animalsScreen != null) animalsScreen.SetActive(target == animalsScreen);
        if (aboutScreen   != null) aboutScreen.SetActive(target == aboutScreen);

        if (bottomNav != null) bottomNav.SetActive(target != splashScreen);

        if (cameraInference != null)
        {
            cameraInference.inferenceEnabled = (target == mainScreen);
        }
    }
}
