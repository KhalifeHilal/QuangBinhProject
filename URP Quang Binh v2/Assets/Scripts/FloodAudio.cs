using UnityEngine;

/// <summary>
/// Central audio used by the learning application. Clips are loaded from
/// Assets/Resources/FloodAudio so no scene object needs manual references.
/// </summary>
public static class FloodAudio
{
    const string ResourcePrefix = "FloodAudio/";
    const string ManagerName = "FloodAudioManager";

    static AudioSource source;
    static AudioClip click;
    static AudioClip dykeBuild;
    static AudioClip dykeRemove;
    static AudioClip question;
    static AudioClip generalDiscomfort;
    static bool loaded;

    static void Ensure()
    {
        if (source == null)
        {
            GameObject manager = GameObject.Find(ManagerName);
            if (manager == null)
            {
                manager = new GameObject(ManagerName);
                Object.DontDestroyOnLoad(manager);
            }

            source = manager.GetComponent<AudioSource>();
            if (source == null) source = manager.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.mute = false;
            source.spatialBlend = 0f;
            source.loop = false;
            source.volume = .85f;
        }

        // Domain-reload-disabled Play Mode can preserve the static loaded flag
        // while the clips have been reimported or unloaded.
        if (loaded && click != null && dykeBuild != null && dykeRemove != null &&
            question != null && generalDiscomfort != null)
            return;

        loaded = true;
        click = Resources.Load<AudioClip>(ResourcePrefix + "CLICK");
        dykeBuild = Resources.Load<AudioClip>(ResourcePrefix + "Dike_bauen");
        dykeRemove = Resources.Load<AudioClip>(ResourcePrefix + "Dyke_abbauen");
        question = Resources.Load<AudioClip>(ResourcePrefix + "Questions");
        generalDiscomfort = Resources.Load<AudioClip>(ResourcePrefix + "General_Discomfort");

        PrepareClip(click);
        PrepareClip(dykeBuild);
        PrepareClip(dykeRemove);
        PrepareClip(question);
        PrepareClip(generalDiscomfort);
    }

    static void PrepareClip(AudioClip clip)
    {
        if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();
    }

    static void PlayOneShot(AudioClip clip)
    {
        Ensure();
        if (clip != null && source != null && source.isActiveAndEnabled)
            source.PlayOneShot(clip);
    }

    public static void PlayClick() => PlayOneShot(click);
    public static void PlayDykeBuilt() => PlayOneShot(dykeBuild);
    public static void PlayDykeRemoved() => PlayOneShot(dykeRemove);
    public static void PlayQuestion() => PlayOneShot(question);
    public static void PlayGeneralDiscomfort() => PlayOneShot(generalDiscomfort);

}
